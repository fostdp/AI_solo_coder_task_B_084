
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Models;

namespace TextileMonitoring.API.Services
{
    public interface ISensorDataService
    {
        Task<bool> ReceiveDustData(ZigBeeDustPayloadDto payload, CancellationToken ct = default);
        Task<bool> ReceiveFungiData(ZigBeeFungiPayloadDto payload, CancellationToken ct = default);
        Task<List<DustSensorDataDto>> GetDustHistory(int textileId, DateTime? start = null, DateTime? end = null, int limit = 100);
        Task<List<FungiSensorDataDto>> GetFungiHistory(int textileId, DateTime? start = null, DateTime? end = null, int limit = 100);
        Task<int> BatchImportDustDataAsync(List<ZigBeeDustPayloadDto> payloads, CancellationToken ct = default);
        Task<int> BatchImportFungiDataAsync(List<ZigBeeFungiPayloadDto> payloads, CancellationToken ct = default);
    }

    public class SensorDataService : ISensorDataService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAlertService _alertService;
        private readonly IPredictionService _predictionService;
        private readonly ILogger<SensorDataService> _logger;

        public SensorDataService(
            ApplicationDbContext context,
            IAlertService alertService,
            IPredictionService predictionService,
            ILogger<SensorDataService> logger)
        {
            _context = context;
            _alertService = alertService;
            _predictionService = predictionService;
            _logger = logger;
        }

        public async Task<bool> ReceiveDustData(ZigBeeDustPayloadDto payload, CancellationToken ct = default)
        {
            try
            {
                var sensor = await _context.Sensors
                    .FirstOrDefaultAsync(s => s.SensorCode == payload.SensorCode, ct);

                if (sensor == null || sensor.SensorType != SensorType.DustSensor || !sensor.IsActive)
                {
                    _logger.LogWarning("收到无效粉尘传感器数据: SensorCode={Code}", payload.SensorCode);
                    return false;
                }

                var textile = await _context.Textiles.FindAsync(new object[] { sensor.TextileId }, ct);
                if (textile == null) return false;

                decimal areaCm2 = textile.AreaCm2 > 0 ? textile.AreaCm2 : 1000m;
                decimal holeDensityPer100 = (payload.HoleCount / areaCm2) * 100m;

                var data = new DustSensorData
                {
                    SensorId = sensor.Id,
                    TextileId = sensor.TextileId,
                    ReadingTime = payload.Timestamp == default ? DateTime.Now : payload.Timestamp,
                    PM2_5 = payload.PM2_5,
                    PM10 = payload.PM10,
                    FrassDensity = payload.FrassDensity,
                    Temperature = payload.Temperature,
                    Humidity = payload.Humidity,
                    HoleCount = payload.HoleCount,
                    HoleDensity = Math.Round(holeDensityPer100, 4),
                    ZigBeeSignalStrength = payload.SignalStrength,
                    CreatedAt = DateTime.Now
                };

                _context.DustSensorData.Add(data);

                if (payload.HoleCount > 0)
                {
                    var existingHoles = await _context.HoleMarkers
                        .CountAsync(h => h.TextileId == sensor.TextileId, ct);

                    int newHoles = Math.Max(0, payload.HoleCount - existingHoles);
                    Random random = new Random(Guid.NewGuid().GetHashCode());

                    for (int i = 0; i < newHoles; i++)
                    {
                        var hole = new HoleMarker
                        {
                            TextileId = sensor.TextileId,
                            DustDataId = data.Id,
                            PositionX = (decimal)(random.NextDouble() * 100),
                            PositionY = (decimal)(random.NextDouble() * 100),
                            RadiusMm = 0.5m + (decimal)(random.NextDouble() * 4),
                            DetectedTime = data.ReadingTime,
                            Severity = (SeverityLevel)(random.Next(3)),
                            CreatedAt = DateTime.Now
                        };
                        _context.HoleMarkers.Add(hole);
                    }
                }

                await _context.SaveChangesAsync(ct);

                await _alertService.CheckAndCreateHoleAlert(sensor.TextileId, holeDensityPer100, ct);

                var latestFungi = await _context.FungiSensorData
                    .Where(f => f.TextileId == sensor.TextileId)
                    .OrderByDescending(f => f.ReadingTime)
                    .FirstOrDefaultAsync(ct);

                if (latestFungi != null)
                {
                    var synergyRisk = _predictionService.CalculateSynergyRisk(holeDensityPer100, latestFungi.FungiCFU);
                    await _alertService.CheckAndCreateSynergyAlert(sensor.TextileId, synergyRisk, holeDensityPer100, latestFungi.FungiCFU, ct);
                }

                _logger.LogInformation("粉尘数据接收成功: Sensor={Code}, Textile={TextileId}, Holes={Holes}, Density={Density:N4}",
                    payload.SensorCode, sensor.TextileId, payload.HoleCount, holeDensityPer100);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收粉尘传感器数据异常");
                return false;
            }
        }

