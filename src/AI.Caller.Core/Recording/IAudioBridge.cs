using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 音频桥接器接口，负责在SIPClient和录音系统之间传递音频数据
    /// </summary>
    public interface IAudioBridge
    {
        /// <summary>
        /// 音频数据接收事件
        /// </summary>
        event EventHandler<AudioBridgeDataEventArgs> AudioDataReceived;
        
        /// <summary>
        /// 转发音频数据到录音系统
        /// </summary>
        /// <param name="source">音频源</param>
        /// <param name="audioData">音频数据</param>
        /// <param name="format">音频格式</param>
        void ForwardAudioData(AudioSource source, byte[] audioData, AudioFormat format);
        
        /// <summary>
        /// 注册录音管理器
        /// </summary>
        /// <param name="recordingManager">录音管理器实例</param>
        void RegisterRecordingManager(IAudioRecordingManager recordingManager);
        
        /// <summary>
        /// 注销录音管理器
        /// </summary>
        void UnregisterRecordingManager();
        
        /// <summary>
        /// 检查录音是否激活
        /// </summary>
        bool IsRecordingActive { get; }
        
        /// <summary>
        /// 获取音频统计信息
        /// </summary>
        AudioBridgeStats GetStats();
        
        /// <summary>
        /// 重置统计信息
        /// </summary>
        void ResetStats();
        
        /// <summary>
        /// 启动桥接器
        /// </summary>
        void Start();
        
        /// <summary>
        /// 停止桥接器
        /// </summary>
        void Stop();
        
        /// <summary>
        /// 桥接器是否正在运行
        /// </summary>
        bool IsRunning { get; }
    }
    
    /// <summary>
    /// 音频桥接器数据事件参数
    /// </summary>
    public class AudioBridgeDataEventArgs : EventArgs
    {
        public AudioSource Source { get; set; }
        public byte[] AudioData { get; set; }
        public AudioFormat Format { get; set; }
        public DateTime Timestamp { get; set; }
        public int SequenceNumber { get; set; }
        
        public AudioBridgeDataEventArgs(AudioSource source, byte[] audioData, AudioFormat format)
        {
            Source = source;
            AudioData = audioData ?? throw new ArgumentNullException(nameof(audioData));
            Format = format ?? throw new ArgumentNullException(nameof(format));
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 音频桥接器统计信息
    /// </summary>
    public class AudioBridgeStats
    {
        public long TotalFramesForwarded { get; set; }
        public long TotalBytesForwarded { get; set; }
        public Dictionary<AudioSource, long> FramesBySource { get; set; } = new();
        public Dictionary<AudioSource, long> BytesBySource { get; set; } = new();
        public DateTime LastDataReceived { get; set; }
        public bool IsHealthy { get; set; }
        public List<string> Issues { get; set; } = new();
        
        public override string ToString()
        {
            return $"Total: {TotalFramesForwarded} frames ({TotalBytesForwarded} bytes), " +
                   $"Last: {LastDataReceived:HH:mm:ss}, Healthy: {IsHealthy}";
        }
    }
}