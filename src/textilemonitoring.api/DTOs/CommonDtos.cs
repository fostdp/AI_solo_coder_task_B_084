
namespace TextileMonitoring.API.DTOs
{
    public class TextileSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Dynasty { get; set; } = string.Empty;
        public string Material { get; set; } = string.Empty;
        public decimal WidthCm { get; set; }
        public decimal HeightCm { get; set; }
        public decimal AreaCm2 { get; set; }
        public string Location { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int Status { get; set; }
        public int HoleCount { get; set; }
        public int MoldRegionCount { get; set; }
        public decimal LatestHoleDensity { get; set; }
        public decimal LatestFungiCFU { get; set; }
        public decimal SynergyRisk { get; set; }
    }

    public class TextileDetailDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Dynasty { get; set; } = string.Empty;
        public string Material { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal WidthCm { get; set; }
        public decimal HeightCm { get; set; }
        public decimal AreaCm2 { get; set; }
        public string Location { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public int Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<HoleMarkerDto> HoleMarkers { get; set; } = new();
        public List<MoldRegionDto> MoldRegions { get; set; } = new();
        public List<SensorDto> Sensors { get; set; } = new();
    }

    public class SensorDto
    {
        public int Id { get; set; }
        public string SensorCode { get; set; } = string.Empty;
        public int SensorType { get; set; }
        public int TextileId { get; set; }
        public decimal? PositionX { get; set; }
        public decimal? PositionY { get; set; }
        public bool IsActive { get; set; }
        public string ZigBeeAddress { get; set; } = string.Empty;
    }

    public class HoleMarkerDto
    {
        public int Id { get; set; }
        public int TextileId { get; set; }
        public decimal PositionX { get; set; }
        public decimal PositionY { get; set; }
        public decimal RadiusMm { get; set; }
        public DateTime DetectedTime { get; set; }
        public int Severity { get; set; }
    }

    public class MoldRegionDto
    {
        public int Id { get; set; }
        public int TextileId { get; set; }
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal RadiusMm { get; set; }
        public decimal AreaCm2 { get; set; }
        public DateTime DetectedTime { get; set; }
        public int Severity { get; set; }
        public string? FungiType { get; set; }
    }

    public class DustSensorDataDto
    {
        public long Id { get; set; }
        public int SensorId { get; set; }
        public int TextileId { get; set; }
        public DateTime ReadingTime { get; set; }
        public decimal? PM2_5 { get; set; }
        public decimal? PM10 { get; set; }
        public decimal FrassDensity { get; set; }
        public decimal? Temperature { get; set; }
        public decimal? Humidity { get; set; }
        public int HoleCount { get; set; }
        public decimal HoleDensity { get; set; }
    }

    public class FungiSensorDataDto
    {
        public long Id { get; set; }
        public int SensorId { get; set; }
        public int TextileId { get; set; }
        public DateTime ReadingTime { get; set; }
        public decimal SporeCount { get; set; }
        public decimal FungiCFU { get; set; }
        public decimal? Temperature { get; set; }
        public decimal? Humidity { get; set; }
        public string? DominantFungiType { get; set; }
    }

    public class ZigBeeDustPayloadDto
    {
        public string SensorCode { get; set; } = string.Empty;
        public string ZigBeeAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public decimal PM2_5 { get; set; }
        public decimal PM10 { get; set; }
        public decimal FrassDensity { get; set; }
        public decimal Temperature { get; set; }
        public decimal Humidity { get; set; }
        public int HoleCount { get; set; }
        public int SignalStrength { get; set; }
    }

    public class ZigBeeFungiPayloadDto
    {
        public string SensorCode { get; set; } = string.Empty;
        public string ZigBeeAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public decimal SporeCount { get; set; }
        public decimal FungiCFU { get; set; }
        public decimal Temperature { get; set; }
        public decimal Humidity { get; set; }
        public string DominantFungiType { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
    }

    public class AlertDto
    {
        public long Id { get; set; }
        public int TextileId { get; set; }
        public string TextileName { get; set; } = string.Empty;
        public int AlertType { get; set; }
        public int AlertLevel { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public decimal? HoleDensity { get; set; }
        public decimal? FungiCFU { get; set; }
        public decimal? SynergyRisk { get; set; }
        public decimal Threshold { get; set; }
        public decimal ActualValue { get; set; }
        public bool DingTalkPushed { get; set; }
        public bool EmailPushed { get; set; }
        public DateTime? PushedAt { get; set; }
        public bool Acknowledged { get; set; }
        public bool Resolved { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PredictionResultDto
    {
        public int TextileId { get; set; }
        public string TextileName { get; set; } = string.Empty;
        public int Model { get; set; }
        public int HorizonDays { get; set; }
        public List<PredictionPointDto> DataPoints { get; set; } = new();
        public int RiskLevel { get; set; }
        public decimal? Confidence { get; set; }
    }

    public class PredictionPointDto
    {
        public DateTime Date { get; set; }
        public decimal? PredictedHoleDensity { get; set; }
        public decimal? PredictedFungiCFU { get; set; }
        public decimal? PredictedSynergyRisk { get; set; }
        public decimal? PredatorDensity { get; set; }
        public decimal? PredationRate { get; set; }
    }

    public class DashboardStatsDto
    {
        public int TotalTextiles { get; set; }
        public int NormalTextiles { get; set; }
        public int WarningTextiles { get; set; }
        public int AlertTextiles { get; set; }
        public int ActiveDustSensors { get; set; }
        public int ActiveFungiSensors { get; set; }
        public int ActiveAlerts { get; set; }
        public int TodayAlerts { get; set; }
        public int TotalHoleMarkers { get; set; }
        public int TotalMoldRegions { get; set; }
        public decimal AvgHoleDensity { get; set; }
        public decimal AvgFungiCFU { get; set; }
    }

    public class ZigBeeDustPayloadDto
    {
        public string SensorCode { get; set; } = string.Empty;
        public string ZigBeeAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public decimal PM2_5 { get; set; }
        public decimal PM10 { get; set; }
        public decimal FrassDensity { get; set; }
        public decimal Temperature { get; set; }
        public decimal Humidity { get; set; }
        public int HoleCount { get; set; }
        public short SignalStrength { get; set; }
    }

    public class ZigBeeFungiPayloadDto
    {
        public string SensorCode { get; set; } = string.Empty;
        public string ZigBeeAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public decimal SporeCount { get; set; }
        public decimal FungiCFU { get; set; }
        public decimal Temperature { get; set; }
        public decimal Humidity { get; set; }
        public string DominantFungiType { get; set; } = string.Empty;
        public short SignalStrength { get; set; }
    }
}
