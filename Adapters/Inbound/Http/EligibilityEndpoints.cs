using renegotiation_service.Application;
using renegotiation_service.Application.Ports.Inbound;

namespace renegotiation_service.Adapters.Inbound.Http;

public static class EligibilityEndpoints
{
    public static IEndpointRouteBuilder MapEligibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/contracts/{contractId}/eligibility", HandleGetEligibilityAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetEligibilityAsync(
        string contractId,
        IGetEligibilityUseCase useCase,
        ILogger<EligibilityLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(contractId, cancellationToken);
            return Results.Ok(result);
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed after retries ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(
                new ErrorResponse("Eligibility API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class EligibilityLogCategory;
