using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 场景录音片段表
    /// </summary>
    public class ScenarioRecordingSegment {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ScenarioRecordingId { get; set; }

        [Required]
        public int SegmentOrder { get; set; }

        [Required]
        public SegmentType SegmentType { get; set; }

        // 录音片段字段
        [MaxLength(500)]
        public string? FilePath { get; set; }

        // TTS片段字段
        [MaxLength(2000)]
        public string? TtsText { get; set; }

        /// <summary>
        /// TTS变量（JSON格式）
        /// </summary>
        [MaxLength(1000)]
        public string? TtsVariables { get; set; }

        // DTMF输入片段字段
        public int? DtmfTemplateId { get; set; }

        [MaxLength(50)]
        public string? DtmfVariableName { get; set; }

        // 条件分支字段
        [MaxLength(500)]
        public string? ConditionExpression { get; set; }

        public int? NextSegmentIdOnTrue { get; set; }

        public int? NextSegmentIdOnFalse { get; set; }

        // 通用字段
        public int? Duration { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // 导航属性
        [ForeignKey(nameof(ScenarioRecordingId))]
        public virtual ScenarioRecording? ScenarioRecording { get; set; }

        [ForeignKey(nameof(DtmfTemplateId))]
        public virtual DtmfInputTemplate? DtmfTemplate { get; set; }
    }
}
