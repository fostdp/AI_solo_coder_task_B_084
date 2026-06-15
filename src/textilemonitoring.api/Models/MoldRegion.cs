
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public class MoldRegion
    {
        [Key]
        public int Id { get; set; }

        public int TextileId { get; set; }

        public long? FungiDataId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal CenterX { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal CenterY { get; set; }

        [Column(TypeName = "decimal(8,4)")]
        public decimal RadiusMm { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal AreaCm2 { get; set; }

        public DateTime DetectedTime { get; set; }

        public SeverityLevel Severity { get; set; } = SeverityLevel.Mild;

        [MaxLength(100)]
        public string? FungiType { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("TextileId")]
        public virtual Textile? Textile { get; set; }
    }
}