        public async Task<bool> ReceiveFungiData(ZigBeeFungiPayloadDto payload, CancellationToken ct = default)
        {
            try
            {
                var sensor = await _context.Sensors
                    .FirstOrDefaultAsync(s => s.SensorCode == payload.SensorCode, ct);

                if (sensor == null || sensor.SensorType != SensorType.FungiSensor || !sensor.IsActive)
                {
                    _logger.LogWarning("收到无效真菌传感器数据: SensorCode={Code}", payload.SensorCode);
                    return false;
                }

                var data = new FungiSensorData
                {
                    SensorId = sensor.Id,
                    TextileId = sensor.TextileId,
                    ReadingTime = payload.Timestamp == default ? DateTime.Now : payload.Timestamp,
                    SporeCount = payload.SporeCount,
                    FungiCFU = payload.FungiCFU,
                    Temperature = payload.Temperature,
                    Humidity = payload.Humidity,
                    DominantFungiType = payload.DominantFungiType,
                    ZigBeeSignalStrength = payload.SignalStrength,
                    CreatedAt = DateTime.Now
                };

                _context.FungiSensorData.Add(data);

                if (payload.FungiCFU >= 150)
                {
                    var textile = await _context.Textiles.FindAsync(new object[] { sensor.TextileId }, ct);
                    Random random = new Random(Guid.NewGuid().GetHashCode());

                    if (payload.FungiCFU >= 250)
                    {
                        int newRegions = 1 + random.Next(3);
                        for (int i = 0; i < newRegions; i++)
                        {
                            var radius = 1.0m + (decimal)(random.NextDouble() * 8);
                            var region = new MoldRegion
                            {
                                TextileId = sensor.TextileId,
                                FungiDataId = data.Id,
                                CenterX = (decimal)(random.NextDouble() * 100),
                                CenterY = (decimal)(random.NextDouble() * 100),
                                RadiusMm = radius,
                                AreaCm2 = Math.Round((decimal)(Math.PI * (double)radius * (double)radius) / 100m, 4),
                                DetectedTime = data.ReadingTime,
                                Severity = (SeverityLevel)Math.Min(2, (int)(payload.FungiCFU / 200)),
                                FungiType = payload.DominantFungiType,
                                CreatedAt = DateTime.Now
                            };
                            _context.MoldRegions.Add(region);
                        }
                    }
                }

                await _context.SaveChangesAsync(ct);

                await _alertService.CheckAndCreateFungiAlert(sensor.TextileId, payload.FungiCFU, ct);

                var latestDust = await _context.DustSensorData
                    .Where(d => d.TextileId == sensor.TextileId)
                    .OrderByDescending(d => d.ReadingTime)
                    .FirstOrDefaultAsync(ct);

                if (latestDust != null)
                {
                    var synergyRisk = _predictionService.CalculateSynergyRisk(latestDust.HoleDensity, payload.FungiCFU);
                    await _alertService.CheckAndCreateSynergyAlert(sensor.TextileId, synergyRisk, latestDust.HoleDensity, payload.FungiCFU, ct);
                }

                _logger.LogInformation("真菌数据接收成功: Sensor={Code}, Textile={TextileId}, CFU={CFU:N2}",
                    payload.SensorCode, sensor.TextileId, payload.FungiCFU);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收真菌传感器数据异常");
                return false;
            }
        }

