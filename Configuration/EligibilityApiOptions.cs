namespace renegotiation_service.Configuration;

public class EligibilityApiOptions
{
    public const string SectionName = "EligibilityApi";

    public string BaseUrl { get; set; } = string.Empty;
    public int RetryAttempts { get; set; } = 2;
}
