using System.Text.Json;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 音频流统计信息
    /// </summary>
    public class AudioStreamStats
    {
        /// <summary>
        /// 总处理帧数
        /// </summary>
        public long TotalFramesProcessed { get; set; }
        
        /// <summary>
        /// 总处理字节数
        /// </summary>
        public long TotalBytesProcessed { get; set; }
        
        /// <summary>
        /// 平均延迟（毫秒）
        /// </summary>
        public double AverageLatency { get; set; }
        
        /// <summary>
        /// 丢包率（百分比）
        /// </summary>
        public double PacketLossRate { get; set; }
        
        /// <summary>
        /// 最后一帧时间
        /// </summary>
        public DateTime LastFrameTime { get; set; }
        
        /// <summary>
        /// 音频质量指标
        /// </summary>
        public AudioQualityMetrics QualityMetrics { get; set; } = new AudioQualityMetrics();
        
        /// <summary>
        /// 统计开始时间
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 统计持续时间
        /// </summary>
        public TimeSpan Duration => DateTime.UtcNow - StartTime;
        
        /// <summary>
        /// 平均帧率（帧/秒）
        /// </summary>
        public double AverageFrameRate => Duration.TotalSeconds > 0 ? TotalFramesProcessed / Duration.TotalSeconds : 0;
        
        /// <summary>
        /// 平均比特率（比特/秒）
        /// </summary>
        public double AverageBitRate => Duration.TotalSeconds > 0 ? (TotalBytesProcessed * 8) / Duration.TotalSeconds : 0;
        
        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            TotalFramesProcessed = 0;
            TotalBytesProcessed = 0;
            AverageLatency = 0;
            PacketLossRate = 0;
            LastFrameTime = DateTime.MinValue;
            QualityMetrics.Reset();
            StartTime = DateTime.UtcNow;
        }
        
        /// <summary>
        /// 更新统计信息
        /// </summary>
        /// <param name="frameSize">帧大小</param>
        /// <param name="latency">延迟</param>
        public void UpdateStats(int frameSize, TimeSpan latency)
        {
            TotalFramesProcessed++;
            TotalBytesProcessed += frameSize;
            LastFrameTime = DateTime.UtcNow;
            
            // 更新平均延迟（使用指数移动平均）
            var latencyMs = latency.TotalMilliseconds;
            AverageLatency = AverageLatency == 0 ? latencyMs : (AverageLatency * 0.9 + latencyMs * 0.1);
        }
        
        /// <summary>
        /// 序列化为JSON
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        
        /// <summary>
        /// 从JSON反序列化
        /// </summary>
        public static AudioStreamStats? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<AudioStreamStats>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch
            {
                return null;
            }
        }
        
        public override string ToString()
        {
            return $"Frames: {TotalFramesProcessed}, Bytes: {TotalBytesProcessed}, " +
                   $"Latency: {AverageLatency:F2}ms, Loss: {PacketLossRate:F2}%, " +
                   $"FrameRate: {AverageFrameRate:F1}fps, BitRate: {AverageBitRate:F0}bps";
        }
    }
    
    /// <summary>
    /// 音频质量指标
    /// </summary>
    public class AudioQualityMetrics
    {
        /// <summary>
        /// 信噪比（dB）
        /// </summary>
        public double SignalToNoiseRatio { get; set; }
        
        /// <summary>
        /// 音频电平（dB）
        /// </summary>
        public double AudioLevel { get; set; }
        
        /// <summary>
        /// 丢弃的帧数
        /// </summary>
        public int DroppedFrames { get; set; }
        
        /// <summary>
        /// 重复的帧数
        /// </summary>
        public int DuplicatedFrames { get; set; }
        
        /// <summary>
        /// 平均延迟
        /// </summary>
        public TimeSpan AverageDelay { get; set; }
        
        /// <summary>
        /// 最大延迟
        /// </summary>
        public TimeSpan MaxDelay { get; set; }
        
        /// <summary>
        /// 最小延迟
        /// </summary>
        public TimeSpan MinDelay { get; set; } = TimeSpan.MaxValue;
        
        /// <summary>
        /// 延迟抖动
        /// </summary>
        public TimeSpan Jitter { get; set; }
        
        /// <summary>
        /// 音频中断次数
        /// </summary>
        public int AudioInterruptions { get; set; }
        
        /// <summary>
        /// 缓冲区下溢次数
        /// </summary>
        public int BufferUnderflows { get; set; }
        
        /// <summary>
        /// 缓冲区溢出次数
        /// </summary>
        public int BufferOverflows { get; set; }
        
        /// <summary>
        /// 重置质量指标
        /// </summary>
        public void Reset()
        {
            SignalToNoiseRatio = 0;
            AudioLevel = 0;
            DroppedFrames = 0;
            DuplicatedFrames = 0;
            AverageDelay = TimeSpan.Zero;
            MaxDelay = TimeSpan.Zero;
            MinDelay = TimeSpan.MaxValue;
            Jitter = TimeSpan.Zero;
            AudioInterruptions = 0;
            BufferUnderflows = 0;
            BufferOverflows = 0;
        }
        
        /// <summary>
        /// 更新延迟统计
        /// </summary>
        public void UpdateDelay(TimeSpan delay)
        {
            if (delay > MaxDelay)
                MaxDelay = delay;
                
            if (delay < MinDelay)
                MinDelay = delay;
                
            // 更新平均延迟（指数移动平均）
            AverageDelay = AverageDelay == TimeSpan.Zero ? delay : 
                TimeSpan.FromMilliseconds(AverageDelay.TotalMilliseconds * 0.9 + delay.TotalMilliseconds * 0.1);
                
            // 计算抖动（延迟变化）
            var jitterMs = Math.Abs(delay.TotalMilliseconds - AverageDelay.TotalMilliseconds);
            Jitter = TimeSpan.FromMilliseconds(Jitter.TotalMilliseconds * 0.9 + jitterMs * 0.1);
        }
        
        /// <summary>
        /// 计算音频电平（简化实现）
        /// </summary>
        public void UpdateAudioLevel(byte[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return;
                
            // 简化的RMS计算
            long sum = 0;
            for (int i = 0; i < audioData.Length; i += 2) // 假设16位音频
            {
                if (i + 1 < audioData.Length)
                {
                    var sample = BitConverter.ToInt16(audioData, i);
                    sum += sample * sample;
                }
            }
            
            var rms = Math.Sqrt(sum / (audioData.Length / 2.0));
            var dbLevel = 20 * Math.Log10(rms / 32768.0); // 相对于16位最大值
            
            // 使用指数移动平均更新音频电平
            AudioLevel = AudioLevel == 0 ? dbLevel : (AudioLevel * 0.9 + dbLevel * 0.1);
        }
        
        public override string ToString()
        {
            return $"SNR: {SignalToNoiseRatio:F1}dB, Level: {AudioLevel:F1}dB, " +
                   $"Dropped: {DroppedFrames}, Jitter: {Jitter.TotalMilliseconds:F1}ms, " +
                   $"Delay: {AverageDelay.TotalMilliseconds:F1}ms";
        }
    }
    
    /// <summary>
    /// 音频路由事件参数
    /// </summary>
    public class AudioRoutedEventArgs : EventArgs
    {
        public AudioFrame Frame { get; }
        public AudioSource Source { get; }
        public AudioSource Destination { get; }
        public TimeSpan ProcessingTime { get; }
        public bool Success { get; }
        public string? ErrorMessage { get; }
        
        public AudioRoutedEventArgs(AudioFrame frame, AudioSource source, AudioSource destination, 
            TimeSpan processingTime, bool success, string? errorMessage = null)
        {
            Frame = frame;
            Source = source;
            Destination = destination;
            ProcessingTime = processingTime;
            Success = success;
            ErrorMessage = errorMessage;
        }
    }
    
    /// <summary>
    /// 流质量事件参数
    /// </summary>
    public class StreamQualityEventArgs : EventArgs
    {
        public AudioStreamStats StreamStats { get; }
        public AudioQualityLevel QualityLevel { get; }
        public string? Message { get; }
        
        public StreamQualityEventArgs(AudioStreamStats streamStats, AudioQualityLevel qualityLevel, string? message = null)
        {
            StreamStats = streamStats;
            QualityLevel = qualityLevel;
            Message = message;
        }
    }
    
    /// <summary>
    /// 音频质量等级
    /// </summary>
    public enum AudioQualityLevel
    {
        Excellent,
        Good,
        Fair,
        Poor,
        Critical
    }
}