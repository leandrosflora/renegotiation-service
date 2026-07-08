namespace renegotiation_service.Configuration;

public class OtelOptions
{
    public const string SectionName = "Otel";

    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}
