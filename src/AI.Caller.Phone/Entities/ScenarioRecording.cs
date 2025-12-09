using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 场景录音主表
    /// </summary>
    public class ScenarioRecording {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(50)]
        public string? Category { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int CreatedBy { get; set; }

        // 导航属性
        public virtual ICollection<ScenarioRecordingSegment> Segments { get; set; } = new List<ScenarioRecordingSegment>();
    }
}
