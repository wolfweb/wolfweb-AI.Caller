using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities;

public enum BatchJobStatus {
    [Display(Name = "排队中")]
    Queued,

    [Display(Name = "预处理中")] // 正在读取文件、创建子任务
    Preprocessing,

    [Display(Name = "执行中")]
    Processing,

    [Display(Name = "已完成")]
    Completed,

    [Display(Name = "部分失败")] // 任务完成，但有部分呼叫失败
    CompletedWithFailures,

    [Display(Name = "已取消")]
    Cancelled,

    [Display(Name = "失败")] // 整个任务因严重错误（如文件格式错误）而失败
    Failed
}

public class BatchCallJob {
    [Key]
    public int Id { get; set; }

    [Display(Name = "任务名称")]
    [Required]
    public string JobName { get; set; }

    [Display(Name = "任务状态")]
    public BatchJobStatus Status { get; set; } = BatchJobStatus.Queued;

    [Display(Name = "使用的TTS模板ID")]
    [Required]
    public int TtsTemplateId { get; set; }

    [Display(Name = "原始文件名")]
    [Required]
    public string OriginalFileName { get; set; }

    [Display(Name = "存储文件路径")]
    [Required]
    public string StoredFilePath { get; set; }

    [Display(Name = "总计数量")]
    public int TotalCount { get; set; } = 0;

    [Display(Name = "已处理数量")]
    public int ProcessedCount { get; set; } = 0;

    [Display(Name = "成功数量")]
    public int SuccessCount { get; set; } = 0;

    [Display(Name = "失败数量")]
    public int FailedCount { get; set; } = 0;

    [Display(Name = "创建者ID")]
    public int? CreatedByUserId { get; set; }
    public virtual User? CreatedByUser { get; set; }

    [Display(Name = "创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "开始执行时间")]
    public DateTime? StartedAt { get; set; }

    [Display(Name = "完成时间")]
    public DateTime? CompletedAt { get; set; }
    
    [Display(Name = "任务失败原因")]
    public string? FailureReason { get; set; }

    public virtual ICollection<CallLog> CallLogs { get; set; } = new List<CallLog>();
}