        public async Task<List<DustSensorDataDto>> GetDustHistory(int textileId, DateTime? start = null, DateTime? end = null, int limit = 100)
        {
            var query = _context.DustSensorData.AsQueryable();

            if (textileId > 0)
                query = query.Where(d => d.TextileId == textileId);

            if (start.HasValue)
                query = query.Where(d => d.ReadingTime >= start.Value);

            if (end.HasValue)
                query = query.Where(d => d.ReadingTime <= end.Value);

            return await query
                .OrderByDescending(d => d.ReadingTime)
                .Take(limit)
                .Select(d => new DustSensorDataDto
                {
                    Id = d.Id,
                    SensorId = d.SensorId,
                    TextileId = d.TextileId,
                    ReadingTime = d.ReadingTime,
                    PM2_5 = d.PM2_5,
                    PM10 = d.PM10,
                    FrassDensity = d.FrassDensity,
                    Temperature = d.Temperature,
                    Humidity = d.Humidity,
                    HoleCount = d.HoleCount,
                    HoleDensity = d.HoleDensity
                })
                .OrderBy(d => d.ReadingTime)
                .ToListAsync();
        }

        public async Task<List<FungiSensorDataDto>> GetFungiHistory(int textileId, DateTime? start = null, DateTime? end = null, int limit = 100)
        {
            var query = _context.FungiSensorData.AsQueryable();

            if (textileId > 0)
                query = query.Where(f => f.TextileId == textileId);

            if (start.HasValue)
                query = query.Where(f => f.ReadingTime >= start.Value);

            if (end.HasValue)
                query = query.Where(f => f.ReadingTime <= end.Value);

            return await query
                .OrderByDescending(f => f.ReadingTime)
                .Take(limit)
                .Select(f => new FungiSensorDataDto
                {
                    Id = f.Id,
                    SensorId = f.SensorId,
                    TextileId = f.TextileId,
                    ReadingTime = f.ReadingTime,
                    SporeCount = f.SporeCount,
                    FungiCFU = f.FungiCFU,
                    Temperature = f.Temperature,
                    Humidity = f.Humidity,
                    DominantFungiType = f.DominantFungiType
                })
                .OrderBy(f => f.ReadingTime)
                .ToListAsync();
        }

        public async Task<int> BatchImportDustDataAsync(List<ZigBeeDustPayloadDto> payloads, CancellationToken ct = default)
        {
            if (payloads == null || payloads.Count == 0) return 0;

            var sensorCodes = payloads.Select(p => p.SensorCode).Distinct().ToList();
            var sensors = await _context.Sensors
                .Where(s => sensorCodes.Contains(s.SensorCode) && s.SensorType == SensorType.DustSensor && s.IsActive)
                .Include(s => s.Textile)
                .ToDictionaryAsync(s => s.SensorCode, s => s, ct);

            var dataList = new List<DustSensorData>(payloads.Count);
            var perTextileHoleCounts = new Dictionary<int, int>();

            foreach (var p in payloads)
            {
                if (!sensors.TryGetValue(p.SensorCode, out var sensor) || sensor.Textile == null)
                    continue;

                var textile = sensor.Textile;
                decimal areaCm2 = textile.AreaCm2 > 0 ? textile.AreaCm2 : 1000m;
                decimal holeDensityPer100 = (p.HoleCount / areaCm2) * 100m;

                dataList.Add(new DustSensorData
                {
                    SensorId = sensor.Id,
                    TextileId = sensor.TextileId,
                    ReadingTime = p.Timestamp == default ? DateTime.Now : p.Timestamp,
                    PM2_5 = p.PM2_5,
                    PM10 = p.PM10,
                    FrassDensity = p.FrassDensity,
                    Temperature = p.Temperature,
                    Humidity = p.Humidity,
                    HoleCount = p.HoleCount,
                    HoleDensity = Math.Round(holeDensityPer100, 4),
                    ZigBeeSignalStrength = p.SignalStrength,
                    CreatedAt = DateTime.Now
                });

                if (p.HoleCount > 0)
                    perTextileHoleCounts[sensor.TextileId] = p.HoleCount;
            }

            if (dataList.Count == 0) return 0;

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            await _context.DustSensorData.AddRangeAsync(dataList, ct);
            int inserted = await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            foreach (var kvp in perTextileHoleCounts)
            {
                var existingHoles = await _context.HoleMarkers
                    .CountAsync(h => h.TextileId == kvp.Key, ct);
                int newHoles = Math.Max(0, kvp.Value - existingHoles);
                if (newHoles > 0)
                {
                    var markers = new List<HoleMarker>(newHoles);
                    var rand = new Random(Guid.NewGuid().GetHashCode());
                    for (int i = 0; i < newHoles; i++)
                    {
                        markers.Add(new HoleMarker
                        {
                            TextileId = kvp.Key,
                            RelativeX = Math.Round((decimal)(rand.NextDouble() * 80 + 10), 2),
                            RelativeY = Math.Round((decimal)(rand.NextDouble() * 80 + 10), 2),
                            RadiusMm = Math.Round((decimal)(0.8 + rand.NextDouble() * 4), 2),
                            SeverityLevel = 1 + rand.Next(4),
                            DetectedAt = DateTime.Now,
                            Status = 1,
                            CreatedAt = DateTime.Now
                        });
                    }
                    _context.HoleMarkers.AddRange(markers);
                }
            }
            if (perTextileHoleCounts.Count > 0)
                await _context.SaveChangesAsync(ct);

            _logger.LogInformation("ZigBee批量导入粉尘数据: {Count} 条入库", inserted);
            return inserted;
        }

