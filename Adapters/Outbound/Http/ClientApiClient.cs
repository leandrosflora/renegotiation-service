using System.Net;
using System.Net.Http.Json;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Adapters.Outbound.Http;

public class ClientApiClient(HttpClient httpClient) : IClientApiClient
{
    public async Task<ClientData?> GetClientAsync(string cpf, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/clients/{cpf}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClientData>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ContractSummary>?> GetContractsAsync(string clientId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/clients/{clientId}/contracts", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ContractSummary>>(cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<DebtItem>?> GetDebtsAsync(string contractId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/contracts/{contractId}/debts", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DebtItem>>(cancellationToken: cancellationToken)
               ?? [];
    }
}
