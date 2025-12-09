using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 监听会话
    /// </summary>
    public class MonitoringSession {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string CallId { get; set; } = string.Empty;

        [Required]
        public int MonitorUserId { get; set; }

        [MaxLength(100)]
        public string? MonitorUserName { get; set; }

        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public DateTime? EndTime { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? InterventionTime { get; set; }

        [MaxLength(200)]
        public string? InterventionReason { get; set; }
    }
}
