
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public enum TextileStatus
    {
        Normal = 0,
        Warning = 1,
        Alert = 2,
        Critical = 3
    }

    public class Textile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Dynasty { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Material { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal WidthCm { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal HeightCm { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AreaCm2 => WidthCm * HeightCm;

        [Required]
        [MaxLength(200)]
        public string Location { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        public DateTime? AcquisitionDate { get; set; }

        public TextileStatus Status { get; set; } = TextileStatus.Normal;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<Sensor>? Sensors { get; set; }
        public virtual ICollection<HoleMarker>? HoleMarkers { get; set; }
        public virtual ICollection<MoldRegion>? MoldRegions { get; set; }
        public virtual ICollection<Alert>? Alerts { get; set; }
        public virtual ICollection<Prediction>? Predictions { get; set; }
    }
}
