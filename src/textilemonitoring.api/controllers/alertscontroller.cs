
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
    public class AlertsController : ControllerBase
    {
        private readonly IAlertService _alertService;
        private readonly ApplicationDbContext _context;

        public AlertsController(IAlertService alertService, ApplicationDbContext context)
        {
            _alertService = alertService;
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AlertDto>>> GetAlerts(
            int? textileId = null,
            int? alertLevel = null,
            int? alertType = null,
            bool? resolved = false,
            int page = 1,
            int pageSize = 50)
        {
            var query = _context.Alerts
                .Include(a => a.Textile)
                .AsQueryable();

            if (textileId.HasValue)
                query = query.Where(a => a.TextileId == textileId.Value);

            if (alertLevel.HasValue)
                query = query.Where(a => (int)a.AlertLevel == alertLevel.Value);

            if (alertType.HasValue)
                query = query.Where(a => (int)a.AlertType == alertType.Value);

            if (resolved.HasValue)
                query = query.Where(a => a.Resolved == resolved.Value);

            var alerts = await query
                .OrderByDescending(a => a.AlertLevel)
                .ThenByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AlertDto
                {
                    Id = a.Id,
                    TextileId = a.TextileId,
                    TextileName = a.Textile != null ? a.Textile.Name : string.Empty,
                    AlertType = (int)a.AlertType,
                    AlertLevel = (int)a.AlertLevel,
                    Title = a.Title,
                    Message = a.Message,
                    HoleDensity = a.HoleDensity,
                    FungiCFU = a.FungiCFU,
                    SynergyRisk = a.SynergyRisk,
                    Threshold = a.Threshold,
                    ActualValue = a.ActualValue,
                    DingTalkPushed = a.DingTalkPushed,
                    EmailPushed = a.EmailPushed,
                    PushedAt = a.PushedAt,
                    Acknowledged = a.Acknowledged,
                    Resolved = a.Resolved,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return Ok(alerts);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AlertDto>> GetAlert(long id)
        {
            var alert = await _context.Alerts
                .Include(a => a.Textile)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (alert == null)
                return NotFound();

            return Ok(new AlertDto
            {
                Id = alert.Id,
                TextileId = alert.TextileId,
                TextileName = alert.Textile != null ? alert.Textile.Name : string.Empty,
                AlertType = (int)alert.AlertType,
                AlertLevel = (int)alert.AlertLevel,
                Title = alert.Title,
                Message = alert.Message,
                HoleDensity = alert.HoleDensity,
                FungiCFU = alert.FungiCFU,
                SynergyRisk = alert.SynergyRisk,
                Threshold = alert.Threshold,
                ActualValue = alert.ActualValue,
                DingTalkPushed = alert.DingTalkPushed,
                EmailPushed = alert.EmailPushed,
                PushedAt = alert.PushedAt,
                Acknowledged = alert.Acknowledged,
                Resolved = alert.Resolved,
                CreatedAt = alert.CreatedAt
            });
        }

        [HttpGet("active")]
        public async Task<ActionResult<IEnumerable<AlertDto>>> GetActiveAlerts(int? textileId = null)
        {
            var alerts = await _alertService.GetActiveAlerts(textileId);

            var dtos = alerts.Select(a => new AlertDto
            {
                Id = a.Id,
                TextileId = a.TextileId,
                TextileName = a.Textile != null ? a.Textile.Name : string.Empty,
                AlertType = (int)a.AlertType,
                AlertLevel = (int)a.AlertLevel,
                Title = a.Title,
                Message = a.Message,
                HoleDensity = a.HoleDensity,
                FungiCFU = a.FungiCFU,
                SynergyRisk = a.SynergyRisk,
                Threshold = a.Threshold,
                ActualValue = a.ActualValue,
                DingTalkPushed = a.DingTalkPushed,
                EmailPushed = a.EmailPushed,
                PushedAt = a.PushedAt,
                Acknowledged = a.Acknowledged,
                Resolved = a.Resolved,
                CreatedAt = a.CreatedAt
            }).ToList();

            return Ok(dtos);
        }

        [HttpPost("{id}/acknowledge")]
        public async Task<IActionResult> AcknowledgeAlert(long id, [FromBody] AcknowledgeRequest request)
        {
            var result = await _alertService.AcknowledgeAlert(id, request.UserName);
            if (!result)
                return NotFound();

            return Ok(new { success = true, message = "告警已确认" });
        }

        [HttpPost("{id}/resolve")]
        public async Task<IActionResult> ResolveAlert(long id, [FromBody] ResolveRequest request)
        {
            var result = await _alertService.ResolveAlert(id, request.Remarks ?? string.Empty);
            if (!result)
                return NotFound();

            return Ok(new { success = true, message = "告警已解决" });
        }

        [HttpGet("stats/summary")]
        public async Task<ActionResult<object>> GetAlertStats()
        {
            var todayStart = DateTime.Today;
            var weekStart = DateTime.Today.AddDays(-7);
            var monthStart = DateTime.Today.AddDays(-30);

            var todayCount = await _context.Alerts.CountAsync(a => a.CreatedAt >= todayStart);
            var weekCount = await _context.Alerts.CountAsync(a => a.CreatedAt >= weekStart);
            var monthCount = await _context.Alerts.CountAsync(a => a.CreatedAt >= monthStart);

            var activeCount = await _context.Alerts.CountAsync(a => !a.Resolved);
            var criticalCount = await _context.Alerts.CountAsync(a => !a.Resolved && a.AlertLevel == AlertLevel.Critical);

            var byType = await _context.Alerts
                .Where(a => a.CreatedAt >= monthStart)
                .GroupBy(a => a.AlertType)
                .Select(g => new { Type = (int)g.Key, Count = g.Count() })
                .ToListAsync();

            var byLevel = await _context.Alerts
                .Where(a => a.CreatedAt >= monthStart)
                .GroupBy(a => a.AlertLevel)
                .Select(g => new { Level = (int)g.Key, Count = g.Count() })
                .ToListAsync();

            var topTextiles = await _context.Alerts
                .Where(a => a.CreatedAt >= monthStart)
                .GroupBy(a => new { a.TextileId, a.Textile.Name })
                .Select(g => new { TextileId = g.Key.TextileId, Name = g.Key.Name, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToListAsync();

            return Ok(new
            {
                Today = todayCount,
                ThisWeek = weekCount,
                ThisMonth = monthCount,
                Active = activeCount,
                Critical = criticalCount,
                ByType = byType,
                ByLevel = byLevel,
                TopTextiles = topTextiles
            });
        }
    }

    public class AcknowledgeRequest
    {
        public string UserName { get; set; } = string.Empty;
    }

    public class ResolveRequest
    {
        public string? Remarks { get; set; }
    }
}