        public async Task<int> BatchImportFungiDataAsync(List<ZigBeeFungiPayloadDto> payloads, CancellationToken ct = default)
        {
            if (payloads == null || payloads.Count == 0) return 0;

            var sensorCodes = payloads.Select(p => p.SensorCode).Distinct().ToList();
            var sensors = await _context.Sensors
                .Where(s => sensorCodes.Contains(s.SensorCode) && s.SensorType == SensorType.FungiSensor && s.IsActive)
                .ToDictionaryAsync(s => s.SensorCode, s => s, ct);

            var dataList = new List<FungiSensorData>(payloads.Count);
            var perTextileMolds = new Dictionary<int, (double cfu, List<ZigBeeFungiPayloadDto> payloads)>();

            foreach (var p in payloads)
            {
                if (!sensors.TryGetValue(p.SensorCode, out var sensor))
                    continue;

                dataList.Add(new FungiSensorData
                {
                    SensorId = sensor.Id,
                    TextileId = sensor.TextileId,
                    ReadingTime = p.Timestamp == default ? DateTime.Now : p.Timestamp,
                    SporeCount = p.SporeCount,
                    FungiCFU = p.FungiCFU,
                    Temperature = p.Temperature,
                    Humidity = p.Humidity,
                    DominantFungiType = p.DominantFungiType,
                    ZigBeeSignalStrength = p.SignalStrength,
                    CreatedAt = DateTime.Now
                });

                if (p.FungiCFU >= 200)
                {
                    if (!perTextileMolds.ContainsKey(sensor.TextileId))
                        perTextileMolds[sensor.TextileId] = (0, new List<ZigBeeFungiPayloadDto>());
                    var entry = perTextileMolds[sensor.TextileId];
                    entry.cfu += (double)p.FungiCFU;
                    entry.payloads.Add(p);
                    perTextileMolds[sensor.TextileId] = entry;
                }
            }

            if (dataList.Count == 0) return 0;

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            await _context.FungiSensorData.AddRangeAsync(dataList, ct);
            int inserted = await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            foreach (var kvp in perTextileMolds)
            {
                var (totalCfu, ps) = kvp.Value;
                var avgCfu = totalCfu / ps.Count;
                if (avgCfu >= 200)
                {
                    var rand = new Random(Guid.NewGuid().GetHashCode());
                    var region = new MoldRegion
                    {
                        TextileId = kvp.Key,
                        RelativeX = Math.Round((decimal)(rand.NextDouble() * 60 + 20), 2),
                        RelativeY = Math.Round((decimal)(rand.NextDouble() * 60 + 20), 2),
                        RadiusMm = Math.Round((decimal)(15 + Math.Min(60, avgCfu / 8)), 1),
                        DominantFungiType = ps.FirstOrDefault()?.DominantFungiType ?? "Aspergillus",
                        DetectedAt = DateTime.Now,
                        Status = 1,
                        CreatedAt = DateTime.Now
                    };
                    _context.MoldRegions.Add(region);
                }
            }
            if (perTextileMolds.Count > 0)
                await _context.SaveChangesAsync(ct);

            _logger.LogInformation("ZigBee批量导入真菌数据: {Count} 条入库", inserted);
            return inserted;
        }
    }
}
