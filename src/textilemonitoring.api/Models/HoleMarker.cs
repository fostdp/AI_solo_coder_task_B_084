
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public enum SeverityLevel
    {
        Mild = 0,
        Moderate = 1,
        Severe = 2
    }

    public class HoleMarker
    {
        [Key]
        public int Id { get; set; }

        public int TextileId { get; set; }

        public long? DustDataId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal PositionX { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal PositionY { get; set; }

        [Column(TypeName = "decimal(8,4)")]
        public decimal RadiusMm { get; set; }

        public DateTime DetectedTime { get; set; }

        public SeverityLevel Severity { get; set; } = SeverityLevel.Mild;

        [MaxLength(500)]
        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("TextileId")]
        public virtual Textile? Textile { get; set; }
    }
}
