using renegotiation_service.Application;
using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Domain;
using renegotiation_service.Platform;

namespace renegotiation_service.Adapters.Inbound.Http;

public static class SimulationEndpoints
{
    private static readonly HashSet<string> AllowedStages =
    [
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
