using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface ITtsCallTaskService {
        /// <summary>
        /// 执行TTS外呼任务
        /// </summary>
        Task ExecuteCallTaskAsync(int documentId);
        
        /// <summary>
        /// 暂停任务
        /// </summary>
        Task PauseTaskAsync(int documentId);
        
        /// <summary>
        /// 恢复任务
        /// </summary>
        Task ResumeTaskAsync(int documentId);
        
        /// <summary>
        /// 停止任务
        /// </summary>
        Task StopTaskAsync(int documentId);
        
        /// <summary>
        /// 获取任务状态
        /// </summary>
        Task<bool> IsTaskRunningAsync(int documentId);
    }

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