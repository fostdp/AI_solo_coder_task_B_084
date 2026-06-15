
namespace TextileMonitoring.Contracts.Messages;

public record FrassImageCaptured
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SensorCode { get; init; } = string.Empty;
    public int TextileId { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public int PixelDepth { get; init; } = 8;
    public double Magnification { get; init; } = 40.0;
    public double AverageParticleArea { get; init; }
    public double ParticleCount { get; init; }
    public double MeanGrayscale { get; init; }
    public double TextureEntropy { get; init; }
    public double EllipticityMean { get; init; }
    public double AspectRatioMean { get; init; }
    public double SolidityMean { get; init; }
    public double FrassDensityCorrelated { get; init; }
    public short SignalStrength { get; init; }
    public double? Temperature { get; init; }
    public double? Humidity { get; init; }
}

public enum PestSpecies
{
    Unknown = 0,
    LepismaSaccharina = 1,       // 衣鱼 (Silverfish)
    CtenolepismaLongicaudata = 2, // 毛衣鱼 (Firebrat)
    AttagenusPellio = 3,         // 黑毛皮蠹 (Black carpet beetle)
    TineolaBisselliella = 4,     // 衣蛾 (Clothes moth)
    AnthrenusVerbasci = 5        // 标本皮蠹 (Varied carpet beetle)
}
