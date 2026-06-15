
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.Data;
using TextileMonitoring.Data.Entities;

namespace TextileMonitoring.Api.Controllers
{
    [ApiController]
    [Route("api/classification")]
    public class PestVocClassificationController : ControllerBase
    {
        private readonly TextileMonitoringDbContext _context;

        public PestVocClassificationController(TextileMonitoringDbContext context)
        {
            _context = context;
        }

        [HttpGet("textiles/{textileId:int}/pest")]
        public async Task<ActionResult<IEnumerable<PestClassificationRecord>>> GetPestClassifications(
            int textileId,
            int limit = 50)
        {
            var textile = await _context.Textiles.FindAsync(textileId);
            if (textile == null)
                return NotFound();

            var records = await _context.PestClassificationRecords
                .AsNoTracking()
                .Where(r => r.TextileId == textileId)
                .OrderByDescending(r => r.ClassifiedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(records);
        }

        [HttpGet("textiles/{textileId:int}/voc")]
        public async Task<ActionResult<IEnumerable<VocClassificationRecord>>> GetVocClassifications(
            int textileId,
            int limit = 50)
        {
            var textile = await _context.Textiles.FindAsync(textileId);
            if (textile == null)
                return NotFound();

            var records = await _context.VocClassificationRecords
                .AsNoTracking()
                .Where(r => r.TextileId == textileId)
                .OrderByDescending(r => r.ClassifiedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(records);
        }

        [HttpGet("pest/species-stats")]
        public async Task<ActionResult<IEnumerable<object>>> GetPestSpeciesStats()
        {
            var stats = await _context.PestClassificationRecords
                .AsNoTracking()
                .GroupBy(r => r.PredictedSpeciesName)
                .Select(g => new
                {
                    SpeciesName = g.Key,
                    Count = g.Count(),
                    AvgConfidence = Math.Round(g.Average(r => (double)r.Confidence), 4),
                    LastDetected = g.Max(r => r.ClassifiedAt)
                })
                .OrderByDescending(s => s.Count)
                .ToListAsync();

            return Ok(stats);
        }

        [HttpGet("voc/mold-stats")]
        public async Task<ActionResult<IEnumerable<object>>> GetVocMoldStats()
        {
            var stats = await _context.VocClassificationRecords
                .AsNoTracking()
                .GroupBy(r => r.PredictedMoldSpeciesName)
                .Select(g => new
                {
                    MoldSpeciesName = g.Key,
                    Count = g.Count(),
                    AvgConfidence = Math.Round(g.Average(r => (double)r.Confidence), 4),
                    LastDetected = g.Max(r => r.ClassifiedAt)
                })
                .OrderByDescending(s => s.Count)
                .ToListAsync();

            return Ok(stats);
        }

        [HttpGet("voc/latest/{textileId:int}")]
        public async Task<ActionResult<IEnumerable<VocSensorData>>> GetLatestVocTimeSeries(int textileId)
        {
            var textile = await _context.Textiles.FindAsync(textileId);
            if (textile == null)
                return NotFound();

            var startTime = DateTime.UtcNow.AddHours(-12);

            var data = await _context.VocSensorData
                .AsNoTracking()
                .Where(d => d.TextileId == textileId && d.ReadingTime >= startTime)
                .OrderBy(d => d.ReadingTime)
                .ToListAsync();

            return Ok(data);
        }
    }
}
