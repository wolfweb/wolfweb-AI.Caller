using System;
using System.ComponentModel.DataAnnotations;
using AI.Caller.Core;
using AI.Caller.Phone.Models;
using SIPSorcery.Net;

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

public enum CallDirection {
    [Display(Name = "呼入")] Inbound,
    [Display(Name = "呼出")] Outbound
}

public class CallLog {
    public int Id { get; set; }

    [Display(Name = "电话号码")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "呼叫状态")]
    public CallStatus Status { get; set; } = CallStatus.Queued;

    [Display(Name = "最终播报内容")]
    public string? ResolvedContent { get; set; }

    [Display(Name = "发起类型")]
    public CallInitiationType InitiationType { get; set; }

    [Display(Name = "所属批量任务ID")]
    public int? BatchCallJobId { get; set; }
    public virtual BatchCallJob? BatchCallJob { get; set; }

    [Display(Name = "创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "完成时间")]
    public DateTime? CompletedAt { get; set; }
    
    [Display(Name = "失败原因")]
    public string? FailureReason { get; set; }

    [Display(Name = "场景录音ID")]
    public int? ScenarioRecordingId { get; set; }

    // ========== 新增字段：全场景通话记录支持 ==========
    
    [Display(Name = "呼叫ID")]
    public string? CallId { get; set; }

    [Display(Name = "通话场景")]
    public CallScenario? CallScenario { get; set; }

    [Display(Name = "通话方向")]
    public CallDirection? Direction { get; set; }

    // 主叫信息
    [Display(Name = "主叫用户ID")]
    public int? CallerUserId { get; set; }

    [Display(Name = "主叫号码")]
    public string? CallerNumber { get; set; }

    // 被叫信息
    [Display(Name = "被叫用户ID")]
    public int? CalleeUserId { get; set; }

    [Display(Name = "被叫号码")]
    public string? CalleeNumber { get; set; }

    // 通话时间
    [Display(Name = "通话开始时间")]
    public DateTime? StartTime { get; set; }

    [Display(Name = "通话结束时间")]
    public DateTime? EndTime { get; set; }

    [Display(Name = "通话时长")]
    public TimeSpan? Duration { get; set; }

    // 通话结果
    [Display(Name = "通话结束状态")]
    public CallFinishStatus? FinishStatus { get; set; }

    [Display(Name = "录音文件路径")]
    public string? RecordingFilePath { get; set; }

    // 导航属性
    public virtual User? CallerUser { get; set; }
    public virtual User? CalleeUser { get; set; }
    public virtual ScenarioRecording? ScenarioRecording { get; set; }
    public virtual ICollection<DtmfInputRecord> DtmfInputs { get; set; } = [];
    public virtual ICollection<MonitoringSession> MonitoringSessions { get; set; } = [];
    public virtual ICollection<PlaybackControl> PlaybackControls { get; set; } = [];
}