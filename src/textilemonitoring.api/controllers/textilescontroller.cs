
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Models;
using TextileMonitoring.API.Services;

namespace TextileMonitoring.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TextilesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IPredictionService _predictionService;

        public TextilesController(ApplicationDbContext context, IPredictionService predictionService)
        {
            _context = context;
            _predictionService = predictionService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TextileSummaryDto>>> GetTextiles(
            string? dynasty = null,
            int? status = null,
            string? search = null,
            int page = 1,
            int pageSize = 50)
        {
            var query = _context.Textiles.AsQueryable();

            if (!string.IsNullOrEmpty(dynasty))
                query = query.Where(t => t.Dynasty.Contains(dynasty));

            if (status.HasValue)
                query = query.Where(t => (int)t.Status == status.Value);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(t => t.Name.Contains(search) || t.Location.Contains(search));

            var textiles = await query
                .OrderBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TextileSummaryDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Dynasty = t.Dynasty,
                    Material = t.Material,
                    WidthCm = t.WidthCm,
                    HeightCm = t.HeightCm,
                    AreaCm2 = t.AreaCm2,
                    Location = t.Location,
                    ImageUrl = t.ImageUrl,
                    Status = (int)t.Status,
                    HoleCount = t.HoleMarkers != null ? t.HoleMarkers.Count : 0,
                    MoldRegionCount = t.MoldRegions != null ? t.MoldRegions.Count : 0
                })
                .ToListAsync();

            var textileIds = textiles.Select(t => t.Id).ToList();

            var latestDust = await _context.DustSensorData
                .Where(d => textileIds.Contains(d.TextileId))
                .GroupBy(d => d.TextileId)
                .Select(g => new
                {
                    TextileId = g.Key,
                    LatestDensity = g.OrderByDescending(x => x.ReadingTime).First().HoleDensity
                })
                .ToListAsync();

            var latestFungi = await _context.FungiSensorData
                .Where(f => textileIds.Contains(f.TextileId))
                .GroupBy(f => f.TextileId)
                .Select(g => new
                {
                    TextileId = g.Key,
                    LatestCFU = g.OrderByDescending(x => x.ReadingTime).First().FungiCFU
                })
                .ToListAsync();

            foreach (var textile in textiles)
            {
                textile.LatestHoleDensity = latestDust.FirstOrDefault(x => x.TextileId == textile.Id)?.LatestDensity ?? 0;
                textile.LatestFungiCFU = latestFungi.FirstOrDefault(x => x.TextileId == textile.Id)?.LatestCFU ?? 0;
                textile.SynergyRisk = _predictionService.CalculateSynergyRisk(textile.LatestHoleDensity, textile.LatestFungiCFU);
            }

            return Ok(textiles);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TextileDetailDto>> GetTextile(int id)
        {
            var textile = await _context.Textiles
                .Include(t => t.HoleMarkers)
                .Include(t => t.MoldRegions)
                .Include(t => t.Sensors)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (textile == null)
                return NotFound();

            var dto = new TextileDetailDto
            {
                Id = textile.Id,
                Name = textile.Name,
                Dynasty = textile.Dynasty,
                Material = textile.Material,
                Description = textile.Description,
                WidthCm = textile.WidthCm,
                HeightCm = textile.HeightCm,
                AreaCm2 = textile.AreaCm2,
                Location = textile.Location,
                ImageUrl = textile.ImageUrl,
                AcquisitionDate = textile.AcquisitionDate,
                Status = (int)textile.Status,
                CreatedAt = textile.CreatedAt,
                HoleMarkers = textile.HoleMarkers?.Select(h => new HoleMarkerDto
                {
                    Id = h.Id,
                    TextileId = h.TextileId,
                    PositionX = h.PositionX,
                    PositionY = h.PositionY,
                    RadiusMm = h.RadiusMm,
                    DetectedTime = h.DetectedTime,
                    Severity = (int)h.Severity
                }).ToList() ?? new List<HoleMarkerDto>(),
                MoldRegions = textile.MoldRegions?.Select(m => new MoldRegionDto
                {
                    Id = m.Id,
                    TextileId = m.TextileId,
                    CenterX = m.CenterX,
                    CenterY = m.CenterY,
                    RadiusMm = m.RadiusMm,
                    AreaCm2 = m.AreaCm2,
                    DetectedTime = m.DetectedTime,
                    Severity = (int)m.Severity,
                    FungiType = m.FungiType
                }).ToList() ?? new List<MoldRegionDto>(),
                Sensors = textile.Sensors?.Select(s => new SensorDto
                {
                    Id = s.Id,
                    SensorCode = s.SensorCode,
                    SensorType = (int)s.SensorType,
                    TextileId = s.TextileId,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    IsActive = s.IsActive,
                    ZigBeeAddress = s.ZigBeeAddress
                }).ToList() ?? new List<SensorDto>()
            };

            return Ok(dto);
        }

        [HttpGet("{id}/status")]
        public async Task<ActionResult<object>> GetTextileStatus(int id)
        {
            var textile = await _context.Textiles.FindAsync(id);
            if (textile == null) return NotFound();

            var latestDust = await _context.DustSensorData
                .Where(d => d.TextileId == id)
                .OrderByDescending(d => d.ReadingTime)
                .FirstOrDefaultAsync();

            var latestFungi = await _context.FungiSensorData
                .Where(f => f.TextileId == id)
                .OrderByDescending(f => f.ReadingTime)
                .FirstOrDefaultAsync();

            var holeDensity = latestDust?.HoleDensity ?? 0;
            var fungiCFU = latestFungi?.FungiCFU ?? 0;
            var synergyRisk = _predictionService.CalculateSynergyRisk(holeDensity, fungiCFU);
            var riskLevel = _predictionService.GetRiskLevel(synergyRisk);

            var alerts = await _context.Alerts
                .Where(a => a.TextileId == id && !a.Resolved)
                .CountAsync();

            return Ok(new
            {
                TextileId = id,
                textile.Name,
                Status = (int)textile.Status,
                HoleDensity = holeDensity,
                FungiCFU = fungiCFU,
                SynergyRisk = synergyRisk,
                RiskLevel = riskLevel,
                ActiveAlerts = alerts,
                LastDustUpdate = latestDust?.ReadingTime,
                LastFungiUpdate = latestFungi?.ReadingTime,
                Temperature = latestDust?.Temperature ?? latestFungi?.Temperature,
                Humidity = latestDust?.Humidity ?? latestFungi?.Humidity
            });
        }

        [HttpGet("dashboard/stats")]
        public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats()
        {
            var total = await _context.Textiles.CountAsync();
            var statusCounts = await _context.Textiles
                .GroupBy(t => t.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync();

            var normalCount = statusCounts.FirstOrDefault(s => s.Status == 0)?.Count ?? 0;
            var warningCount = statusCounts.FirstOrDefault(s => s.Status == 1)?.Count ?? 0;
            var alertCount = statusCounts.FirstOrDefault(s => s.Status >= 2)?.Count() ?? 0;

            var dustSensors = await _context.Sensors.CountAsync(s => s.SensorType == SensorType.DustSensor && s.IsActive);
            var fungiSensors = await _context.Sensors.CountAsync(s => s.SensorType == SensorType.FungiSensor && s.IsActive);

            var todayStart = DateTime.Today;
            var todayAlerts = await _context.Alerts.CountAsync(a => a.CreatedAt >= todayStart);
            var activeAlerts = await _context.Alerts.CountAsync(a => !a.Resolved);

            var totalHoles = await _context.HoleMarkers.CountAsync();
            var totalMolds = await _context.MoldRegions.CountAsync();

            var avgHole = 0m;
            var avgFungi = 0m;

            var latestDustData = await _context.DustSensorData
                .GroupBy(d => d.TextileId)
                .Select(g => g.OrderByDescending(x => x.ReadingTime).First().HoleDensity)
                .ToListAsync();

            if (latestDustData.Any())
                avgHole = Math.Round(latestDustData.Average(), 4);

            var latestFungiData = await _context.FungiSensorData
                .GroupBy(f => f.TextileId)
                .Select(g => g.OrderByDescending(x => x.ReadingTime).First().FungiCFU)
                .ToListAsync();

            if (latestFungiData.Any())
                avgFungi = Math.Round(latestFungiData.Average(), 2);

            return Ok(new DashboardStatsDto
            {
                TotalTextiles = total,
                NormalTextiles = normalCount,
                WarningTextiles = warningCount,
                AlertTextiles = alertCount,
                ActiveDustSensors = dustSensors,
                ActiveFungiSensors = fungiSensors,
                ActiveAlerts = activeAlerts,
                TodayAlerts = todayAlerts,
                TotalHoleMarkers = totalHoles,
                TotalMoldRegions = totalMolds,
                AvgHoleDensity = avgHole,
                AvgFungiCFU = avgFungi
            });
        }

        [HttpGet("dynasties")]
        public async Task<ActionResult<IEnumerable<object>>> GetDynasties()
        {
            var dynasties = await _context.Textiles
                .GroupBy(t => t.Dynasty)
                .Select(g => new { Dynasty = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            return Ok(dynasties);
        }
    }
}
