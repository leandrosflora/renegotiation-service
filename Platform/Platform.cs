using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace renegotiation_service.Platform;

public sealed class InternalAuthOptions
{
    public const string SectionName = "InternalAuth";
    public string Issuer { get; init; } = "conversational-ai-platform";
    public string ServiceName { get; init; } = "renegotiation-service";
    public string SigningKey { get; init; } = string.Empty;
    public int TokenTtlSeconds { get; init; } = 300;
}

public sealed class TenantContext
{
    public const string ClaimType = "tenant_id";
    private static readonly AsyncLocal<string?> Current = new();
    public string TenantId => Current.Value ?? throw new InvalidOperationException("Tenant context unavailable.");

    public IDisposable Push(string tenantId)
    {
        if (!TryNormalize(tenantId, out var canonical))
        {
            throw new ArgumentException("Tenant ID must be a non-empty UUID.", nameof(tenantId));
        }
        var previous = Current.Value;
        Current.Value = canonical;
        return new Scope(() => Current.Value = previous);
    }

    public static bool TryNormalize(string? tenantId, out string canonical)
    {
        canonical = string.Empty;
        if (!Guid.TryParse(tenantId?.Trim(), out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }
        canonical = parsed.ToString("D");
        return true;
    }

    private sealed class Scope(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

public sealed class InternalTokenService(IOptions<InternalAuthOptions> options)
{
    public string CreateToken(string audience, string tenantId)
    {
        var value = options.Value;
        if (Encoding.UTF8.GetByteCount(value.SigningKey) < 32)
        {
            throw new InvalidOperationException("InternalAuth:SigningKey must contain at least 32 UTF-8 bytes.");
        }
        if (!TenantContext.TryNormalize(tenantId, out var canonicalTenant))
        {
            throw new ArgumentException("Tenant ID must be a non-empty UUID.", nameof(tenantId));
        }
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: value.Issuer,
            audience: audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, value.ServiceName),
                new Claim(TenantContext.ClaimType, canonicalTenant),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
            ],
            notBefore: now,
            expires: now.AddSeconds(Math.Clamp(value.TokenTtlSeconds, 30, 900)),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value.SigningKey)),
                SecurityAlgorithms.HmacSha256)));
    }
}

public sealed class InternalRequestHandler(
    InternalTokenService tokenService,
    TenantContext tenantContext) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        request.Headers.Authorization = new("Bearer", tokenService.CreateToken("core-bancario-mock", tenantId));
        request.Headers.Remove("X-Tenant-Id");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId);
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class PlatformMetrics
{
    private readonly ConcurrentDictionary<string, long> _values = new();
    private readonly ConcurrentDictionary<string, double> _sums = new();
    public void Increment(string name, params (string Name, string Value)[] labels) =>
        _values.AddOrUpdate(Key(name, labels), 1, (_, value) => value + 1);
    public void Observe(string name, double seconds, params (string Name, string Value)[] labels) =>
        _sums.AddOrUpdate(Key(name, labels), seconds, (_, value) => value + seconds);
    public string Render()
    {
        var output = new StringBuilder();
        foreach (var item in _values.OrderBy(item => item.Key)) output.Append(item.Key).Append(' ').Append(item.Value).AppendLine();
        foreach (var item in _sums.OrderBy(item => item.Key)) output.Append(item.Key).Append(' ').Append(item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        return output.ToString();
    }
    private static string Key(string name, params (string Name, string Value)[] labels) => labels.Length == 0
        ? name
        : $"{name}{{{string.Join(',', labels.Select(label => $"{Regex.Replace(label.Name, "[^a-zA-Z0-9_:]", "_")}=\"{label.Value.Replace("\"", "\\\"")}\""))}}}";
}

public sealed class PlatformMiddleware(RequestDelegate next, TenantContext tenantContext, PlatformMetrics metrics)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        IDisposable? tenantScope = null;
        try
        {
            if (context.User.Identity?.IsAuthenticated == true
                && !context.Request.Path.StartsWithSegments("/health")
                && !context.Request.Path.StartsWithSegments("/metrics"))
            {
                var headerTenant = context.Request.Headers["X-Tenant-Id"].ToString();
                var claimTenant = context.User.FindFirstValue(TenantContext.ClaimType);
                if (!TenantContext.TryNormalize(headerTenant, out var canonicalHeader)
                    || !TenantContext.TryNormalize(claimTenant, out var canonicalClaim))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = "Tenant must be a signed non-empty UUID." });
                    return;
                }
                if (!string.Equals(canonicalHeader, canonicalClaim, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = "X-Tenant-Id does not match signed tenant_id claim." });
                    return;
                }
                tenantScope = tenantContext.Push(canonicalClaim);
            }
            await next(context);
        }
        finally
        {
            tenantScope?.Dispose();
            stopwatch.Stop();
            metrics.Increment("renegotiation_http_requests_total",
                ("method", context.Request.Method),
                ("status", context.Response.StatusCode.ToString()));
            metrics.Observe("renegotiation_http_duration_seconds", stopwatch.Elapsed.TotalSeconds,
                ("method", context.Request.Method));
        }
    }
}

public static class PlatformExtensions
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(InternalAuthOptions.SectionName).Get<InternalAuthOptions>() ?? new();
        services.Configure<InternalAuthOptions>(configuration.GetSection(InternalAuthOptions.SectionName));
        services.AddSingleton<TenantContext>();
        services.AddSingleton<InternalTokenService>();
        services.AddSingleton<PlatformMetrics>();
        var key = Encoding.UTF8.GetByteCount(auth.SigningKey) >= 32
            ? auth.SigningKey
            : "invalid-missing-internal-auth-signing-key-32-bytes";
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = auth.Issuer,
                ValidateAudience = true,
                ValidAudience = auth.ServiceName,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Sub
            };
        });
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(JwtRegisteredClaimNames.Sub)
                .RequireClaim(TenantContext.ClaimType)
                .Build());
        return services;
    }

    public static WebApplication UsePlatform(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PlatformMiddleware>();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/metrics", (PlatformMetrics metrics) => Results.Text(metrics.Render(), "text/plain; version=0.0.4")).AllowAnonymous();
        return app;
    }
}
