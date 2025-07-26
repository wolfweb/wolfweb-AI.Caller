using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 音频质量监控器接口
    /// </summary>
    public interface IAudioQualityMonitor : IDisposable
    {
        /// <summary>
        /// 开始监控
        /// </summary>
        void StartMonitoring();
        
        /// <summary>
        /// 停止监控
        /// </summary>
        void StopMonitoring();
        
        /// <summary>
        /// 处理音频帧
        /// </summary>
        void ProcessAudioFrame(AudioFrame frame, AudioSource source);
        
        /// <summary>
        /// 获取统计信息
        /// </summary>
        AudioStreamStats GetStreamStats(AudioSource source);
        
        /// <summary>
        /// 获取所有统计信息
        /// </summary>
        Dictionary<AudioSource, AudioStreamStats> GetAllStreamStats();
        
        /// <summary>
        /// 重置统计信息
        /// </summary>
        void ResetStats();
        
        /// <summary>
        /// 质量变化事件
        /// </summary>
        event EventHandler<StreamQualityEventArgs>? QualityChanged;
        
        /// <summary>
        /// 质量警告事件
        /// </summary>
        event EventHandler<QualityWarningEventArgs>? QualityWarning;
    }
    
    /// <summary>
    /// 音频质量监控器实现
    /// </summary>
    public class AudioQualityMonitor : IAudioQualityMonitor
    {
        private readonly ILogger _logger;
        private readonly AudioQualitySettings _settings;
        private readonly ConcurrentDictionary<AudioSource, AudioStreamStats> _streamStats;
        private readonly ConcurrentDictionary<AudioSource, Queue<DateTime>> _frameTimestamps;
        private readonly Timer _monitoringTimer;
        private readonly object _lockObject = new object();
        
        private bool _isMonitoring = false;
        private bool _disposed = false;
        private DateTime _lastQualityCheck = DateTime.UtcNow;
        
        public event EventHandler<StreamQualityEventArgs>? QualityChanged;
        public event EventHandler<QualityWarningEventArgs>? QualityWarning;
        
        public AudioQualityMonitor(AudioQualitySettings settings, ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _streamStats = new ConcurrentDictionary<AudioSource, AudioStreamStats>();
            _frameTimestamps = new ConcurrentDictionary<AudioSource, Queue<DateTime>>();
            
            // 初始化各个音频源的统计信息
            foreach (AudioSource source in Enum.GetValues<AudioSource>())
            {
                _streamStats[source] = new AudioStreamStats();
                _frameTimestamps[source] = new Queue<DateTime>();
            }
            
            // 创建监控定时器
            _monitoringTimer = new Timer(PerformQualityCheck, null, Timeout.Infinite, Timeout.Infinite);
        }
        
        public void StartMonitoring()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioQualityMonitor));
                
            lock (_lockObject)
            {
                if (_isMonitoring)
                    return;
                    
                _isMonitoring = true;
                _lastQualityCheck = DateTime.UtcNow;
                
                // 启动监控定时器
                _monitoringTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(_settings.MonitoringInterval));
                
                _logger.LogInformation("Audio quality monitoring started");
            }
        }
        
        public void StopMonitoring()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return;
                    
                _isMonitoring = false;
                
                // 停止监控定时器
                _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);
                
                _logger.LogInformation("Audio quality monitoring stopped");
            }
        }
        
        public void ProcessAudioFrame(AudioFrame frame, AudioSource source)
        {
            if (_disposed || !_isMonitoring || frame?.Data == null)
                return;
                
            try
            {
                var now = DateTime.UtcNow;
                var stats = _streamStats[source];
                var timestamps = _frameTimestamps[source];
                
                // 计算延迟
                var latency = now - frame.Timestamp;
                
                // 更新统计信息
                stats.UpdateStats(frame.Data.Length, latency);
                stats.QualityMetrics.UpdateDelay(latency);
                stats.QualityMetrics.UpdateAudioLevel(frame.Data);
                
                // 管理时间戳队列（用于计算丢包率）
                lock (timestamps)
                {
                    timestamps.Enqueue(now);
                    
                    // 保持队列大小在合理范围内
                    while (timestamps.Count > _settings.MaxTimestampHistory)
                    {
                        timestamps.Dequeue();
                    }
                }
                
                // 检测音频中断
                DetectAudioInterruptions(source, now);
                
                // 计算丢包率
                CalculatePacketLossRate(source);
                
                _logger.LogTrace($"Processed audio frame from {source}: {frame.Data.Length} bytes, latency: {latency.TotalMilliseconds:F1}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing audio frame from {source}: {ex.Message}");
            }
        }
        
        public AudioStreamStats GetStreamStats(AudioSource source)
        {
            return _streamStats.TryGetValue(source, out var stats) ? stats : new AudioStreamStats();
        }
        
        public Dictionary<AudioSource, AudioStreamStats> GetAllStreamStats()
        {
            return new Dictionary<AudioSource, AudioStreamStats>(_streamStats);
        }
        
        public void ResetStats()
        {
            foreach (var stats in _streamStats.Values)
            {
                stats.Reset();
            }
            
            foreach (var timestamps in _frameTimestamps.Values)
            {
                lock (timestamps)
                {
                    timestamps.Clear();
                }
            }
            
            _logger.LogInformation("Audio quality statistics reset");
        }
        
        private void PerformQualityCheck(object? state)
        {
            if (_disposed || !_isMonitoring)
                return;
                
            try
            {
                var now = DateTime.UtcNow;
                
                foreach (var kvp in _streamStats)
                {
                    var source = kvp.Key;
                    var stats = kvp.Value;
                    
                    // 评估音频质量
                    var qualityLevel = EvaluateAudioQuality(stats);
                    
                    // 检查是否需要发出警告
                    CheckForQualityWarnings(source, stats, qualityLevel);
                    
                    // 触发质量变化事件
                    QualityChanged?.Invoke(this, new StreamQualityEventArgs(stats, qualityLevel));
                }
                
                _lastQualityCheck = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during quality check: {ex.Message}");
            }
        }
        
        private AudioQualityLevel EvaluateAudioQuality(AudioStreamStats stats)
        {
            var score = 100.0; // 开始分数
            
            // 延迟影响
            if (stats.AverageLatency > _settings.CriticalLatencyThreshold)
                score -= 40;
            else if (stats.AverageLatency > _settings.WarningLatencyThreshold)
                score -= 20;
                
            // 丢包率影响
            if (stats.PacketLossRate > _settings.CriticalPacketLossThreshold)
                score -= 30;
            else if (stats.PacketLossRate > _settings.WarningPacketLossThreshold)
                score -= 15;
                
            // 抖动影响
            var jitterMs = stats.QualityMetrics.Jitter.TotalMilliseconds;
            if (jitterMs > _settings.CriticalJitterThreshold)
                score -= 20;
            else if (jitterMs > _settings.WarningJitterThreshold)
                score -= 10;
                
            // 音频中断影响
            if (stats.QualityMetrics.AudioInterruptions > _settings.MaxAudioInterruptions)
                score -= 10;
                
            // 根据分数确定质量等级
            return score switch
            {
                >= 90 => AudioQualityLevel.Excellent,
                >= 75 => AudioQualityLevel.Good,
                >= 60 => AudioQualityLevel.Fair,
                >= 40 => AudioQualityLevel.Poor,
                _ => AudioQualityLevel.Critical
            };
        }
        
        private void CheckForQualityWarnings(AudioSource source, AudioStreamStats stats, AudioQualityLevel qualityLevel)
        {
            var warnings = new List<string>();
            
            // 检查延迟
            if (stats.AverageLatency > _settings.WarningLatencyThreshold)
            {
                warnings.Add($"High latency: {stats.AverageLatency:F1}ms");
            }
            
            // 检查丢包率
            if (stats.PacketLossRate > _settings.WarningPacketLossThreshold)
            {
                warnings.Add($"High packet loss: {stats.PacketLossRate:F1}%");
            }
            
            // 检查抖动
            var jitterMs = stats.QualityMetrics.Jitter.TotalMilliseconds;
            if (jitterMs > _settings.WarningJitterThreshold)
            {
                warnings.Add($"High jitter: {jitterMs:F1}ms");
            }
            
            // 检查音频中断
            if (stats.QualityMetrics.AudioInterruptions > _settings.MaxAudioInterruptions)
            {
                warnings.Add($"Audio interruptions: {stats.QualityMetrics.AudioInterruptions}");
            }
            
            // 发出警告
            if (warnings.Any())
            {
                var warningMessage = $"Quality issues for {source}: {string.Join(", ", warnings)}";
                _logger.LogWarning(warningMessage);
                QualityWarning?.Invoke(this, new QualityWarningEventArgs(source, qualityLevel, warningMessage, warnings));
            }
        }
        
        private void DetectAudioInterruptions(AudioSource source, DateTime currentTime)
        {
            var stats = _streamStats[source];
            var timeSinceLastFrame = currentTime - stats.LastFrameTime;
            
            // 如果距离上一帧时间过长，认为是音频中断
            if (stats.LastFrameTime != DateTime.MinValue && 
                timeSinceLastFrame > TimeSpan.FromMilliseconds(_settings.AudioInterruptionThreshold))
            {
                stats.QualityMetrics.AudioInterruptions++;
                _logger.LogWarning($"Audio interruption detected for {source}: {timeSinceLastFrame.TotalMilliseconds:F0}ms gap");
            }
        }
        
        private void CalculatePacketLossRate(AudioSource source)
        {
            var timestamps = _frameTimestamps[source];
            var stats = _streamStats[source];
            
            lock (timestamps)
            {
                if (timestamps.Count < 10) // 需要足够的样本
                    return;
                    
                var now = DateTime.UtcNow;
                var windowStart = now - TimeSpan.FromSeconds(_settings.PacketLossCalculationWindow);
                
                // 计算时间窗口内的帧数
                var framesInWindow = timestamps.Count(t => t >= windowStart);
                
                // 估算期望的帧数（基于典型的音频帧率）
                var expectedFrames = _settings.PacketLossCalculationWindow * _settings.ExpectedFrameRate;
                
                // 计算丢包率
                if (expectedFrames > 0)
                {
                    var lossRate = Math.Max(0, (expectedFrames - framesInWindow) / expectedFrames * 100);
                    stats.PacketLossRate = lossRate;
                }
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            StopMonitoring();
            _monitoringTimer?.Dispose();
            
            _logger.LogInformation("AudioQualityMonitor disposed");
        }
    }
    
    /// <summary>
    /// 音频质量设置
    /// </summary>
    public class AudioQualitySettings
    {
        /// <summary>
        /// 监控间隔（毫秒）
        /// </summary>
        public int MonitoringInterval { get; set; } = 1000;
        
        /// <summary>
        /// 警告延迟阈值（毫秒）
        /// </summary>
        public double WarningLatencyThreshold { get; set; } = 150;
        
        /// <summary>
        /// 严重延迟阈值（毫秒）
        /// </summary>
        public double CriticalLatencyThreshold { get; set; } = 300;
        
        /// <summary>
        /// 警告丢包率阈值（百分比）
        /// </summary>
        public double WarningPacketLossThreshold { get; set; } = 1.0;
        
        /// <summary>
        /// 严重丢包率阈值（百分比）
        /// </summary>
        public double CriticalPacketLossThreshold { get; set; } = 5.0;
        
        /// <summary>
        /// 警告抖动阈值（毫秒）
        /// </summary>
        public double WarningJitterThreshold { get; set; } = 30;
        
        /// <summary>
        /// 严重抖动阈值（毫秒）
        /// </summary>
        public double CriticalJitterThreshold { get; set; } = 100;
        
        /// <summary>
        /// 最大音频中断次数
        /// </summary>
        public int MaxAudioInterruptions { get; set; } = 3;
        
        /// <summary>
        /// 音频中断阈值（毫秒）
        /// </summary>
        public int AudioInterruptionThreshold { get; set; } = 100;
        
        /// <summary>
        /// 最大时间戳历史记录数
        /// </summary>
        public int MaxTimestampHistory { get; set; } = 1000;
        
        /// <summary>
        /// 丢包率计算窗口（秒）
        /// </summary>
        public double PacketLossCalculationWindow { get; set; } = 10.0;
        
        /// <summary>
        /// 期望帧率（帧/秒）
        /// </summary>
        public double ExpectedFrameRate { get; set; } = 50.0;
        
        /// <summary>
        /// 创建默认设置
        /// </summary>
        public static AudioQualitySettings CreateDefault()
        {
            return new AudioQualitySettings();
        }
        
        /// <summary>
        /// 创建严格设置
        /// </summary>
        public static AudioQualitySettings CreateStrict()
        {
            return new AudioQualitySettings
            {
                WarningLatencyThreshold = 100,
                CriticalLatencyThreshold = 200,
                WarningPacketLossThreshold = 0.5,
                CriticalPacketLossThreshold = 2.0,
                WarningJitterThreshold = 20,
                CriticalJitterThreshold = 50,
                MaxAudioInterruptions = 1,
                AudioInterruptionThreshold = 50
            };
        }
        
        /// <summary>
        /// 创建宽松设置
        /// </summary>
        public static AudioQualitySettings CreateLenient()
        {
            return new AudioQualitySettings
            {
                WarningLatencyThreshold = 250,
                CriticalLatencyThreshold = 500,
                WarningPacketLossThreshold = 2.0,
                CriticalPacketLossThreshold = 10.0,
                WarningJitterThreshold = 50,
                CriticalJitterThreshold = 150,
                MaxAudioInterruptions = 5,
                AudioInterruptionThreshold = 200
            };
        }
    }
    
    /// <summary>
    /// 质量警告事件参数
    /// </summary>
    public class QualityWarningEventArgs : EventArgs
    {
        public AudioSource Source { get; }
        public AudioQualityLevel QualityLevel { get; }
        public string Message { get; }
        public List<string> Warnings { get; }
        public DateTime Timestamp { get; }
        
        public QualityWarningEventArgs(AudioSource source, AudioQualityLevel qualityLevel, 
            string message, List<string> warnings)
        {
            Source = source;
            QualityLevel = qualityLevel;
            Message = message;
            Warnings = warnings;
            Timestamp = DateTime.UtcNow;
        }
    }
}