namespace renegotiation_service.Configuration;

public class FormalizationApiOptions
{
    public const string SectionName = "FormalizationApi";

    public string BaseUrl { get; set; } = string.Empty;
    public int RetryAttempts { get; set; } = 2;
}
