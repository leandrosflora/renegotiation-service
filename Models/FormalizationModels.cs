namespace renegotiation_service.Models;

public record AgreementData(string AgreementId);

public record AgreementConfirmationResult(bool Confirmed, string? Reason, AgreementData? Agreement);

public record DocumentResult(bool Available, string? Reason, string? DocumentUrl);
