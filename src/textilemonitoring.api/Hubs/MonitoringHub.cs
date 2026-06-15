
using Microsoft.AspNetCore.SignalR;
using TextileMonitoring.Contracts.Messages;

namespace TextileMonitoring.Api.Hubs
{
    public class MonitoringHub : Hub
    {
        public async Task JoinTextileGroup(int textileId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"textile_{textileId}");
        }

        public async Task LeaveTextileGroup(int textileId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"textile_{textileId}");
        }
    }

    public interface IMonitoringHubService
    {
        Task SendPestClassificationUpdate(PestClassificationResult result);
        Task SendVocUpdate(VocClassificationResult result);
        Task SendTreatmentUpdate(NitrogenTreatmentResult result);
        Task SendVulnerabilityUpdate(VulnerabilityIndexGenerated result);
    }

    public class MonitoringHubService : IMonitoringHubService
    {
        private readonly IHubContext<MonitoringHub> _hubContext;

        public MonitoringHubService(IHubContext<MonitoringHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SendPestClassificationUpdate(PestClassificationResult result)
        {
            await _hubContext.Clients.All.SendAsync("PestClassificationUpdated", result);
            await _hubContext.Clients.Group($"textile_{result.TextileId}").SendAsync("PestClassificationUpdated", result);
        }

        public async Task SendVocUpdate(VocClassificationResult result)
        {
            await _hubContext.Clients.All.SendAsync("VocClassificationUpdated", result);
            await _hubContext.Clients.Group($"textile_{result.TextileId}").SendAsync("VocClassificationUpdated", result);
        }

        public async Task SendTreatmentUpdate(NitrogenTreatmentResult result)
        {
            await _hubContext.Clients.All.SendAsync("TreatmentUpdated", result);
            await _hubContext.Clients.Group($"textile_{result.TextileId}").SendAsync("TreatmentUpdated", result);
        }

        public async Task SendVulnerabilityUpdate(VulnerabilityIndexGenerated result)
        {
            await _hubContext.Clients.All.SendAsync("VulnerabilityUpdated", result);
            await _hubContext.Clients.Group($"textile_{result.TextileId}").SendAsync("VulnerabilityUpdated", result);
        }
    }
}
