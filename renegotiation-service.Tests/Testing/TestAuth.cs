using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace renegotiation_service.Tests.Testing;

/// <summary>
/// Mirrors renegotiation-service's own InternalTokenService (Platform/PlatformServices.cs) so
/// WebApplicationFactory-based endpoint tests can mint a JWT that satisfies the mandatory
/// FallbackPolicy (RequireAuthenticatedUser) instead of bypassing auth entirely.
/// </summary>
public static class TestAuth
{
    public const string SigningKey = "test-only-internal-auth-signing-key-32-bytes-min";
    public const string Issuer = "conversational-ai-platform";
    public const string Audience = "renegotiation-service";
    public const string TenantId = "00000000-0000-0000-0000-000000000001";

    public static void ConfigureSigningKey(IWebHostBuilder builder) =>
        builder.UseSetting("InternalAuth:SigningKey", SigningKey);

    public static string IssueToken()
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "test-caller"),
                new Claim("tenant_id", TenantId)
            ],
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Mirrors the "governed_tool" token tool-service-renegotiation signs for
    /// simular_proposta/confirmar_acordo calls (see Platform/GovernedToolPolicy.cs), so endpoint
    /// tests can exercise the real authorization path instead of bypassing it.
    /// </summary>
    public static string IssueGovernedToolToken(
        string toolName,
        string journeyStage,
        string policyId,
        string conversationId = "5511999990000",
        string messageId = "wamid.governed-1",
        long journeyVersion = 0,
        string? confirmationMessageId = null)
    {
        var now = DateTime.UtcNow;
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, "tool-service-renegotiation"),
            new Claim("tenant_id", TenantId),
            new Claim("token_use", "governed_tool"),
            new Claim("tool_name", toolName),
            new Claim("conversation_id", conversationId),
            new Claim("message_id", messageId),
            new Claim("journey_stage", journeyStage),
            new Claim("journey_version", journeyVersion.ToString()),
            new Claim("policy_id", policyId)
        ];
        if (confirmationMessageId is not null)
        {
            claims.Add(new Claim("confirmation_message_id", confirmationMessageId));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
