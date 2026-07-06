using renegotiation_service.Application;
using renegotiation_service.Application.Ports.Inbound;

namespace renegotiation_service.Adapters.Inbound.Http;

public static class ClientLookupEndpoints
{
    public static IEndpointRouteBuilder MapClientLookupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/clients/{cpf}", HandleGetClientAsync);
        endpoints.MapGet("/clients/{clientId}/contracts", HandleGetContractsAsync);
        endpoints.MapGet("/contracts/{contractId}/debts", HandleGetDebtsAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetClientAsync(
        string cpf,
        IGetClientUseCase useCase,
        ILogger<ClientLookupLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(cpf, cancellationToken);
            return Results.Ok(result);
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed after retries ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(new ErrorResponse("Client API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> HandleGetContractsAsync(
        string clientId,
        IGetContractsUseCase useCase,
        ILogger<ClientLookupLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(clientId, cancellationToken);
            return Results.Ok(result);
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed after retries ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(new ErrorResponse("Client API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> HandleGetDebtsAsync(
        string contractId,
        IGetDebtsUseCase useCase,
        ILogger<ClientLookupLogCategory> logger,
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
            return Results.Json(new ErrorResponse("Client API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class ClientLookupLogCategory;
