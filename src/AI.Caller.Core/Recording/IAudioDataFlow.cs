namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 音频数据流管理接口，确保音频数据正确流向文件写入器
    /// </summary>
    public interface IAudioDataFlow
    {
        /// <summary>
        /// 初始化数据流
        /// </summary>
        /// <param name="inputFormat">输入音频格式</param>
        /// <param name="outputPath">输出文件路径</param>
        /// <returns>是否成功初始化</returns>
        Task<bool> InitializeAsync(AudioFormat inputFormat, string outputPath);
        
        /// <summary>
        /// 写入音频数据
        /// </summary>
        /// <param name="audioData">音频数据</param>
        /// <param name="source">音频源</param>
        /// <returns>是否成功写入</returns>
        Task<bool> WriteAudioDataAsync(byte[] audioData, AudioSource source);
        
        /// <summary>
        /// 刷新缓冲区
        /// </summary>
        /// <returns>是否成功刷新</returns>
        Task<bool> FlushAsync();
        
        /// <summary>
        /// 完成数据流写入
        /// </summary>
        /// <returns>是否成功完成</returns>
        Task<bool> FinalizeAsync();
        
        /// <summary>
        /// 获取已写入字节数
        /// </summary>
        /// <returns>已写入字节数</returns>
        long GetBytesWritten();
        
        /// <summary>
        /// 检查数据流是否健康
        /// </summary>
        /// <returns>是否健康</returns>
        bool IsHealthy();
        
        /// <summary>
        /// 获取数据流统计信息
        /// </summary>
        /// <returns>数据流统计信息</returns>
        AudioDataFlowStats GetStats();
        
        /// <summary>
        /// 重置统计信息
        /// </summary>
        void ResetStats();
        
        /// <summary>
        /// 是否已初始化
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// 输出文件路径
        /// </summary>
        string? OutputPath { get; }
        
        /// <summary>
        /// 数据流健康状态变化事件
        /// </summary>
        event EventHandler<DataFlowHealthEventArgs> HealthStatusChanged;
        
        /// <summary>
        /// 数据写入事件
        /// </summary>
        event EventHandler<DataWrittenEventArgs> DataWritten;
        
        /// <summary>
        /// 数据流错误事件
        /// </summary>
        event EventHandler<DataFlowErrorEventArgs> ErrorOccurred;
    }
    
    /// <summary>
    /// 音频数据流统计信息
    /// </summary>
    public class AudioDataFlowStats
    {
        /// <summary>
        /// 总写入次数
        /// </summary>
        public long TotalWrites { get; set; }
        
        /// <summary>
        /// 总写入字节数
        /// </summary>
        public long TotalBytesWritten { get; set; }
        
        /// <summary>
        /// 按音频源分组的写入次数
        /// </summary>
        public Dictionary<AudioSource, long> WritesBySource { get; set; } = new();
        
        /// <summary>
        /// 按音频源分组的字节数
        /// </summary>
        public Dictionary<AudioSource, long> BytesBySource { get; set; } = new();
        
        /// <summary>
        /// 最后写入时间
        /// </summary>
        public DateTime LastWriteTime { get; set; }
        
        /// <summary>
        /// 写入失败次数
        /// </summary>
        public long FailedWrites { get; set; }
        
        /// <summary>
        /// 平均写入速度（字节/秒）
        /// </summary>
        public double AverageWriteSpeed { get; set; }
        
        /// <summary>
        /// 是否健康
        /// </summary>
        public bool IsHealthy { get; set; }
        
        /// <summary>
        /// 问题列表
        /// </summary>
        public List<string> Issues { get; set; } = new();
        
        public override string ToString()
        {
            return $"Writes: {TotalWrites}, Bytes: {TotalBytesWritten}, " +
                   $"Failed: {FailedWrites}, Speed: {AverageWriteSpeed:F1} B/s, " +
                   $"Healthy: {IsHealthy}";
        }
    }
    
    /// <summary>
    /// 数据流健康状态事件参数
    /// </summary>
    public class DataFlowHealthEventArgs : EventArgs
    {
        public bool IsHealthy { get; set; }
        public List<string> Issues { get; set; } = new();
        public DateTime Timestamp { get; set; }
        
        public DataFlowHealthEventArgs(bool isHealthy, List<string> issues)
        {
            IsHealthy = isHealthy;
            Issues = issues ?? new List<string>();
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 数据写入事件参数
    /// </summary>
    public class DataWrittenEventArgs : EventArgs
    {
        public AudioSource Source { get; set; }
        public int BytesWritten { get; set; }
        public bool Success { get; set; }
        public DateTime WriteTime { get; set; }
        public string? ErrorMessage { get; set; }
        
        public DataWrittenEventArgs(AudioSource source, int bytesWritten, bool success, string? errorMessage = null)
        {
            Source = source;
            BytesWritten = bytesWritten;
            Success = success;
            WriteTime = DateTime.UtcNow;
            ErrorMessage = errorMessage;
        }
    }
    
    /// <summary>
    /// 数据流错误事件参数
    /// </summary>
    public class DataFlowErrorEventArgs : EventArgs
    {
        public DataFlowErrorType ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public Exception? Exception { get; set; }
        public DateTime Timestamp { get; set; }
        
        public DataFlowErrorEventArgs(DataFlowErrorType errorType, string errorMessage, Exception? exception = null)
        {
            ErrorType = errorType;
            ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
            Exception = exception;
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 数据流错误类型
    /// </summary>
    public enum DataFlowErrorType
    {
        InitializationFailed,
        WriteError,
        FlushError,
        FileSystemError,
        BufferOverflow,
        FormatError,
        Unknown
    }
}