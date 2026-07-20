using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using JsonWebToken = Microsoft.IdentityModel.JsonWebTokens.JsonWebToken;

namespace renegotiation_service.Platform;

public sealed class InternalAuthOptions
{
    public const string SectionName = "InternalAuth";
    public string Issuer { get; init; } = "conversational-ai-platform";
    public string ServiceName { get; init; } = "renegotiation-service";
    public int TokenTtlSeconds { get; init; } = 300;
    public Dictionary<string, string> OutboundSecrets { get; init; } = new();
    public Dictionary<string, string> InboundSecrets { get; init; } = new();

    public static bool HasValidSecret(string? secret) =>
        !string.IsNullOrEmpty(secret) && Encoding.UTF8.GetByteCount(secret) >= 32;
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
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            release();
        }
    }
}

public sealed class InternalTokenService(IOptions<InternalAuthOptions> options)
{
    public string CreateToken(string audience, string tenantId)
    {
        var value = options.Value;
        if (!value.OutboundSecrets.TryGetValue(audience, out var secret) || !InternalAuthOptions.HasValidSecret(secret))
        {
            throw new InvalidOperationException(
                $"InternalAuth:OutboundSecrets:{audience} must be configured with at least 32 UTF-8 bytes.");
        }
        if (!TenantContext.TryNormalize(tenantId, out var canonicalTenant))
        {
            throw new ArgumentException("Tenant ID must be a non-empty UUID.", nameof(tenantId));
        }
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        var header = new JwtHeader(credentials)
        {
            [JwtHeaderParameterNames.Kid] = value.ServiceName
        };
        var payload = new JwtPayload(
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
            issuedAt: now);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}

public sealed class InternalRequestHandler(
    InternalTokenService tokenService,
    TenantContext tenantContext,
    IOptions<InternalAuthOptions> authOptions) : DelegatingHandler
{
    private const string CoreBancarioAudience = "core-bancario-mock";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        // core-bancario-mock has no internal-auth validation (out of scope for
        // per-service internal-auth secrets), so renegotiation-service has no configured
        // OutboundSecrets entry for it. Only attach a Bearer token if one is ever configured
        // for this audience; otherwise skip signing rather than fail the outbound call.
        if (authOptions.Value.OutboundSecrets.ContainsKey(CoreBancarioAudience))
        {
            request.Headers.Authorization = new("Bearer", tokenService.CreateToken(CoreBancarioAudience, tenantId));
        }
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
        foreach (var item in _values.OrderBy(item => item.Key))
        {
            output.Append(item.Key).Append(' ').Append(item.Value).AppendLine();
        }
        foreach (var item in _sums.OrderBy(item => item.Key))
        {
            output.Append(item.Key).Append(' ')
                .Append(item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine();
        }
        return output.ToString();
    }

    private static string Key(string name, params (string Name, string Value)[] labels)
    {
        if (labels.Length == 0) return name;
        var rendered = string.Join(",", labels.Select(label =>
            $"{Regex.Replace(label.Name, "[^a-zA-Z0-9_:]", "_")}=\"{label.Value.Replace("\"", "\\\"")}\""));
        return $"{name}{{{rendered}}}";
    }
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
                IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                {
                    if (kid is null
                        || !auth.InboundSecrets.TryGetValue(kid, out var secret)
                        || !InternalAuthOptions.HasValidSecret(secret))
                    {
                        return Array.Empty<SecurityKey>();
                    }
                    return new SecurityKey[] { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) };
                },
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Sub
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var kid = context.SecurityToken switch
                    {
                        JsonWebToken jsonWebToken => jsonWebToken.Kid,
                        JwtSecurityToken jwtSecurityToken => jwtSecurityToken.Header.Kid,
                        _ => null
                    };
                    var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                    if (!string.Equals(kid, sub, StringComparison.Ordinal))
                    {
                        context.HttpContext.RequestServices.GetRequiredService<PlatformMetrics>()
                            .Increment("renegotiation_internal_auth_failures_total", ("reason", "kid_sub_mismatch"));
                        context.Fail("Token kid header does not match sub claim.");
                    }
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<PlatformMetrics>()
                        .Increment("renegotiation_internal_auth_failures_total", ("reason", "authentication_failed"));
                    return Task.CompletedTask;
                }
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
