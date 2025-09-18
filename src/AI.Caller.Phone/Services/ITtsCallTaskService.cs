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
}