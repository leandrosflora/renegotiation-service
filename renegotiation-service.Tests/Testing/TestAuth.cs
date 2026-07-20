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
/// renegotiation-service only has one configured inbound caller (tool-service-renegotiation,
/// see InternalAuth:InboundSecrets:tool-service-renegotiation), so every token minted here is
/// signed with that pair's secret and carries kid == sub == "tool-service-renegotiation".
/// </summary>
public static class TestAuth
{
    public const string InboundSecret = "test-only-tool-service-renegotiation-inbound-secret-32b";
    public const string Issuer = "conversational-ai-platform";
    public const string Audience = "renegotiation-service";
    public const string CallerServiceName = "tool-service-renegotiation";
    public const string TenantId = "00000000-0000-0000-0000-000000000001";

    public static void ConfigureInboundSecret(IWebHostBuilder builder) =>
        builder.UseSetting($"InternalAuth:InboundSecrets:{CallerServiceName}", InboundSecret);

    public static string IssueToken()
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            BuildHeader(),
            new JwtPayload(
                issuer: Issuer,
                audience: Audience,
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, CallerServiceName),
                    new Claim("tenant_id", TenantId)
                ],
                notBefore: now,
                expires: now.AddMinutes(5),
                issuedAt: now));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static JwtHeader BuildHeader() =>
        new(new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InboundSecret)),
            SecurityAlgorithms.HmacSha256))
        {
            [JwtHeaderParameterNames.Kid] = CallerServiceName
        };

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
            new Claim(JwtRegisteredClaimNames.Sub, CallerServiceName),
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
            BuildHeader(),
            new JwtPayload(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(5),
                issuedAt: now));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
