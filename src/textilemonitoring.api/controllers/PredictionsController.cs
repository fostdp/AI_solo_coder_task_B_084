
using Microsoft.AspNetCore.Mvc;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Services;

namespace TextileMonitoring.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PredictionsController : ControllerBase
    {
        private readonly IPredictionService _predictionService;

        public PredictionsController(IPredictionService predictionService)
        {
            _predictionService = predictionService;
        }

        [HttpGet("hole/{textileId}")]
        public async Task<ActionResult<PredictionResultDto>> PredictHoleGrowth(int textileId, [FromQuery] int horizonDays = 30)
        {
            try
            {
                var result = await _predictionService.PredictHoleGrowth(textileId, horizonDays);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet("mold/{textileId}")]
        public async Task<ActionResult<PredictionResultDto>> PredictMoldGrowth(int textileId, [FromQuery] int horizonDays = 30)
        {
            try
            {
                var result = await _predictionService.PredictMoldGrowth(textileId, horizonDays);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet("synergy/{textileId}")]
        public async Task<ActionResult<PredictionResultDto>> PredictSynergyRisk(int textileId, [FromQuery] int horizonDays = 30)
        {
            try
            {
                var result = await _predictionService.PredictSynergyRisk(textileId, horizonDays);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet("full/{textileId}")]
        public async Task<ActionResult<object>> GetFullPrediction(int textileId, [FromQuery] int horizonDays = 30)
        {
            try
            {
                var holeTask = _predictionService.PredictHoleGrowth(textileId, horizonDays);
                var moldTask = _predictionService.PredictMoldGrowth(textileId, horizonDays);
                var synergyTask = _predictionService.PredictSynergyRisk(textileId, horizonDays);

                await Task.WhenAll(holeTask, moldTask, synergyTask);

                return Ok(new
                {
                    TextileId = textileId,
                    HorizonDays = horizonDays,
                    HolePrediction = holeTask.Result,
                    MoldPrediction = moldTask.Result,
                    SynergyPrediction = synergyTask.Result
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("calculate/synergy")]
        public ActionResult CalculateSynergy([FromBody] SynergyCalcRequest request)
        {
            var risk = _predictionService.CalculateSynergyRisk(request.HoleDensity, request.FungiCFU);
            var level = _predictionService.GetRiskLevel(risk);

            return Ok(new
            {
                HoleDensity = request.HoleDensity,
                FungiCFU = request.FungiCFU,
                SynergyRisk = risk,
                RiskLevel = level,
                RiskLevelText = level switch
                {
                    0 => "低风险",
                    1 => "中风险",
                    2 => "高风险",
                    3 => "严重风险",
                    _ => "未知"
                }
            });
        }
    }

    public class SynergyCalcRequest
    {
        public decimal HoleDensity { get; set; }
        public decimal FungiCFU { get; set; }
    }
}
