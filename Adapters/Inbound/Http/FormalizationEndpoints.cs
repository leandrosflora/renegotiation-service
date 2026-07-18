using renegotiation_service.Application;
using renegotiation_service.Application.Ports.Inbound;

namespace renegotiation_service.Adapters.Inbound.Http;

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
        HttpRequest httpRequest,
        IConfirmAgreementUseCase useCase,
        ILogger<FormalizationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        }

        try
        {
            var result = await useCase.ExecuteAsync(simulationId, idempotencyKey, cancellationToken);
            return Results.Ok(result);
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(
                new ErrorResponse("Formalization API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> HandleGetDocumentAsync(
        string agreementId,
        IGetDocumentUseCase useCase,
        ILogger<FormalizationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(agreementId, cancellationToken);
            return Results.Ok(result);
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed after retries ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(
                new ErrorResponse("Formalization API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class FormalizationLogCategory;
