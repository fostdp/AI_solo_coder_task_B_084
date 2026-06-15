using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Data;
using TextileMonitoring.Data.Entities;
using TreatmentSimulator.Service.Services;

namespace TreatmentSimulator.Service.Consumers;

public class TreatmentRequestConsumer : IConsumer<NitrogenTreatmentRequest>
{
    private readonly INitrogenProbitSimulator _simulator;
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger _logger;

    public TreatmentRequestConsumer(
        INitrogenProbitSimulator simulator,
        TextileMonitoringDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ILogger logger)
    {
        _simulator = simulator;
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NitrogenTreatmentRequest> context)
    {
        var request = context.Message;

        _logger.Information("Received NitrogenTreatmentRequest: {CorrelationId}, TextileId: {TextileId}, Target: {TargetOrganisms}",
            request.CorrelationId, request.TextileId, request.TargetOrganisms);

        try
        {
            var result = _simulator.SimulateTreatment(request);

            _logger.Information("Treatment simulation completed: {CorrelationId}, Success: {IsSuccess}, Egg: {EggMort:F2}%, Larvae: {LarvaeMort:F2}%, Adult: {AdultMort:F2}%, Fungi: {FungiMort:F2}%",
                result.CorrelationId,
                result.IsSuccessCriteriaMet,
                result.PredictedEggMortalityRate,
                result.PredictedLarvaeMortalityRate,
                result.PredictedAdultMortalityRate,
                result.PredictedFungiSterilityRate);

            var session = new NitrogenTreatmentSession
            {
                CorrelationId = result.CorrelationId,
                RequestCorrelationId = request.CorrelationId,
                TextileId = request.TextileId,
                RequestedBy = request.RequestedBy ?? string.Empty,
                TargetOrganismsId = (int)request.TargetOrganisms,
                TargetOxygenConcentrationPct = (decimal)request.TargetOxygenConcentrationPct,
                NitrogenFlowRateLpm = (decimal)request.NitrogenFlowRateLpm,
                ExposureDurationMinutes = request.ExposureDurationMinutes,
                ChamberPressureKpa = (decimal)request.ChamberPressureKpa,
                ChamberTemperatureC = (decimal)request.ChamberTemperatureC,
                ChamberHumidityPct = (decimal)request.ChamberHumidityPct,
                CurrentPestDensity = (decimal)request.CurrentPestDensity,
                CurrentFungiCFU = (decimal)request.CurrentFungiCFU,
                PrimaryPestTargetId = request.PrimaryPestTarget.HasValue ? (int)request.PrimaryPestTarget.Value : null,
                PredictedEggMortalityRate = (decimal)result.PredictedEggMortalityRate,
                PredictedLarvaeMortalityRate = (decimal)result.PredictedLarvaeMortalityRate,
                PredictedAdultMortalityRate = (decimal)result.PredictedAdultMortalityRate,
                PredictedFungiSterilityRate = (decimal)result.PredictedFungiSterilityRate,
                CILowPct = (decimal)result.ConfidenceIntervalLowPct,
                CIHighPct = (decimal)result.ConfidenceIntervalHighPct,
                ProbitTransformValue = result.ProbitTransformValue,
                LD99Minutes = (decimal)result.CalculatedLethalDoseLD99Min,
                MinimumRequiredExposureMin = (decimal)result.MinimumRequiredExposureMin,
                RecommendedSafetyExposureMin = (decimal)result.RecommendedSafetyExposureMin,
                FiberStrengthDegradationPct = (decimal)result.FiberStrengthDegradationEstimatedPct,
                ColorChangeDeltaE = (decimal)result.ColorChangeDeltaE,
                SessionStatus = result.IsSuccessCriteriaMet ? 1 : 0,
                IsSuccessCriteriaMet = result.IsSuccessCriteriaMet,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                Notes = result.TreatmentRiskNotes
            };

            _dbContext.NitrogenTreatmentSessions.Add(session);
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.Information("Treatment session saved to database: SessionId={SessionId}", session.Id);

            await _publishEndpoint.Publish(result, context.CancellationToken);

            _logger.Information("Treatment result published: {ResultCorrelationId}", result.CorrelationId);
        }
        catch (DbUpdateException dbEx)
        {
            _logger.Error(dbEx, "Database error while processing treatment request: {CorrelationId}", request.CorrelationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process treatment request: {CorrelationId}", request.CorrelationId);
            throw;
        }
    }
}

public class TreatmentRequestConsumerDefinition : ConsumerDefinition<TreatmentRequestConsumer>
{
    private const string ServiceName = "treatment-simulator";
    private const string EventName = "treatment-request";

    public TreatmentRequestConsumerDefinition()
    {
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TreatmentRequestConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Immediate(3));
        endpointConfigurator.UseDeadLetterQueue($"textile-monitoring.{ServiceName}.{EventName}.dlq");

        endpointConfigurator.ConfigureConsumer<TreatmentRequestConsumer>(context);
    }
}
