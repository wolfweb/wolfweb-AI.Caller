namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音状态服务接口，提供录音系统状态的实时查询
    /// </summary>
    public interface IRecordingStatusService
    {
        /// <summary>
        /// 获取录音健康状态
        /// </summary>
        /// <returns>录音健康状态</returns>
        RecordingHealthStatus GetHealthStatus();
        
        /// <summary>
        /// 获取数据流监控状态
        /// </summary>
        /// <returns>数据流监控状态</returns>
        DataFlowMonitorStatus GetDataFlowStatus();
        
        /// <summary>
        /// 获取音频桥接器统计信息
        /// </summary>
        /// <returns>音频桥接器统计信息</returns>
        AudioBridgeStats GetAudioBridgeStats();
        
        /// <summary>
        /// 获取综合系统状态
        /// </summary>
        /// <returns>系统状态</returns>
        SystemHealthStatus GetSystemStatus();
        
        /// <summary>
        /// 重置所有统计信息
        /// </summary>
        void ResetAllStats();
        
        /// <summary>
        /// 系统状态变化事件
        /// </summary>
        event EventHandler<SystemStatusChangedEventArgs> SystemStatusChanged;
        
        /// <summary>
        /// 健康状态警告事件
        /// </summary>
        event EventHandler<HealthWarningEventArgs> HealthWarning;
        
        /// <summary>
        /// 系统恢复事件
        /// </summary>
        event EventHandler<SystemRecoveredEventArgs> SystemRecovered;
    }
    
    /// <summary>
    /// 系统健康状态
    /// </summary>
    public class SystemHealthStatus
    {
        /// <summary>
        /// 整体是否健康
        /// </summary>
        public bool IsHealthy { get; set; }
        
        /// <summary>
        /// 系统质量评级
        /// </summary>
        public RecordingQuality OverallQuality { get; set; }
        
        /// <summary>
        /// 录音核心状态
        /// </summary>
        public RecordingHealthStatus? RecordingStatus { get; set; }
        
        /// <summary>
        /// 数据流状态
        /// </summary>
        public DataFlowMonitorStatus? DataFlowStatus { get; set; }
        
        /// <summary>
        /// 音频桥接器状态
        /// </summary>
        public AudioBridgeStats? AudioBridgeStatus { get; set; }
        
        /// <summary>
        /// 系统问题列表
        /// </summary>
        public List<SystemIssue> Issues { get; set; } = new();
        
        /// <summary>
        /// 状态检查时间
        /// </summary>
        public DateTime CheckTime { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 系统运行时长
        /// </summary>
        public TimeSpan Uptime { get; set; }
        
        public override string ToString()
        {
            return $"System Health: {(IsHealthy ? "Healthy" : "Unhealthy")}, " +
                   $"Quality: {OverallQuality}, Issues: {Issues.Count}, " +
                   $"Uptime: {Uptime:hh\\:mm\\:ss}";
        }
    }
    
    /// <summary>
    /// 系统问题
    /// </summary>
    public class SystemIssue
    {
        /// <summary>
        /// 问题类型
        /// </summary>
        public SystemIssueType Type { get; set; }
        
        /// <summary>
        /// 问题描述
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// 严重程度
        /// </summary>
        public IssueSeverity Severity { get; set; }
        
        /// <summary>
        /// 发生时间
        /// </summary>
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 相关组件
        /// </summary>
        public string Component { get; set; } = string.Empty;
        
        public override string ToString()
        {
            return $"[{Severity}] {Type}: {Description} ({Component})";
        }
    }
    
    /// <summary>
    /// 系统问题类型
    /// </summary>
    public enum SystemIssueType
    {
        DataFlowInterruption,
        RecordingFailure,
        AudioBridgeError,
        FileSystemError,
        PerformanceIssue,
        ConfigurationError,
        Unknown
    }
    
    /// <summary>
    /// 问题严重程度
    /// </summary>
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
    
    /// <summary>
    /// 系统状态变化事件参数
    /// </summary>
    public class SystemStatusChangedEventArgs : EventArgs
    {
        public SystemHealthStatus PreviousStatus { get; set; } = new();
        public SystemHealthStatus CurrentStatus { get; set; } = new();
        public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
        
        public bool HealthImproved => CurrentStatus.IsHealthy && !PreviousStatus.IsHealthy;
        public bool HealthDegraded => !CurrentStatus.IsHealthy && PreviousStatus.IsHealthy;
    }
    
    /// <summary>
    /// 健康状态警告事件参数
    /// </summary>
    public class HealthWarningEventArgs : EventArgs
    {
        public SystemIssue Issue { get; set; } = new();
        public SystemHealthStatus CurrentStatus { get; set; } = new();
        public DateTime WarningTime { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// 系统恢复事件参数
    /// </summary>
    public class SystemRecoveredEventArgs : EventArgs
    {
        public List<SystemIssue> ResolvedIssues { get; set; } = new();
        public TimeSpan DowntimeDuration { get; set; }
        public DateTime RecoveryTime { get; set; } = DateTime.UtcNow;
    }
}