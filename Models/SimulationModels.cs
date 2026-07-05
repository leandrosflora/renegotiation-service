using System.Text.Json.Serialization;

namespace renegotiation_service.Models;

public class SimulationRequest
{
    [JsonPropertyName("installments")]
    public int Installments { get; init; }

    [JsonPropertyName("discount_percentage")]
    public double DiscountPercentage { get; init; }
}

public record SimulationData(string SimulationId, int Installments, decimal InstallmentAmount, decimal TotalAmount);

public record SimulationResult(bool Possible, string? Reason, SimulationData? Simulation);
