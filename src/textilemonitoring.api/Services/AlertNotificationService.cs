
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.Models;

namespace TextileMonitoring.API.Services
{
    public interface IAlertNotificationService
    {
        Task<bool> PushDingTalk(Alert alert, CancellationToken cancellationToken = default);
        Task<bool> PushEmail(Alert alert, CancellationToken cancellationToken = default);
        Task PushAll(Alert alert, CancellationToken cancellationToken = default);
    }

    public class AlertNotificationService : IAlertNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AlertNotificationService> _logger;
        private readonly IConfiguration _configuration;

        public AlertNotificationService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<AlertNotificationService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _configuration = configuration;
        }

        public async Task PushAll(Alert alert, CancellationToken cancellationToken = default)
        {
            var dingTalkTask = PushDingTalk(alert, cancellationToken);
            var emailTask = PushEmail(alert, cancellationToken);

            await Task.WhenAll(dingTalkTask, emailTask);

            if (dingTalkTask.Result || emailTask.Result)
            {
                alert.PushedAt = DateTime.Now;
                alert.DingTalkPushed = dingTalkTask.Result;
                alert.EmailPushed = emailTask.Result;
                _context.Alerts.Update(alert);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<bool> PushDingTalk(Alert alert, CancellationToken cancellationToken = default)
        {
            try
            {
                var webhookConfig = await GetConfigValue("DingTalkWebhook");
                var secretConfig = await GetConfigValue("DingTalkSecret");

                if (string.IsNullOrEmpty(webhookConfig) || webhookConfig.Contains("YOUR_ACCESS_TOKEN"))
                {
                    _logger.LogWarning("钉钉Webhook未配置，跳过钉钉推送");
                    return false;
                }

                string webhookUrl = webhookConfig;

                if (!string.IsNullOrEmpty(secretConfig) && !secretConfig.Contains("YOUR_SECRET"))
                {
                    webhookUrl = BuildDingTalkSignedUrl(webhookConfig, secretConfig);
                }

                var textile = await _context.Textiles.FindAsync(alert.TextileId);
                string textileName = textile?.Name ?? $"织绣品#{alert.TextileId}";
                string location = textile?.Location ?? "未知位置";

                var alertLevelText = alert.AlertLevel switch
                {
                    AlertLevel.Warning => "⚠️ 警告",
                    AlertLevel.Critical => "🔴 紧急",
                    _ => "ℹ️ 通知"
                };

                var alertTypeText = alert.AlertType switch
                {
                    AlertType.HoleDensity => "虫蛀密度超标",
                    AlertType.FungiCFU => "霉菌浓度超标",
                    AlertType.SynergyRisk => "协同风险指数超标",
                    _ => "异常"
                };

                var markdownText = new StringBuilder();
                markdownText.AppendLine($"## {alertLevelText}｜{alert.Title}");
                markdownText.AppendLine();
                markdownText.AppendLine($"> **类型**: {alertTypeText}");
                markdownText.AppendLine($"> **织绣品**: {textileName}");
                markdownText.AppendLine($"> **位置**: {location}");
                markdownText.AppendLine($"> **触发时间**: {alert.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                markdownText.AppendLine();
                markdownText.AppendLine("### 详细信息");
                markdownText.AppendLine($"- **告警阈值**: {alert.Threshold:N2}");
                markdownText.AppendLine($"- **当前数值**: {alert.ActualValue:N2}");
                if (alert.HoleDensity.HasValue)
                    markdownText.AppendLine($"- **虫蛀孔洞密度**: {alert.HoleDensity.Value:N4} 个/100cm²");
                if (alert.FungiCFU.HasValue)
                    markdownText.AppendLine($"- **霉菌浓度**: {alert.FungiCFU.Value:N2} CFU/g");
                if (alert.SynergyRisk.HasValue)
                    markdownText.AppendLine($"- **协同风险指数**: {alert.SynergyRisk.Value:N4}");
                markdownText.AppendLine();
                markdownText.AppendLine("### 描述");
                markdownText.AppendLine(alert.Message);
                markdownText.AppendLine();
                markdownText.AppendLine("---");
                markdownText.AppendLine("*请及时检查并采取相应保护措施*");

                var payload = new
                {
                    msgtype = "markdown",
                    markdown = new
                    {
                        title = $"{alertLevelText} {alert.Title}",
                        text = markdownText.ToString()
                    },
                    at = new
                    {
                        isAtAll = alert.AlertLevel == AlertLevel.Critical
                    }
                };

                var jsonContent = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(webhookUrl, jsonContent, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonConvert.DeserializeObject<DingTalkResponse>(responseContent);

                    if (result?.ErrCode == 0)
                    {
                        _logger.LogInformation("钉钉推送成功: Alert {AlertId}", alert.Id);
                        return true;
                    }

                    _logger.LogWarning("钉钉推送返回错误: Errcode={ErrCode}, Errmsg={ErrMsg}",
                        result?.ErrCode, result?.ErrMsg);
                    return false;
                }

                _logger.LogError("钉钉推送HTTP失败: StatusCode={StatusCode}", (int)response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "钉钉推送异常");
                return false;
            }
        }

        public async Task<bool> PushEmail(Alert alert, CancellationToken cancellationToken = default)
        {
            try
            {
                var smtpHost = await GetConfigValue("SmtpHost");
                var smtpPortStr = await GetConfigValue("SmtpPort");
                var smtpUser = await GetConfigValue("SmtpUser");
                var smtpPassword = await GetConfigValue("SmtpPassword");
                var enableSslStr = await GetConfigValue("SmtpEnableSsl");
                var recipientsStr = await GetConfigValue("EmailRecipients");

                if (string.IsNullOrEmpty(smtpHost) || smtpHost.Contains("example.com") ||
                    string.IsNullOrEmpty(smtpUser) || string.IsNullOrEmpty(smtpPassword))
                {
                    _logger.LogWarning("邮件SMTP未配置，跳过邮件推送");
                    return false;
                }

                if (!int.TryParse(smtpPortStr, out int smtpPort))
                    smtpPort = 587;

                if (!bool.TryParse(enableSslStr, out bool enableSsl))
                    enableSsl = true;

                var recipients = recipientsStr?.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim())
                    .ToList() ?? new List<string>();

                if (!recipients.Any())
                {
                    _logger.LogWarning("邮件收件人为空，跳过邮件推送");
                    return false;
                }

                var textile = await _context.Textiles.FindAsync(alert.TextileId);
                string textileName = textile?.Name ?? $"织绣品#{alert.TextileId}";
                string location = textile?.Location ?? "未知位置";
                string dynasty = textile?.Dynasty ?? "未知";
                string material = textile?.Material ?? "未知";

                var alertLevelText = alert.AlertLevel switch
                {
                    AlertLevel.Warning => "【警告】",
                    AlertLevel.Critical => "【紧急】",
                    _ => "【通知】"
                };

                var subject = $"{alertLevelText}织绣品监测告警 - {alert.Title}";

                var bodyBuilder = new StringBuilder();
                bodyBuilder.AppendLine("<html><body style='font-family:Microsoft YaHei,Arial,sans-serif;padding:20px;'>");
                bodyBuilder.AppendLine("<div style='max-width:800px;margin:0 auto;border:1px solid #e0e0e0;border-radius:8px;overflow:hidden;'>");

                var headerColor = alert.AlertLevel switch
                {
                    AlertLevel.Critical => "#d32f2f",
                    AlertLevel.Warning => "#f57c00",
                    _ => "#1976d2"
                };

                bodyBuilder.AppendLine($"<div style='background:{headerColor};color:white;padding:20px;'>");
                bodyBuilder.AppendLine($"<h2 style='margin:0;'>{alertLevelText}{alert.Title}</h2>");
                bodyBuilder.AppendLine($"<p style='margin:8px 0 0;opacity:0.9;'>{alert.CreatedAt:yyyy-MM-dd HH:mm:ss}</p>");
                bodyBuilder.AppendLine("</div>");

                bodyBuilder.AppendLine("<div style='padding:24px;background:#fafafa;'>");
                bodyBuilder.AppendLine("<h3 style='margin-top:0;color:#333;'>📜 织绣品信息</h3>");
                bodyBuilder.AppendLine("<table style='width:100%;border-collapse:collapse;margin-bottom:20px;'>");
                bodyBuilder.AppendLine("<tr><td style='padding:8px 12px;border-bottom:1px solid #eee;width:120px;color:#666;'>名称</td>");
                bodyBuilder.AppendLine($"<td style='padding:8px 12px;border-bottom:1px solid #eee;font-weight:bold;'>{textileName}</td></tr>");
                bodyBuilder.AppendLine("<tr><td style='padding:8px 12px;border-bottom:1px solid #eee;color:#666;'>朝代</td>");
                bodyBuilder.AppendLine($"<td style='padding:8px 12px;border-bottom:1px solid #eee;'>{dynasty}</td></tr>");
                bodyBuilder.AppendLine("<tr><td style='padding:8px 12px;border-bottom:1px solid #eee;color:#666;'>材质</td>");
                bodyBuilder.AppendLine($"<td style='padding:8px 12px;border-bottom:1px solid #eee;'>{material}</td></tr>");
                bodyBuilder.AppendLine("<tr><td style='padding:8px 12px;color:#666;'>存放位置</td>");
                bodyBuilder.AppendLine($"<td style='padding:8px 12px;'>{location}</td></tr>");
                bodyBuilder.AppendLine("</table>");

                bodyBuilder.AppendLine("<h3 style='color:#333;'>📊 告警数据</h3>");
                bodyBuilder.AppendLine("<table style='width:100%;border-collapse:collapse;margin-bottom:20px;background:white;'>");
                bodyBuilder.AppendLine("<tr style='background:#f5f5f5;'>");
                bodyBuilder.AppendLine("<th style='padding:10px 12px;text-align:left;border-bottom:2px solid #ddd;'>指标</th>");
                bodyBuilder.AppendLine("<th style='padding:10px 12px;text-align:left;border-bottom:2px solid #ddd;'>阈值</th>");
                bodyBuilder.AppendLine("<th style='padding:10px 12px;text-align:left;border-bottom:2px solid #ddd;'>当前值</th>");
                bodyBuilder.AppendLine("<th style='padding:10px 12px;text-align:left;border-bottom:2px solid #ddd;'>状态</th></tr>");

                var exceedColor = alert.AlertLevel == AlertLevel.Critical ? "#d32f2f" : "#f57c00";
                bodyBuilder.AppendLine("<tr>");
                bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>告警指标</td>");
                bodyBuilder.AppendLine($"<td style='padding:10px 12px;border-bottom:1px solid #eee;'>{alert.Threshold:N2}</td>");
                bodyBuilder.AppendLine($"<td style='padding:10px 12px;border-bottom:1px solid #eee;color:{exceedColor};font-weight:bold;'>{alert.ActualValue:N2}</td>");
                bodyBuilder.AppendLine($"<td style='padding:10px 12px;border-bottom:1px solid #eee;color:{exceedColor};'>超出 {((alert.ActualValue - alert.Threshold) / alert.Threshold * 100):N1}%</td>");
                bodyBuilder.AppendLine("</tr>");

                if (alert.HoleDensity.HasValue)
                {
                    bodyBuilder.AppendLine("<tr>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>虫蛀孔洞密度</td>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>5.0 个/100cm²</td>");
                    bodyBuilder.AppendLine($"<td style='padding:10px 12px;border-bottom:1px solid #eee;'>{alert.HoleDensity.Value:N4}</td>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>个/100cm²</td></tr>");
                }

                if (alert.FungiCFU.HasValue)
                {
                    bodyBuilder.AppendLine("<tr>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>霉菌浓度</td>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>300 CFU/g</td>");
                    bodyBuilder.AppendLine($"<td style='padding:10px 12px;border-bottom:1px solid #eee;'>{alert.FungiCFU.Value:N2}</td>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;border-bottom:1px solid #eee;'>CFU/g</td></tr>");
                }

                if (alert.SynergyRisk.HasValue)
                {
                    bodyBuilder.AppendLine("<tr>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;'>协同风险指数</td>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;'>50.0</td>");
                    bodyBuilder.AppendLine($"<td style='padding:10px 12px;color:{exceedColor};font-weight:bold;'>{alert.SynergyRisk.Value:N4}</td>");
                    bodyBuilder.AppendLine("<td style='padding:10px 12px;'>指数</td></tr>");
                }

                bodyBuilder.AppendLine("</table>");

                bodyBuilder.AppendLine("<h3 style='color:#333;'>📝 告警描述</h3>");
                bodyBuilder.AppendLine($"<div style='background:white;padding:16px;border-left:4px solid {headerColor};border-radius:4px;'>");
                bodyBuilder.AppendLine(alert.Message);
                bodyBuilder.AppendLine("</div>");

                bodyBuilder.AppendLine("<div style='margin-top:24px;padding:16px;background:#fff3e0;border-radius:4px;color:#e65100;'>");
                bodyBuilder.AppendLine("<strong>⚠️ 建议措施：</strong>请立即检查该织绣品的存放环境，采取隔离、清洁、熏蒸等必要的文物保护措施。");
                bodyBuilder.AppendLine("</div>");

                bodyBuilder.AppendLine("</div></div>");
                bodyBuilder.AppendLine("<div style='text-align:center;padding:16px;color:#999;font-size:12px;'>");
                bodyBuilder.AppendLine("--- 古代织绣品虫蛀与霉变协同监测系统 ---");
                bodyBuilder.AppendLine("<br>此邮件由系统自动发送，请勿直接回复。");
                bodyBuilder.AppendLine("</div></body></html>");

                using var message = new MimeKit.MimeMessage();
                message.From.Add(new MimeKit.MailboxAddress("织绣品监测系统", smtpUser));

                foreach (var recipient in recipients)
                {
                    message.To.Add(new MimeKit.MailboxAddress(recipient, recipient));
                }

                message.Subject = subject;

                var bodyBuilder = new MimeKit.BodyBuilder
                {
                    HtmlBody = bodyBuilder.ToString()
                };
                message.Body = bodyBuilder.ToMessageBody();

                using var client = new MailKit.Net.Smtp.SmtpClient();
                await client.ConnectAsync(smtpHost, smtpPort, enableSsl, cancellationToken);
                await client.AuthenticateAsync(smtpUser, smtpPassword, cancellationToken);
                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);

                _logger.LogInformation("邮件推送成功: Alert {AlertId} -> {Recipients}", alert.Id, string.Join(",", recipients));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "邮件推送异常");
                return false;
            }
        }

        private async Task<string> GetConfigValue(string key)
        {
            var fromDb = await _context.AlertConfigs
                .FirstOrDefaultAsync(c => c.ConfigKey == key);

            if (fromDb != null && !string.IsNullOrEmpty(fromDb.ConfigValue))
                return fromDb.ConfigValue;

            return _configuration[$"AlertSettings:{key}"] ?? _configuration[key] ?? string.Empty;
        }

        private static string BuildDingTalkSignedUrl(string webhook, string secret)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string stringToSign = $"{timestamp}\n{secret}";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            string sign = Convert.ToBase64String(hashBytes);

            var uriBuilder = new UriBuilder(webhook);
            var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
            query["timestamp"] = timestamp.ToString();
            query["sign"] = sign;
            uriBuilder.Query = query.ToString();

            return uriBuilder.ToString();
        }

        private class DingTalkResponse
        {
            [JsonProperty("errcode")]
            public int ErrCode { get; set; }

            [JsonProperty("errmsg")]
            public string? ErrMsg { get; set; }
        }
    }
}
