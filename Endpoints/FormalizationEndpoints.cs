using renegotiation_service.Clients;
using renegotiation_service.Models;

namespace renegotiation_service.Endpoints;

public static class FormalizationEndpoints
{
    public static IEndpointRouteBuilder MapFormalizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/simulations/{simulationId}/confirmations", HandleConfirmAsync);
        endpoints.MapGet("/agreements/{agreementId}/document", HandleGetDocumentAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleConfirmAsync(
        string simulationId,
        IFormalizationApiClient client,
        ILogger<FormalizationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.ConfirmAsync(simulationId, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Formalization API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(
                new ErrorResponse("Formalization API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> HandleGetDocumentAsync(
        string agreementId,
        IFormalizationApiClient client,
        ILogger<FormalizationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.GetDocumentAsync(agreementId, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Formalization API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(
                new ErrorResponse("Formalization API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class FormalizationLogCategory;
