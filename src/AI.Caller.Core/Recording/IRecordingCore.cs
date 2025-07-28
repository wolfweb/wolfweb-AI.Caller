namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音核心接口，专门处理录音逻辑的独立组件
    /// </summary>
    public interface IRecordingCore
    {
        /// <summary>
        /// 开始录音
        /// </summary>
        /// <param name="options">录音选项</param>
        /// <returns>是否成功开始录音</returns>
        Task<bool> StartRecordingAsync(RecordingOptions options);
        
        /// <summary>
        /// 停止录音
        /// </summary>
        /// <returns>录音文件路径，如果失败则返回null</returns>
        Task<string?> StopRecordingAsync();
        
        /// <summary>
        /// 暂停录音
        /// </summary>
        /// <returns>是否成功暂停</returns>
        Task<bool> PauseRecordingAsync();
        
        /// <summary>
        /// 恢复录音
        /// </summary>
        /// <returns>是否成功恢复</returns>
        Task<bool> ResumeRecordingAsync();
        
        /// <summary>
        /// 取消录音
        /// </summary>
        /// <returns>是否成功取消</returns>
        Task<bool> CancelRecordingAsync();
        
        /// <summary>
        /// 处理音频数据
        /// </summary>
        /// <param name="source">音频源</param>
        /// <param name="data">音频数据</param>
        /// <param name="format">音频格式</param>
        void ProcessAudioData(AudioSource source, byte[] data, AudioFormat format);
        
        /// <summary>
        /// 获取录音状态
        /// </summary>
        /// <returns>当前录音状态</returns>
        RecordingStatus GetStatus();
        
        /// <summary>
        /// 获取录音健康状态
        /// </summary>
        /// <returns>录音健康状态</returns>
        RecordingHealthStatus GetHealthStatus();
        
        /// <summary>
        /// 是否正在录音
        /// </summary>
        bool IsRecording { get; }
        
        /// <summary>
        /// 录音时长
        /// </summary>
        TimeSpan RecordingDuration { get; }
        
        /// <summary>
        /// 录音状态变化事件
        /// </summary>
        event EventHandler<RecordingStatusEventArgs> StatusChanged;
        
        /// <summary>
        /// 录音进度更新事件
        /// </summary>
        event EventHandler<RecordingProgressEventArgs> ProgressUpdated;
        
        /// <summary>
        /// 录音错误事件
        /// </summary>
        event EventHandler<RecordingErrorEventArgs> ErrorOccurred;
    }
    
    /// <summary>
    /// 录音健康状态
    /// </summary>
    public class RecordingHealthStatus
    {
        /// <summary>
        /// 数据是否正在流动
        /// </summary>
        public bool IsDataFlowing { get; set; }
        
        /// <summary>
        /// 已写入字节数
        /// </summary>
        public long BytesWritten { get; set; }
        
        /// <summary>
        /// 最后接收数据时间
        /// </summary>
        public DateTime LastDataReceived { get; set; }
        
        /// <summary>
        /// 问题列表
        /// </summary>
        public List<string> Issues { get; set; } = new();
        
        /// <summary>
        /// 录音质量
        /// </summary>
        public RecordingQuality Quality { get; set; }
        
        /// <summary>
        /// 是否健康
        /// </summary>
        public bool IsHealthy => Issues.Count == 0 && IsDataFlowing;
        
        /// <summary>
        /// 数据流中断时长
        /// </summary>
        public TimeSpan DataFlowInterruption => DateTime.UtcNow - LastDataReceived;
        
        /// <summary>
        /// 录音开始时间
        /// </summary>
        public DateTime RecordingStartTime { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 录音持续时间
        /// </summary>
        public TimeSpan RecordingDuration => DateTime.UtcNow - RecordingStartTime;
        
        /// <summary>
        /// 平均数据流速率（字节/秒）
        /// </summary>
        public double AverageDataRate { get; set; }
        
        /// <summary>
        /// 音频帧计数
        /// </summary>
        public long AudioFrameCount { get; set; }
        
        /// <summary>
        /// 丢失的音频帧数
        /// </summary>
        public long LostFrameCount { get; set; }
        
        /// <summary>
        /// 音频帧丢失率
        /// </summary>
        public double FrameLossRate => AudioFrameCount > 0 
            ? (double)LostFrameCount / AudioFrameCount 
            : 0.0;
        
        /// <summary>
        /// 缓冲区使用情况
        /// </summary>
        public BufferUsageInfo BufferUsage { get; set; } = new();
        
        /// <summary>
        /// 编码器状态
        /// </summary>
        public EncoderHealthInfo EncoderHealth { get; set; } = new();
        
        /// <summary>
        /// 文件系统状态
        /// </summary>
        public FileSystemHealthInfo FileSystemHealth { get; set; } = new();
        
        /// <summary>
        /// 创建健康状态的副本
        /// </summary>
        /// <returns>健康状态副本</returns>
        public RecordingHealthStatus Clone()
        {
            return new RecordingHealthStatus
            {
                IsDataFlowing = IsDataFlowing,
                BytesWritten = BytesWritten,
                LastDataReceived = LastDataReceived,
                Issues = new List<string>(Issues),
                Quality = Quality,
                RecordingStartTime = RecordingStartTime,
                AverageDataRate = AverageDataRate,
                AudioFrameCount = AudioFrameCount,
                LostFrameCount = LostFrameCount,
                BufferUsage = BufferUsage.Clone(),
                EncoderHealth = EncoderHealth.Clone(),
                FileSystemHealth = FileSystemHealth.Clone()
            };
        }
        
        public override string ToString()
        {
            return $"Healthy: {IsHealthy}, DataFlowing: {IsDataFlowing}, " +
                   $"BytesWritten: {BytesWritten}, Issues: {Issues.Count}, " +
                   $"Quality: {Quality}, FrameLoss: {FrameLossRate:P2}";
        }
    }
    
    /// <summary>
    /// 录音质量枚举
    /// </summary>
    public enum RecordingQuality
    {
        Unknown,
        Poor,
        Fair,
        Good,
        Excellent
    }
    
    /// <summary>
    /// 缓冲区使用信息
    /// </summary>
    public class BufferUsageInfo
    {
        /// <summary>
        /// 当前缓冲区大小
        /// </summary>
        public int CurrentSize { get; set; }
        
        /// <summary>
        /// 最大缓冲区大小
        /// </summary>
        public int MaxSize { get; set; }
        
        /// <summary>
        /// 缓冲区使用率
        /// </summary>
        public double UsagePercentage => MaxSize > 0 ? (double)CurrentSize / MaxSize * 100 : 0;
        
        /// <summary>
        /// 是否接近满载
        /// </summary>
        public bool IsNearFull => UsagePercentage > 80;
        
        /// <summary>
        /// 缓冲区溢出次数
        /// </summary>
        public int OverflowCount { get; set; }
        
        public BufferUsageInfo Clone()
        {
            return new BufferUsageInfo
            {
                CurrentSize = CurrentSize,
                MaxSize = MaxSize,
                OverflowCount = OverflowCount
            };
        }
        
        public override string ToString()
        {
            return $"Buffer: {CurrentSize}/{MaxSize} ({UsagePercentage:F1}%), Overflows: {OverflowCount}";
        }
    }
    
    /// <summary>
    /// 编码器健康信息
    /// </summary>
    public class EncoderHealthInfo
    {
        /// <summary>
        /// 编码器是否正常工作
        /// </summary>
        public bool IsWorking { get; set; } = true;
        
        /// <summary>
        /// 编码失败次数
        /// </summary>
        public int FailureCount { get; set; }
        
        /// <summary>
        /// 最后一次编码时间
        /// </summary>
        public DateTime LastEncodeTime { get; set; }
        
        /// <summary>
        /// 平均编码时间（毫秒）
        /// </summary>
        public double AverageEncodeTime { get; set; }
        
        /// <summary>
        /// 编码器类型
        /// </summary>
        public string EncoderType { get; set; } = "Unknown";
        
        /// <summary>
        /// 编码器版本
        /// </summary>
        public string EncoderVersion { get; set; } = "Unknown";
        
        public EncoderHealthInfo Clone()
        {
            return new EncoderHealthInfo
            {
                IsWorking = IsWorking,
                FailureCount = FailureCount,
                LastEncodeTime = LastEncodeTime,
                AverageEncodeTime = AverageEncodeTime,
                EncoderType = EncoderType,
                EncoderVersion = EncoderVersion
            };
        }
        
        public override string ToString()
        {
            return $"Encoder: {EncoderType} v{EncoderVersion}, Working: {IsWorking}, " +
                   $"Failures: {FailureCount}, AvgTime: {AverageEncodeTime:F1}ms";
        }
    }
    
    /// <summary>
    /// 文件系统健康信息
    /// </summary>
    public class FileSystemHealthInfo
    {
        /// <summary>
        /// 可用磁盘空间（字节）
        /// </summary>
        public long AvailableDiskSpace { get; set; }
        
        /// <summary>
        /// 总磁盘空间（字节）
        /// </summary>
        public long TotalDiskSpace { get; set; }
        
        /// <summary>
        /// 磁盘使用率
        /// </summary>
        public double DiskUsagePercentage => TotalDiskSpace > 0 
            ? (double)(TotalDiskSpace - AvailableDiskSpace) / TotalDiskSpace * 100 
            : 0;
        
        /// <summary>
        /// 磁盘空间是否不足
        /// </summary>
        public bool IsLowOnSpace => DiskUsagePercentage > 90 || AvailableDiskSpace < 1024 * 1024 * 100; // 小于100MB
        
        /// <summary>
        /// 文件写入失败次数
        /// </summary>
        public int WriteFailureCount { get; set; }
        
        /// <summary>
        /// 最后一次写入时间
        /// </summary>
        public DateTime LastWriteTime { get; set; }
        
        /// <summary>
        /// 输出文件路径
        /// </summary>
        public string? OutputPath { get; set; }
        
        /// <summary>
        /// 文件是否可写
        /// </summary>
        public bool IsWritable { get; set; } = true;
        
        public FileSystemHealthInfo Clone()
        {
            return new FileSystemHealthInfo
            {
                AvailableDiskSpace = AvailableDiskSpace,
                TotalDiskSpace = TotalDiskSpace,
                WriteFailureCount = WriteFailureCount,
                  
         LastWriteTime = LastWriteTime,
                OutputPath = OutputPath,
                IsWritable = IsWritable
            };
        }
        
        public override string ToString()
        {
            return $"Disk: {AvailableDiskSpace / (1024 * 1024)}MB free ({100 - DiskUsagePercentage:F1}% available), " +
                   $"WriteFailures: {WriteFailureCount}, Writable: {IsWritable}";
        }
    }
}