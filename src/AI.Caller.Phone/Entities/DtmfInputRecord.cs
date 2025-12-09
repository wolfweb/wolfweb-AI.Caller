using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// DTMF输入记录
    /// </summary>
    public class DtmfInputRecord {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string CallId { get; set; } = string.Empty;

        public int? SegmentId { get; set; }

        public int? TemplateId { get; set; }

        [Required]
        [MaxLength(100)]
        public string InputValue { get; set; } = string.Empty;

        public bool IsValid { get; set; }

        [MaxLength(500)]
        public string? ValidationMessage { get; set; }

        public DateTime InputTime { get; set; } = DateTime.UtcNow;

        public int RetryCount { get; set; } = 0;

        public int Duration { get; set; }

        // 导航属性
        [ForeignKey(nameof(SegmentId))]
        public virtual ScenarioRecordingSegment? Segment { get; set; }

        [ForeignKey(nameof(TemplateId))]
        public virtual DtmfInputTemplate? Template { get; set; }
    }
}
