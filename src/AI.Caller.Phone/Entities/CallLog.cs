using System;
using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities;

public enum CallStatus {
    [Display(Name = "排队中")] Queued,
    [Display(Name = "呼叫中")] InProgress,
    [Display(Name = "已完成")] Completed,
    [Display(Name = "失败")] Failed,
    [Display(Name = "无应答")] NoAnswer
}

public enum CallInitiationType {
    [Display(Name = "单个呼叫")] Single,
    [Display(Name = "批量呼叫")] Batch
}

public class CallLog {
    public int Id { get; set; }

    [Display(Name = "电话号码")]
    [Required]
    public string PhoneNumber { get; set; }

    [Display(Name = "呼叫状态")]
    public CallStatus Status { get; set; } = CallStatus.Queued;

    [Display(Name = "最终播报内容")]
    public string ResolvedContent { get; set; }

    [Display(Name = "发起类型")]
    public CallInitiationType InitiationType { get; set; }

    [Display(Name = "所属批量任务ID")]
    public int? BatchCallJobId { get; set; } // 外键，关联到批量任务
    public virtual BatchCallJob? BatchCallJob { get; set; }

    [Display(Name = "创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "完成时间")]
    public DateTime? CompletedAt { get; set; }
    
    [Display(Name = "失败原因")]
    public string? FailureReason { get; set; }
}