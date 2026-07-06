namespace renegotiation_service.Application;

/// <summary>
/// Raised by a use case when a downstream Core Bancário API call fails. Carries only the
/// service name and the inner exception's type, never its message — the message can embed
/// the request URL (e.g. a CPF), so it must never reach logs.
/// </summary>
public sealed class UpstreamServiceUnavailableException(string serviceName, Exception inner)
    : Exception($"{serviceName} unavailable ({inner.GetType().Name})", inner)
{
    public string ServiceName { get; } = serviceName;
}
