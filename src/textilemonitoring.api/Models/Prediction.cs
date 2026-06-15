
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public enum PredictionModel
    {
        Logistic = 1,
        Gompertz = 2,
        Synergy = 3
    }

    public enum RiskLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public class Prediction
    {
        [Key]
        public long Id { get; set; }

        public int TextileId { get; set; }

        public DateTime PredictionDate { get; set; }

        public PredictionModel PredictionModel { get; set; }

        public int HorizonDays { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal? PredictedHoleDensity { get; set; }

        [Column(TypeName = "decimal(15,4)")]
        public decimal? PredictedFungiCFU { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal? PredictedSynergyRisk { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? Confidence { get; set; }

        public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("TextileId")]
        public virtual Textile? Textile { get; set; }
    }
}
