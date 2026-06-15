
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.Models;

namespace TextileMonitoring.API.Services
{
    public interface IAlertService
    {
        Task<Alert?> CheckAndCreateHoleAlert(int textileId, decimal holeDensity, CancellationToken ct = default);
        Task<Alert?> CheckAndCreateFungiAlert(int textileId, decimal fungiCFU, CancellationToken ct = default);
        Task<Alert?> CheckAndCreateSynergyAlert(int textileId, decimal synergyRisk, decimal holeDensity, decimal fungiCFU, CancellationToken ct = default);
        Task<List<Alert>> GetActiveAlerts(int? textileId = null, int page = 1, int pageSize = 50);
        Task<bool> AcknowledgeAlert(long alertId, string userName);
        Task<bool> ResolveAlert(long alertId, string remarks = "");
    }

    public class AlertService : IAlertService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAlertNotificationService _notificationService;
        private readonly IPredictionService _predictionService;
        private readonly ILogger<AlertService> _logger;

        public AlertService(
            ApplicationDbContext context,
            IAlertNotificationService notificationService,
            IPredictionService predictionService,
            ILogger<AlertService> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _predictionService = predictionService;
            _logger = logger;
        }

        public async Task<Alert?> CheckAndCreateHoleAlert(int textileId, decimal holeDensity, CancellationToken ct = default)
        {
            var (threshold, warningThreshold) = await GetThresholds("HoleDensityThreshold", "WarningHoleDensity", 5.0m, 3.0m);

            if (holeDensity < warningThreshold)
                return null;

            var existingActive = await _context.Alerts
                .Where(a => a.TextileId == textileId
                    && a.AlertType == AlertType.HoleDensity
                    && !a.Resolved
                    && a.CreatedAt >= DateTime.Now.AddHours(-24))
                .FirstOrDefaultAsync(ct);

            if (existingActive != null)
                return existingActive;

            var textile = await _context.Textiles.FindAsync(new object[] { textileId }, ct);
            var isCritical = holeDensity >= threshold;

            var alert = new Alert
            {
                TextileId = textileId,
                AlertType = AlertType.HoleDensity,
                AlertLevel = isCritical ? AlertLevel.Critical : AlertLevel.Warning,
                Title = isCritical ? "虫蛀孔洞密度严重超标" : "虫蛀孔洞密度预警",
                Message = GenerateHoleMessage(holeDensity, threshold, textile?.Name),
                HoleDensity = holeDensity,
                Threshold = threshold,
                ActualValue = holeDensity,
                CreatedAt = DateTime.Now
            };

            _context.Alerts.Add(alert);

            if (textile != null)
            {
                textile.Status = isCritical ? TextileStatus.Critical : TextileStatus.Warning;
                textile.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync(ct);

            _ = _notificationService.PushAll(alert, ct);

            _logger.LogWarning("虫蛀告警: Textile={TextileId}, Density={Density:N4}, Critical={Critical}",
                textileId, holeDensity, isCritical);

            return alert;
        }

        public async Task<Alert?> CheckAndCreateFungiAlert(int textileId, decimal fungiCFU, CancellationToken ct = default)
        {
            var (threshold, warningThreshold) = await GetThresholds("FungiCFUThreshold", "WarningFungiCFU", 300.0m, 200.0m);

            if (fungiCFU < warningThreshold)
                return null;

            var existingActive = await _context.Alerts
                .Where(a => a.TextileId == textileId
                    && a.AlertType == AlertType.FungiCFU
                    && !a.Resolved
                    && a.CreatedAt >= DateTime.Now.AddHours(-24))
                .FirstOrDefaultAsync(ct);

            if (existingActive != null)
                return existingActive;

            var textile = await _context.Textiles.FindAsync(new object[] { textileId }, ct);
            var isCritical = fungiCFU >= threshold;

            var alert = new Alert
            {
                TextileId = textileId,
                AlertType = AlertType.FungiCFU,
                AlertLevel = isCritical ? AlertLevel.Critical : AlertLevel.Warning,
                Title = isCritical ? "霉菌浓度严重超标" : "霉菌浓度预警",
                Message = GenerateFungiMessage(fungiCFU, threshold, textile?.Name),
                FungiCFU = fungiCFU,
                Threshold = threshold,
                ActualValue = fungiCFU,
                CreatedAt = DateTime.Now
            };

            _context.Alerts.Add(alert);

            if (textile != null)
            {
                var currentStatus = (int)textile.Status;
                var newStatus = isCritical ? (int)TextileStatus.Critical : (int)TextileStatus.Warning;
                if (newStatus > currentStatus)
                {
                    textile.Status = (TextileStatus)newStatus;
                    textile.UpdatedAt = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync(ct);

            _ = _notificationService.PushAll(alert, ct);

            _logger.LogWarning("霉菌告警: Textile={TextileId}, CFU={CFU:N2}, Critical={Critical}",
                textileId, fungiCFU, isCritical);

            return alert;
        }

        public async Task<Alert?> CheckAndCreateSynergyAlert(int textileId, decimal synergyRisk, decimal holeDensity, decimal fungiCFU, CancellationToken ct = default)
        {
            var synergyThresholdStr = await _context.AlertConfigs
                .FirstOrDefaultAsync(c => c.ConfigKey == "SynergyRiskThreshold", ct);
            decimal synergyThreshold = 50.0m;
            if (synergyThresholdStr != null && decimal.TryParse(synergyThresholdStr.ConfigValue, out var t))
                synergyThreshold = t;

            if (synergyRisk < synergyThreshold * 0.8m)
                return null;

            var existingActive = await _context.Alerts
                .Where(a => a.TextileId == textileId
                    && a.AlertType == AlertType.SynergyRisk
                    && !a.Resolved
                    && a.CreatedAt >= DateTime.Now.AddHours(-24))
                .FirstOrDefaultAsync(ct);

            if (existingActive != null)
                return existingActive;

            var textile = await _context.Textiles.FindAsync(new object[] { textileId }, ct);
            var isCritical = synergyRisk >= synergyThreshold;

            var alert = new Alert
            {
                TextileId = textileId,
                AlertType = AlertType.SynergyRisk,
                AlertLevel = isCritical ? AlertLevel.Critical : AlertLevel.Warning,
                Title = isCritical ? "虫蛀-霉变协同风险严重" : "协同风险预警",
                Message = GenerateSynergyMessage(synergyRisk, synergyThreshold, holeDensity, fungiCFU, textile?.Name),
                HoleDensity = holeDensity,
                FungiCFU = fungiCFU,
                SynergyRisk = synergyRisk,
                Threshold = synergyThreshold,
                ActualValue = synergyRisk,
                CreatedAt = DateTime.Now
            };

            _context.Alerts.Add(alert);

            if (textile != null)
            {
                var currentStatus = (int)textile.Status;
                var newStatus = isCritical ? (int)TextileStatus.Critical : (int)TextileStatus.Alert;
                if (newStatus > currentStatus)
                {
                    textile.Status = (TextileStatus)newStatus;
                    textile.UpdatedAt = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync(ct);

            _ = _notificationService.PushAll(alert, ct);

            _logger.LogWarning("协同告警: Textile={TextileId}, Risk={Risk:N4}, Critical={Critical}",
                textileId, synergyRisk, isCritical);

            return alert;
        }

        public async Task<List<Alert>> GetActiveAlerts(int? textileId = null, int page = 1, int pageSize = 50)
        {
            var query = _context.Alerts.AsQueryable();

            if (textileId.HasValue)
                query = query.Where(a => a.TextileId == textileId.Value);

            return await query
                .Include(a => a.Textile)
                .Where(a => !a.Resolved)
                .OrderByDescending(a => a.AlertLevel)
                .ThenByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<bool> AcknowledgeAlert(long alertId, string userName)
        {
            var alert = await _context.Alerts.FindAsync(alertId);
            if (alert == null) return false;

            alert.Acknowledged = true;
            alert.AcknowledgedBy = userName;
            alert.AcknowledgedAt = DateTime.Now;
            alert.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResolveAlert(long alertId, string remarks = "")
        {
            var alert = await _context.Alerts.FindAsync(alertId);
            if (alert == null) return false;

            alert.Resolved = true;
            alert.ResolvedAt = DateTime.Now;
            if (!string.IsNullOrEmpty(remarks))
                alert.Remarks = remarks;

            await _context.SaveChangesAsync();

            var unresolved = await _context.Alerts
                .Where(a => a.TextileId == alert.TextileId && !a.Resolved)
                .AnyAsync();

            if (!unresolved)
            {
                var textile = await _context.Textiles.FindAsync(alert.TextileId);
                if (textile != null)
                {
                    textile.Status = TextileStatus.Normal;
                    textile.UpdatedAt = DateTime.Now;
                    await _context.SaveChangesAsync();
                }
            }

            return true;
        }

        private async Task<(decimal Threshold, decimal WarningThreshold)> GetThresholds(
            string thresholdKey, string warningKey, decimal defaultThreshold, decimal defaultWarning)
        {
            decimal threshold = defaultThreshold;
            decimal warning = defaultWarning;

            var configs = await _context.AlertConfigs
                .Where(c => c.ConfigKey == thresholdKey || c.ConfigKey == warningKey)
                .ToDictionaryAsync(c => c.ConfigKey, c => c.ConfigValue);

            if (configs.TryGetValue(thresholdKey, out var tStr) && decimal.TryParse(tStr, out var t))
                threshold = t;

            if (configs.TryGetValue(warningKey, out var wStr) && decimal.TryParse(wStr, out var w))
                warning = w;

            return (threshold, warning);
        }

        private static string GenerateHoleMessage(decimal holeDensity, decimal threshold, string? textileName)
        {
            var exceed = ((holeDensity - threshold) / threshold * 100);
            return $"织绣品【{textileName ?? "未知"}】检测到虫蛀孔洞密度为 {holeDensity:N4} 个/100cm²，" +
                   $"超过告警阈值 {threshold:N2} 个/100cm²，超出 {exceed:N1}%。" +
                   $"检测到蛀虫侵蚀迹象，建议立即进行：1)隔离检查 2)低温或熏蒸灭虫 3)清理蛀虫排泄物 4)评估织物受损情况。";
        }

        private static string GenerateFungiMessage(decimal fungiCFU, decimal threshold, string? textileName)
        {
            var exceed = ((fungiCFU - threshold) / threshold * 100);
            return $"织绣品【{textileName ?? "未知"}】检测到霉菌浓度为 {fungiCFU:N2} CFU/g，" +
                   $"超过告警阈值 {threshold:N2} CFU/g，超出 {exceed:N1}%。" +
                   $"检测到真菌大量繁殖，建议立即进行：1)隔离处理 2)调整温湿度(降温降湿) 3)消毒杀菌处理 4)检查霉斑扩散范围。";
        }

        private static string GenerateSynergyMessage(decimal synergyRisk, decimal threshold, decimal holeDensity, decimal fungiCFU, string? textileName)
        {
            var exceed = ((synergyRisk - threshold) / threshold * 100);
            return $"织绣品【{textileName ?? "未知"}】虫蛀-霉变协同风险指数为 {synergyRisk:N4}，超过阈值 {threshold:N2}，超出 {exceed:N1}%。" +
                   $"当前虫蛀密度 {holeDensity:N4} 个/100cm²，霉菌浓度 {fungiCFU:N2} CFU/g。" +
                   $"⚠️ 两种危害协同作用可能加速文物损坏！建议采取紧急综合保护措施：全面隔离、环境调控、专业修复评估。";
        }
    }
}
