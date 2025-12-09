using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 播放控制
    /// </summary>
    public class PlaybackControl {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string CallId { get; set; } = string.Empty;

        public int? CurrentSegmentId { get; set; }

        [Required]
        public PlaybackState PlaybackState { get; set; } = PlaybackState.NotStarted;

        public int? LastInterventionSegmentId { get; set; }

        /// <summary>
        /// 跳过的片段ID列表（JSON格式）
        /// </summary>
        [MaxLength(500)]
        public string? SkippedSegments { get; set; }

        public DateTime? PausedAt { get; set; }

        public DateTime? ResumedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
