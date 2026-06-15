
using Microsoft.AspNetCore.Mvc;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Services;

namespace TextileMonitoring.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SensorsController : ControllerBase
    {
        private readonly ISensorDataService _sensorDataService;

        public SensorsController(ISensorDataService sensorDataService)
        {
            _sensorDataService = sensorDataService;
        }

        [HttpPost("zigbee/dust")]
        public async Task<ActionResult> ReceiveDustData([FromBody] ZigBeeDustPayloadDto payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.SensorCode))
                return BadRequest("无效的传感器数据");

            var result = await _sensorDataService.ReceiveDustData(payload);
            if (result)
                return Ok(new { success = true, message = "粉尘传感器数据已接收" });

            return BadRequest(new { success = false, message = "数据接收失败" });
        }

        [HttpPost("zigbee/fungi")]
        public async Task<ActionResult> ReceiveFungiData([FromBody] ZigBeeFungiPayloadDto payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.SensorCode))
                return BadRequest("无效的传感器数据");

            var result = await _sensorDataService.ReceiveFungiData(payload);
            if (result)
                return Ok(new { success = true, message = "真菌传感器数据已接收" });

            return BadRequest(new { success = false, message = "数据接收失败" });
        }

        [HttpPost("zigbee/batch")]
        public async Task<ActionResult> ReceiveBatchData([FromBody] BatchZigBeePayload payload)
        {
            if (payload == null)
                return BadRequest();

            int successCount = 0;
            int failCount = 0;

            if (payload.DustData != null)
            {
                foreach (var dust in payload.DustData)
                {
                    if (await _sensorDataService.ReceiveDustData(dust))
                        successCount++;
                    else
                        failCount++;
                }
            }

            if (payload.FungiData != null)
            {
                foreach (var fungi in payload.FungiData)
                {
                    if (await _sensorDataService.ReceiveFungiData(fungi))
                        successCount++;
                    else
                        failCount++;
                }
            }

            return Ok(new
            {
                success = true,
                total = successCount + failCount,
                successCount,
                failCount,
                message = $"批量接收完成：成功{successCount}条，失败{failCount}条"
            });
        }

        [HttpGet("dust/history/{textileId}")]
        public async Task<ActionResult<IEnumerable<DustSensorDataDto>>> GetDustHistory(
            int textileId,
            DateTime? start = null,
            DateTime? end = null,
            int limit = 200)
        {
            var data = await _sensorDataService.GetDustHistory(textileId, start, end, limit);
            return Ok(data);
        }

        [HttpGet("fungi/history/{textileId}")]
        public async Task<ActionResult<IEnumerable<FungiSensorDataDto>>> GetFungiHistory(
            int textileId,
            DateTime? start = null,
            DateTime? end = null,
            int limit = 200)
        {
            var data = await _sensorDataService.GetFungiHistory(textileId, start, end, limit);
            return Ok(data);
        }
    }

    public class BatchZigBeePayload
    {
        public List<ZigBeeDustPayloadDto>? DustData { get; set; }
        public List<ZigBeeFungiPayloadDto>? FungiData { get; set; }
    }
}
