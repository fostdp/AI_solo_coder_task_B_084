
namespace TextileMonitoring.Contracts.Messages;

public record VocSensorDataReceived
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SensorCode { get; init; } = string.Empty;
    public int TextileId { get; init; }

    public double ToluenePPB { get; init; }
    public double XylenePPB { get; init; }
    public double EthylbenzenePPB { get; init; }
    public double FormaldehydePPB { get; init; }
    public double AcetaldehydePPB { get; init; }
    public double 1Octen3OlPPB { get; init; }
    public double GeosminPPT { get; init; }
    public double 2MethylisoborneolPPT { get; init; }
    public double TotalVolatilePPB { get; init; }

    public double? Temperature { get; init; }
    public double? Humidity { get; init; }
    public double AirflowMetered { get; init; }
    public short SignalStrength { get; init; }
    public int SensorStatus { get; init; }
    public string? RawPayload { get; init; }
}

public enum MoldSpeciesFromVoc
{
    Unknown = 0,
    AspergillusNiger = 1,        // 黑曲霉
    PenicilliumChrysogenum = 2,  // 产黄青霉
    CladosporiumHerbarum = 3,    // 多主枝孢
    AlternariaAlternata = 4,     // 链格孢
    TrichodermaViride = 5,       // 绿色木霉
    FusariumGraminearum = 6      // 禾谷镰刀菌
}
