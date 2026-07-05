namespace renegotiation_service.Models;

public record ClientData(string Cpf, string Name);

public record ClientLookupResult(bool Found, ClientData? Client);

public record ContractSummary(string ContractId, string ProductType, decimal OutstandingAmount);

public record ContractsResult(bool Found, IReadOnlyList<ContractSummary> Contracts);

public record DebtItem(string DebtId, decimal Amount, DateOnly DueDate, int DaysOverdue);

public record DebtsResult(bool Found, IReadOnlyList<DebtItem> Debts);
