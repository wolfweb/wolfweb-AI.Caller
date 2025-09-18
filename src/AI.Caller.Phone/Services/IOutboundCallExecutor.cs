using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface IOutboundCallExecutor {
        /// <summary>
        /// 执行外呼
        /// </summary>
        Task<OutboundCallResult> ExecuteCallAsync(TtsCallRecord record, OutboundCallScript script, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 检查外呼能力
        /// </summary>
        bool CanExecuteOutboundCall();
    }
    
    /// <summary>
    /// 外呼结果
    /// </summary>
    public class OutboundCallResult {
        public bool Success { get; set; }
        public TtsCallStatus Status { get; set; }
        public string? FailureReason { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime CallTime { get; set; } = DateTime.UtcNow;
        public string? CallId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}