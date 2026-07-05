using renegotiation_service.Clients;
using renegotiation_service.Models;

namespace renegotiation_service.Endpoints;

public static class SimulationEndpoints
{
    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/contracts/{contractId}/simulations", HandleSimulateAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSimulateAsync(
        string contractId,
        SimulationRequest request,
        IContractingApiClient client,
        ILogger<SimulationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.SimulateAsync(contractId, request, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Contracting API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(
                new ErrorResponse("Contracting API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class SimulationLogCategory;
