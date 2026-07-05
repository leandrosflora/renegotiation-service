using renegotiation_service.Clients;
using renegotiation_service.Models;

namespace renegotiation_service.Endpoints;

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
        IClientApiClient client,
        ILogger<ClientLookupLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await client.GetClientAsync(cpf, cancellationToken);
            return Results.Ok(new ClientLookupResult(data is not null, data));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Client API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(new ErrorResponse("Client API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> HandleGetContractsAsync(
        string clientId,
        IClientApiClient client,
        ILogger<ClientLookupLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var contracts = await client.GetContractsAsync(clientId, cancellationToken);
            return Results.Ok(new ContractsResult(contracts is not null, contracts ?? []));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Client API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(new ErrorResponse("Client API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> HandleGetDebtsAsync(
        string contractId,
        IClientApiClient client,
        ILogger<ClientLookupLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var debts = await client.GetDebtsAsync(contractId, cancellationToken);
            return Results.Ok(new DebtsResult(debts is not null, debts ?? []));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Client API call failed after retries ({ExceptionType})", ex.GetType().Name);
            return Results.Json(new ErrorResponse("Client API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class ClientLookupLogCategory;
