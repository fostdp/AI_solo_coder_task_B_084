
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public enum AlertType
    {
        HoleDensity = 1,
        FungiCFU = 2,
        SynergyRisk = 3
    }

    public enum AlertLevel
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    public class Alert
    {
        [Key]
        public long Id { get; set; }

        public int TextileId { get; set; }

        public AlertType AlertType { get; set; }

        public AlertLevel AlertLevel { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12,4)")]
        public decimal? HoleDensity { get; set; }

        [Column(TypeName = "decimal(15,4)")]
        public decimal? FungiCFU { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal? SynergyRisk { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal Threshold { get; set; }

        [Column(TypeName = "decimal(15,4)")]
        public decimal ActualValue { get; set; }

        public bool DingTalkPushed { get; set; } = false;

        public bool EmailPushed { get; set; } = false;

        public DateTime? PushedAt { get; set; }

        public bool Acknowledged { get; set; } = false;

        [MaxLength(100)]
        public string? AcknowledgedBy { get; set; }

        public DateTime? AcknowledgedAt { get; set; }

        public bool Resolved { get; set; } = false;

        public DateTime? ResolvedAt { get; set; }

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("TextileId")]
        public virtual Textile? Textile { get; set; }
    }
}
