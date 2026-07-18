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
    private static readonly AsyncLocal<string?> Current = new();
    public string TenantId => Current.Value ?? throw new InvalidOperationException("Tenant context unavailable.");
    public IDisposable Push(string tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Scope(() => Current.Value = previous);
    }
    private sealed class Scope(Action release) : IDisposable { public void Dispose() => release(); }
}

public sealed class InternalTokenService(IOptions<InternalAuthOptions> options)
{
    public string CreateToken(string audience)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.SigningKey))
            throw new InvalidOperationException("InternalAuth:SigningKey is required.");
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: value.Issuer,
            audience: audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, value.ServiceName)],
            notBefore: now,
            expires: now.AddSeconds(Math.Max(30, value.TokenTtlSeconds)),
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
        request.Headers.Authorization = new("Bearer", tokenService.CreateToken("core-bancario-mock"));
        request.Headers.Remove("X-Tenant-Id");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantContext.TenantId);
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
                var tenantId = context.Request.Headers["X-Tenant-Id"].ToString();
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "X-Tenant-Id header is required." });
                    return;
                }
                tenantScope = tenantContext.Push(tenantId);
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
        var key = string.IsNullOrWhiteSpace(auth.SigningKey) ? "invalid-missing-internal-auth-signing-key" : auth.SigningKey;
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = auth.Issuer,
                ValidateAudience = true,
                ValidAudience = auth.ServiceName,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            });
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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
