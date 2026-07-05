namespace renegotiation_service.Configuration;

public class ContractingApiOptions
{
    public const string SectionName = "ContractingApi";

    public string BaseUrl { get; set; } = string.Empty;
    public int RetryAttempts { get; set; } = 2;
}
