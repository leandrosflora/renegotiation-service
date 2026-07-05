using renegotiation_service.Clients;
using renegotiation_service.Models;

namespace renegotiation_service.Endpoints;

public static class EligibilityEndpoints
{
    public static IEndpointRouteBuilder MapEligibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/contracts/{contractId}/eligibility", HandleGetEligibilityAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetEligibilityAsync(
        string contractId,
        IEligibilityApiClient client,
        ILogger<EligibilityLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.CheckEligibilityAsync(contractId, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Eligibility API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(
                new ErrorResponse("Eligibility API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class EligibilityLogCategory;
