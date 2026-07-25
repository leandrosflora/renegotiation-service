using renegotiation_service.Application;
using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Domain;
using renegotiation_service.Platform;

namespace renegotiation_service.Adapters.Inbound.Http;

public static class SimulationEndpoints
{
    // Mirrors tool-service-renegotiation's SIMULATION_STAGES exactly - this is the same
    // journey_stage claim from the same signed JWT, defense-in-depth re-checked independently
    // here rather than trusted from the caller. Started/IdentificationPending/CustomerIdentified/
    // ContractSelectionPending included so a single-contract customer's very first message can
    // reach a proactively-offered simulation in the same turn as identification+eligibility, not
    // just tool-service's own gate.
    private static readonly HashSet<string> AllowedStages =
    [
        "Started",
        "IdentificationPending",
        "CustomerIdentified",
        "ContractSelectionPending",
        "ContractSelected",
        "EligibilityChecked",
        "SimulationParametersPending"
    ];

    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/contracts/{contractId}/simulations", HandleSimulateAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSimulateAsync(
        string contractId,
        SimulationRequest request,
        HttpContext httpContext,
        ISimulateUseCase useCase,
        ILogger<SimulationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        }
        if (!GovernedToolPolicy.TryAuthorize(
                httpContext,
                "simular_proposta",
                AllowedStages,
                idempotencyKey,
                requireExplicitConfirmation: false,
                out _,
                out var policyError))
        {
            return Results.Json(new { error = policyError }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                contractId,
                request,
                idempotencyKey,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (IdempotencyInProgressException ex)
        {
            return Results.Conflict(new { error = ex.Message, retryable = true });
        }
        catch (IdempotencyConflictException ex)
        {
            return Results.Conflict(new { error = ex.Message, retryable = false });
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(
                new ErrorResponse("Contracting API unavailable"),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class SimulationLogCategory;
