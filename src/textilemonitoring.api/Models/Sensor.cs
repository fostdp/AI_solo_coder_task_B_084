
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public enum SensorType
    {
        DustSensor = 1,
        FungiSensor = 2
    }

    public class Sensor
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string SensorCode { get; set; } = string.Empty;

        public SensorType SensorType { get; set; }

        public int TextileId { get; set; }

        [Column(TypeName = "decimal(8,4)")]
        public decimal? PositionX { get; set; }

        [Column(TypeName = "decimal(8,4)")]
        public decimal? PositionY { get; set; }

        public DateTime InstallationDate { get; set; } = DateTime.Now;

        public DateTime? LastCalibrationDate { get; set; }

        public bool IsActive { get; set; } = true;

        [Required]
        [MaxLength(20)]
        public string ZigBeeAddress { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey("TextileId")]
        public virtual Textile? Textile { get; set; }
    }
}
