
using System.ComponentModel.DataAnnotations;

namespace TextileMonitoring.API.Models
{
    public class AlertConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ConfigKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string ConfigValue { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
