namespace renegotiation_service.Configuration;

public class ClientApiOptions
{
    public const string SectionName = "ClientApi";

    public string BaseUrl { get; set; } = string.Empty;
    public int RetryAttempts { get; set; } = 2;
}
