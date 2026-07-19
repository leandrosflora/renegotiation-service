using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace renegotiation_service.Platform;

public sealed record GovernedToolContext(
    string TenantId,
    string ConversationId,
    string MessageId,
    string JourneyStage,
    long JourneyVersion,
    string? ConfirmationMessageId,
    string PolicyId);

public static class GovernedToolPolicy
{
    public static bool TryAuthorize(
        HttpContext httpContext,
        string expectedTool,
        IReadOnlySet<string> allowedStages,
        string idempotencyKey,
        bool requireExplicitConfirmation,
        out GovernedToolContext? context,
        out string error)
    {
        context = null;
        error = string.Empty;
        var principal = httpContext.User;

        if (!string.Equals(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                "tool-service-renegotiation",
                StringComparison.Ordinal))
        {
            error = "Only tool-service-renegotiation may invoke governed domain operations.";
            return false;
        }
        if (!string.Equals(principal.FindFirstValue("token_use"), "governed_tool", StringComparison.Ordinal))
        {
            error = "A governed_tool token is required.";
            return false;
        }
        if (!string.Equals(principal.FindFirstValue("tool_name"), expectedTool, StringComparison.Ordinal))
        {
            error = "Signed tool_name does not match the requested operation.";
            return false;
        }

        var tenantId = principal.FindFirstValue(TenantContext.ClaimType);
        var conversationId = principal.FindFirstValue("conversation_id");
        var messageId = principal.FindFirstValue("message_id");
        var journeyStage = principal.FindFirstValue("journey_stage");
        var journeyVersionText = principal.FindFirstValue("journey_version");
        var confirmationMessageId = principal.FindFirstValue("confirmation_message_id");
        var policyId = principal.FindFirstValue("policy_id");

        if (!TenantContext.TryNormalize(tenantId, out var canonicalTenant)
            || string.IsNullOrWhiteSpace(conversationId)
            || string.IsNullOrWhiteSpace(messageId)
            || string.IsNullOrWhiteSpace(journeyStage)
            || !long.TryParse(journeyVersionText, out var journeyVersion)
            || journeyVersion < 0
            || string.IsNullOrWhiteSpace(policyId))
        {
            error = "Signed governed tool context is incomplete.";
            return false;
        }
        if (!allowedStages.Contains(journeyStage))
        {
            error = $"Operation '{expectedTool}' is not allowed from journey stage '{journeyStage}'.";
            return false;
        }
        if (!string.Equals(policyId, idempotencyKey, StringComparison.Ordinal))
        {
            error = "Idempotency-Key does not match the signed policy decision.";
            return false;
        }
        if (requireExplicitConfirmation
            && (string.IsNullOrWhiteSpace(confirmationMessageId)
                || !string.Equals(confirmationMessageId, messageId, StringComparison.Ordinal)))
        {
            error = "Explicit confirmation evidence for the current message is required.";
            return false;
        }

        context = new GovernedToolContext(
            canonicalTenant,
            conversationId,
            messageId,
            journeyStage,
            journeyVersion,
            confirmationMessageId,
            policyId);
        return true;
    }
}
