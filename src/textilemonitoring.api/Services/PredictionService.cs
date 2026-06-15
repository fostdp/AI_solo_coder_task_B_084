
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Models;

namespace TextileMonitoring.API.Services
{
    public interface IPredictionService
    {
        Task<PredictionResultDto> PredictHoleGrowth(int textileId, int horizonDays = 30);
        Task<PredictionResultDto> PredictMoldGrowth(int textileId, int horizonDays = 30);
        Task<PredictionResultDto> PredictSynergyRisk(int textileId, int horizonDays = 30);
        decimal CalculateSynergyRisk(decimal holeDensity, decimal fungiCFU);
        int GetRiskLevel(decimal synergyRisk);
    }

    public class PredictionService : IPredictionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PredictionService> _logger;

        public PredictionService(ApplicationDbContext context, ILogger<PredictionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public decimal CalculateSynergyRisk(decimal holeDensity, decimal fungiCFU)
        {
            if (holeDensity <= 0 || fungiCFU <= 0)
                return 0;

            decimal normalizedHole = NormalizeHoleDensity(holeDensity);
            decimal normalizedFungi = NormalizeFungiCFU(fungiCFU);

            decimal interactionFactor = 1.0m + (normalizedHole * normalizedFungi * 0.5m);

            decimal synergyRisk = (decimal)Math.Sqrt(
                (double)(normalizedHole * normalizedHole * 0.4m +
                         normalizedFungi * normalizedFungi * 0.4m +
                         normalizedHole * normalizedFungi * interactionFactor * 0.2m)
            ) * 100m;

            return Math.Round(synergyRisk, 4);
        }

        public int GetRiskLevel(decimal synergyRisk)
        {
            return synergyRisk switch
            {
                < 25 => (int)RiskLevel.Low,
                < 50 => (int)RiskLevel.Medium,
                < 75 => (int)RiskLevel.High,
                _ => (int)RiskLevel.Critical
            };
        }

        private static decimal NormalizeHoleDensity(decimal holeDensity)
        {
            decimal maxDensity = 15.0m;
            return Math.Min(holeDensity / maxDensity, 1.0m);
        }

        private static decimal NormalizeFungiCFU(decimal fungiCFU)
        {
            decimal maxCFU = 1000.0m;
            return Math.Min(fungiCFU / maxCFU, 1.0m);
        }

        public async Task<PredictionResultDto> PredictHoleGrowth(int textileId, int horizonDays = 30)
        {
            var textile = await _context.Textiles.FindAsync(textileId);
            if (textile == null)
                throw new KeyNotFoundException($"织绣品 {textileId} 不存在");

            var historicalData = await _context.DustSensorData
                .Where(d => d.TextileId == textileId)
                .OrderByDescending(d => d.ReadingTime)
                .Take(180)
                .Select(d => new { d.ReadingTime, d.HoleDensity, d.HoleCount, d.Temperature, d.Humidity })
                .ToListAsync();

            var (r, K, N0) = FitLogisticParameters(historicalData);

            var result = new PredictionResultDto
            {
                TextileId = textileId,
                TextileName = textile.Name,
                Model = (int)PredictionModel.Logistic,
                HorizonDays = horizonDays,
                RiskLevel = (int)RiskLevel.Low
            };

            var lastDate = historicalData.Any()
                ? historicalData.Max(d => d.ReadingTime)
                : DateTime.Now;

            decimal maxPredicted = 0;

            for (int t = 0; t <= horizonDays; t++)
            {
                var date = lastDate.AddDays(t);
                var envFactor = CalculateEnvironmentFactor(historicalData);
                var predicted = LogisticGrowth(r, K, N0, t, envFactor);
                maxPredicted = Math.Max(maxPredicted, predicted);

                result.DataPoints.Add(new PredictionPointDto
                {
                    Date = date,
                    PredictedHoleDensity = Math.Round(predicted, 4)
                });
            }

            var lastActual = historicalData.Any()
                ? historicalData.OrderByDescending(d => d.ReadingTime).First().HoleDensity
                : 0;

            var latestFungi = await _context.FungiSensorData
                .Where(f => f.TextileId == textileId)
                .OrderByDescending(f => f.ReadingTime)
                .FirstOrDefaultAsync();

            var fungiCFU = latestFungi?.FungiCFU ?? 100m;
            var synergyRisk = CalculateSynergyRisk(maxPredicted, fungiCFU);

            result.RiskLevel = CalculateHoleRiskLevel(maxPredicted);
            result.Confidence = CalculateConfidence(historicalData.Count);

            await SavePrediction(textileId, PredictionModel.Logistic, horizonDays,
                maxPredicted, null, synergyRisk, result.Confidence.Value, (RiskLevel)result.RiskLevel);

            return result;
        }

        public async Task<PredictionResultDto> PredictMoldGrowth(int textileId, int horizonDays = 30)
        {
            var textile = await _context.Textiles.FindAsync(textileId);
            if (textile == null)
                throw new KeyNotFoundException($"织绣品 {textileId} 不存在");

            var historicalData = await _context.FungiSensorData
                .Where(f => f.TextileId == textileId)
                .OrderByDescending(f => f.ReadingTime)
                .Take(180)
                .Select(f => new { f.ReadingTime, f.FungiCFU, f.Temperature, f.Humidity })
                .ToListAsync();

            var (A, B, C) = FitGompertzParameters(historicalData);

            var result = new PredictionResultDto
            {
                TextileId = textileId,
                TextileName = textile.Name,
                Model = (int)PredictionModel.Gompertz,
                HorizonDays = horizonDays,
                RiskLevel = (int)RiskLevel.Low
            };

            var lastDate = historicalData.Any()
                ? historicalData.Max(d => d.ReadingTime)
                : DateTime.Now;

            decimal maxPredicted = 0;

            for (int t = 0; t <= horizonDays; t++)
            {
                var date = lastDate.AddDays(t);
                var envFactor = CalculateMoldEnvironmentFactor(historicalData);
                var predicted = GompertzGrowth(A, B, C, t, envFactor);
                maxPredicted = Math.Max(maxPredicted, predicted);

                result.DataPoints.Add(new PredictionPointDto
                {
                    Date = date,
                    PredictedFungiCFU = Math.Round(predicted, 4)
                });
            }

            var latestDust = await _context.DustSensorData
                .Where(d => d.TextileId == textileId)
                .OrderByDescending(d => d.ReadingTime)
                .FirstOrDefaultAsync();

            var holeDensity = latestDust?.HoleDensity ?? 2m;
            var synergyRisk = CalculateSynergyRisk(holeDensity, maxPredicted);

            result.RiskLevel = CalculateFungiRiskLevel(maxPredicted);
            result.Confidence = CalculateConfidence(historicalData.Count);

            await SavePrediction(textileId, PredictionModel.Gompertz, horizonDays,
                null, maxPredicted, synergyRisk, result.Confidence.Value, (RiskLevel)result.RiskLevel);

            return result;
        }

        public async Task<PredictionResultDto> PredictSynergyRisk(int textileId, int horizonDays = 30)
        {
            var holePrediction = await PredictHoleGrowth(textileId, horizonDays);
            var moldPrediction = await PredictMoldGrowth(textileId, horizonDays);

            var textile = await _context.Textiles.FindAsync(textileId);

            var result = new PredictionResultDto
            {
                TextileId = textileId,
                TextileName = textile?.Name ?? string.Empty,
                Model = (int)PredictionModel.Synergy,
                HorizonDays = horizonDays,
                RiskLevel = (int)RiskLevel.Low
            };

            decimal maxSynergyRisk = 0;
            int dataCount = Math.Min(holePrediction.DataPoints.Count, moldPrediction.DataPoints.Count);

            for (int i = 0; i < dataCount; i++)
            {
                var hd = holePrediction.DataPoints[i].PredictedHoleDensity ?? 0;
                var fungi = moldPrediction.DataPoints[i].PredictedFungiCFU ?? 0;
                var synergy = CalculateSynergyRisk(hd, fungi);
                maxSynergyRisk = Math.Max(maxSynergyRisk, synergy);

                result.DataPoints.Add(new PredictionPointDto
                {
                    Date = holePrediction.DataPoints[i].Date,
                    PredictedHoleDensity = Math.Round(hd, 4),
                    PredictedFungiCFU = Math.Round(fungi, 4),
                    PredictedSynergyRisk = Math.Round(synergy, 4)
                });
            }

            result.RiskLevel = GetRiskLevel(maxSynergyRisk);
            result.Confidence = Math.Min(holePrediction.Confidence ?? 0.7m, moldPrediction.Confidence ?? 0.7m);

            var lastPoint = result.DataPoints.LastOrDefault();
            await SavePrediction(textileId, PredictionModel.Synergy, horizonDays,
                lastPoint?.PredictedHoleDensity, lastPoint?.PredictedFungiCFU,
                lastPoint?.PredictedSynergyRisk ?? maxSynergyRisk,
                result.Confidence.Value, (RiskLevel)result.RiskLevel);

            return result;
        }

        private static (decimal r, decimal K, decimal N0) FitLogisticParameters<T>(List<T> historicalData)
        {
            decimal r = 0.05m;
            decimal K = 12.0m;
            decimal N0 = 0.5m;

            if (historicalData == null || historicalData.Count < 2)
                return (r, K, N0);

            var values = new List<decimal>();
            foreach (var item in historicalData)
            {
                var prop = item.GetType().GetProperty("HoleDensity");
                if (prop != null && prop.GetValue(item) != null)
                    values.Add((decimal)prop.GetValue(item)!);
            }

            if (values.Count < 2)
                return (r, K, N0);

            values.Reverse();

            N0 = values.First();
            K = Math.Max(values.Max() * 1.5m, 5.0m);

            if (values.Count >= 3)
            {
                try
                {
                    int midIdx = values.Count / 2;
                    decimal N1 = values.First();
                    decimal N2 = values[midIdx];
                    decimal N3 = values.Last();

                    if (N1 > 0 && N2 > N1 && N3 > N2 && N3 < K * 0.95m)
                    {
                        double t1 = 0;
                        double t2 = midIdx;
                        double t3 = values.Count - 1;

                        double ln1 = Math.Log((double)((K - N1) / N1));
                        double ln2 = Math.Log((double)((K - N2) / N2));
                        double ln3 = Math.Log((double)((K - N3) / N3));

                        double r1 = (ln2 - ln1) / (t2 - t1);
                        double r2 = (ln3 - ln2) / (t3 - t2);
                        r = (decimal)((Math.Abs(r1) + Math.Abs(r2)) / 2.0);
                        r = Math.Clamp(r, 0.005m, 0.2m);
                    }
                }
                catch
                {
                }
            }

            return (r, K, N0);
        }

        private static (decimal A, decimal B, decimal C) FitGompertzParameters<T>(List<T> historicalData)
        {
            decimal A = 800m;
            decimal B = 3.0m;
            decimal C = 0.08m;

            if (historicalData == null || historicalData.Count < 2)
                return (A, B, C);

            var values = new List<decimal>();
            foreach (var item in historicalData)
            {
                var prop = item.GetType().GetProperty("FungiCFU");
                if (prop != null && prop.GetValue(item) != null)
                    values.Add((decimal)prop.GetValue(item)!);
            }

            if (values.Count < 2)
                return (A, B, C);

            values.Reverse();

            A = Math.Max(values.Max() * 1.3m, 300m);
            decimal Y0 = values.First();

            if (Y0 > 0 && Y0 < A)
            {
                B = (decimal)Math.Log(Math.Log((double)(A / Y0)));
                B = Math.Clamp(B, 0.5m, 6.0m);
            }

            if (values.Count >= 3)
            {
                int midIdx = values.Count / 2;
                double tMid = midIdx;
                double yMid = (double)values[midIdx];

                if (yMid > 0 && yMid < (double)A)
                {
                    double lnLnAY = Math.Log(Math.Log((double)A / yMid));
                    C = (decimal)((lnLnAY - (double)B) / -tMid);
                    C = Math.Clamp(Math.Abs(C), 0.005m, 0.2m);
                }
            }

            return (A, B, C);
        }

        private static decimal LogisticGrowth(decimal r, decimal K, decimal N0, int t, decimal envFactor)
        {
            double rDouble = (double)(r * envFactor);
            double KDouble = (double)K;
            double N0Double = (double)N0;
            double tDouble = t;

            double denominator = 1.0 + ((KDouble - N0Double) / N0Double) * Math.Exp(-rDouble * tDouble);
            double result = KDouble / denominator;

            if (double.IsNaN(result) || double.IsInfinity(result))
                return K;

            return (decimal)result;
        }

        private static decimal GompertzGrowth(decimal A, decimal B, decimal C, int t, decimal envFactor)
        {
            double ADouble = (double)A;
            double BDouble = (double)B;
            double CDouble = (double)(C * envFactor);
            double tDouble = t;

            double innerExp = Math.Exp(-CDouble * tDouble);
            double outerExp = Math.Exp(-BDouble * innerExp);
            double result = ADouble * outerExp;

            if (double.IsNaN(result) || double.IsInfinity(result))
                return A * 0.5m;

            return (decimal)result;
        }

        private static decimal CalculateEnvironmentFactor<T>(List<T> data)
        {
            decimal factor = 1.0m;

            if (data == null || !data.Any())
                return factor;

            decimal avgTemp = 0;
            decimal avgHum = 0;
            int count = 0;

            foreach (var item in data)
            {
                var tempProp = item.GetType().GetProperty("Temperature");
                var humProp = item.GetType().GetProperty("Humidity");

                if (tempProp != null && tempProp.GetValue(item) != null)
                {
                    avgTemp += (decimal)tempProp.GetValue(item)!;
                    count++;
                }
                if (humProp != null && humProp.GetValue(item) != null)
                    avgHum += (decimal)humProp.GetValue(item)!;
            }

            if (count > 0)
            {
                avgTemp /= count;
                avgHum /= count;

                decimal tempFactor = 1.0m;
                if (avgTemp > 28)
                    tempFactor = 1.0m + (avgTemp - 28) * 0.05m;
                else if (avgTemp < 18)
                    tempFactor = Math.Max(0.6m, 1.0m - (18 - avgTemp) * 0.03m);

                decimal humFactor = 1.0m;
                if (avgHum > 65)
                    humFactor = 1.0m + (avgHum - 65) * 0.02m;
                else if (avgHum < 45)
                    humFactor = Math.Max(0.7m, 1.0m - (45 - avgHum) * 0.01m);

                factor = tempFactor * humFactor;
            }

            return Math.Clamp(factor, 0.5m, 2.0m);
        }

        private static decimal CalculateMoldEnvironmentFactor<T>(List<T> data)
        {
            decimal factor = 1.0m;

            if (data == null || !data.Any())
                return factor;

            decimal avgTemp = 0;
            decimal avgHum = 0;
            int count = 0;

            foreach (var item in data)
            {
                var tempProp = item.GetType().GetProperty("Temperature");
                var humProp = item.GetType().GetProperty("Humidity");

                if (tempProp != null && tempProp.GetValue(item) != null)
                {
                    avgTemp += (decimal)tempProp.GetValue(item)!;
                    count++;
                }
                if (humProp != null && humProp.GetValue(item) != null)
                    avgHum += (decimal)humProp.GetValue(item)!;
            }

            if (count > 0)
            {
                avgTemp /= count;
                avgHum /= count;

                decimal tempFactor = 1.0m;
                if (avgTemp > 25 && avgTemp < 32)
                    tempFactor = 1.3m;
                else if (avgTemp > 20)
                    tempFactor = 1.1m;
                else if (avgTemp < 15)
                    tempFactor = 0.6m;

                decimal humFactor = 1.0m;
                if (avgHum > 70)
                    humFactor = 1.4m;
                else if (avgHum > 60)
                    humFactor = 1.15m;
                else if (avgHum < 50)
                    humFactor = 0.7m;

                factor = tempFactor * humFactor;
            }

            return Math.Clamp(factor, 0.4m, 2.5m);
        }

        private static int CalculateHoleRiskLevel(decimal holeDensity)
        {
            return holeDensity switch
            {
                < 3 => (int)RiskLevel.Low,
                < 5 => (int)RiskLevel.Medium,
                < 8 => (int)RiskLevel.High,
                _ => (int)RiskLevel.Critical
            };
        }

        private static int CalculateFungiRiskLevel(decimal fungiCFU)
        {
            return fungiCFU switch
            {
                < 200 => (int)RiskLevel.Low,
                < 300 => (int)RiskLevel.Medium,
                < 500 => (int)RiskLevel.High,
                _ => (int)RiskLevel.Critical
            };
        }

        private static decimal CalculateConfidence(int dataPoints)
        {
            if (dataPoints >= 150)
                return 0.92m;
            if (dataPoints >= 100)
                return 0.85m;
            if (dataPoints >= 50)
                return 0.75m;
            if (dataPoints >= 20)
                return 0.65m;
            return 0.50m;
        }

        private async Task SavePrediction(int textileId, PredictionModel model, int horizonDays,
            decimal? holeDensity, decimal? fungiCFU, decimal synergyRisk, decimal confidence, RiskLevel riskLevel)
        {
            try
            {
                var prediction = new Prediction
                {
                    TextileId = textileId,
                    PredictionDate = DateTime.Today,
                    PredictionModel = model,
                    HorizonDays = horizonDays,
                    PredictedHoleDensity = holeDensity,
                    PredictedFungiCFU = fungiCFU,
                    PredictedSynergyRisk = synergyRisk,
                    Confidence = confidence,
                    RiskLevel = riskLevel,
                    CreatedAt = DateTime.Now
                };

                _context.Predictions.Add(prediction);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存预测结果失败");
            }
        }
    }
}
