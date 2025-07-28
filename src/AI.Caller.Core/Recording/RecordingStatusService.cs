using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音状态服务实现，提供录音系统状态的实时查询和监控
    /// </summary>
    public class RecordingStatusService : IRecordingStatusService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly Timer _statusCheckTimer;
        private readonly object _lockObject = new object();
        
        private bool _disposed = false;
        private DateTime _systemStartTime = DateTime.UtcNow;
        private SystemHealthStatus _lastStatus = new();
        
        // 组件引用
        private IRecordingCore? _recordingCore;
        private AudioDataFlowMonitor? _dataFlowMonitor;
        private IAudioBridge? _audioBridge;
        
        public event EventHandler<SystemStatusChangedEventArgs>? SystemStatusChanged;
        public event EventHandler<HealthWarningEventArgs>? HealthWarning;
        public event EventHandler<SystemRecoveredEventArgs>? SystemRecovered;
        
        public RecordingStatusService(ILogger<RecordingStatusService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 创建状态检查定时器（每2秒检查一次）
            _statusCheckTimer = new Timer(PerformStatusCheck, null, 
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            
            _logger.LogInformation("RecordingStatusService initialized");
        }
        
        /// <summary>
        /// 注册录音核心组件
        /// </summary>
        /// <param name="recordingCore">录音核心</param>
        public void RegisterRecordingCore(IRecordingCore recordingCore)
        {
            lock (_lockObject)
            {
                _recordingCore = recordingCore;
                _logger.LogInformation("RecordingCore registered with status service");
            }
        }
        
        /// <summary>
        /// 注册数据流监控器
        /// </summary>
        /// <param name="dataFlowMonitor">数据流监控器</param>
        public void RegisterDataFlowMonitor(AudioDataFlowMonitor dataFlowMonitor)
        {
            lock (_lockObject)
            {
                _dataFlowMonitor = dataFlowMonitor;
                
                // 订阅数据流监控事件
                _dataFlowMonitor.HealthStatusChanged += OnDataFlowHealthChanged;
                _dataFlowMonitor.DataFlowInterrupted += OnDataFlowInterrupted;
                _dataFlowMonitor.QualityAssessed += OnQualityAssessed;
                
                _logger.LogInformation("DataFlowMonitor registered with status service");
            }
        }
        
        /// <summary>
        /// 注册音频桥接器
        /// </summary>
        /// <param name="audioBridge">音频桥接器</param>
        public void RegisterAudioBridge(IAudioBridge audioBridge)
        {
            lock (_lockObject)
            {
                _audioBridge = audioBridge;
                _logger.LogInformation("AudioBridge registered with status service");
            }
        }
        
        public RecordingHealthStatus GetHealthStatus()
        {
            lock (_lockObject)
            {
                return _recordingCore?.GetHealthStatus() ?? new RecordingHealthStatus
                {
                    Quality = RecordingQuality.Unknown,
                    Issues = new List<string> { "RecordingCore not available" }
                };
            }
        }
        
        public DataFlowMonitorStatus GetDataFlowStatus()
        {
            lock (_lockObject)
            {
                if (_dataFlowMonitor == null)
                {
                    return new DataFlowMonitorStatus
                    {
                        IsHealthy = false,
                        Quality = RecordingQuality.Unknown,
                        CurrentIssues = new List<string> { "DataFlowMonitor not available" }
                    };
                }
                
                var healthStatus = _dataFlowMonitor.CurrentHealthStatus;
                return new DataFlowMonitorStatus
                {
                    IsHealthy = healthStatus.IsHealthy,
                    Quality = healthStatus.Quality,
                    LastDataReceived = healthStatus.LastDataReceived,
                    TimeSinceLastData = DateTime.UtcNow - healthStatus.LastDataReceived,
                    TotalDataReceived = healthStatus.BytesWritten,
                    TotalFramesReceived = healthStatus.AudioFrameCount,
                    DataBySource = new Dictionary<AudioSource, long>(), // 简化实现
                    CurrentIssues = new List<string>(healthStatus.Issues),
                    MonitoringStartTime = healthStatus.RecordingStartTime
                };
            }
        }
        
        public AudioBridgeStats GetAudioBridgeStats()
        {
            lock (_lockObject)
            {
                return _audioBridge?.GetStats() ?? new AudioBridgeStats
                {
                    IsHealthy = false,
                    Issues = new List<string> { "AudioBridge not available" }
                };
            }
        }
        
        public SystemHealthStatus GetSystemStatus()
        {
            lock (_lockObject)
            {
                var recordingStatus = GetHealthStatus();
                var dataFlowStatus = GetDataFlowStatus();
                var audioBridgeStatus = GetAudioBridgeStats();
                
                var systemStatus = new SystemHealthStatus
                {
                    RecordingStatus = recordingStatus,
                    DataFlowStatus = dataFlowStatus,
                    AudioBridgeStatus = audioBridgeStatus,
                    Uptime = DateTime.UtcNow - _systemStartTime,
                    CheckTime = DateTime.UtcNow
                };
                
                // 评估整体健康状态
                systemStatus.IsHealthy = recordingStatus.IsHealthy && 
                                        dataFlowStatus.IsHealthy && 
                                        audioBridgeStatus.IsHealthy;
                
                // 评估整体质量
                var qualities = new[] { recordingStatus.Quality, dataFlowStatus.Quality };
                systemStatus.OverallQuality = GetWorstQuality(qualities);
                
                // 收集所有问题
                systemStatus.Issues.AddRange(ConvertToSystemIssues(recordingStatus.Issues, "RecordingCore"));
                systemStatus.Issues.AddRange(ConvertToSystemIssues(dataFlowStatus.CurrentIssues, "DataFlowMonitor"));
                systemStatus.Issues.AddRange(ConvertToSystemIssues(audioBridgeStatus.Issues, "AudioBridge"));
                
                return systemStatus;
            }
        }
        
        public void ResetAllStats()
        {
            lock (_lockObject)
            {
                _audioBridge?.ResetStats();
                
                _systemStartTime = DateTime.UtcNow;
                
                _logger.LogInformation("All recording system statistics reset");
            }
        }
        
        private void PerformStatusCheck(object? state)
        {
            if (_disposed)
                return;
                
            try
            {
                var currentStatus = GetSystemStatus();
                
                lock (_lockObject)
                {
                    // 检查状态是否发生变化
                    if (HasStatusChanged(_lastStatus, currentStatus))
                    {
                        var eventArgs = new SystemStatusChangedEventArgs
                        {
                            PreviousStatus = _lastStatus,
                            CurrentStatus = currentStatus
                        };
                        
                        _logger.LogInformation($"System status changed: {currentStatus}");
                        SystemStatusChanged?.Invoke(this, eventArgs);
                        
                        // 检查是否有新的警告
                        CheckForNewWarnings(_lastStatus, currentStatus);
                        
                        // 检查是否系统恢复
                        if (eventArgs.HealthImproved)
                        {
                            var recoveredIssues = GetResolvedIssues(_lastStatus, currentStatus);
                            SystemRecovered?.Invoke(this, new SystemRecoveredEventArgs
                            {
                                ResolvedIssues = recoveredIssues,
                                DowntimeDuration = currentStatus.Uptime // 简化实现
                            });
                        }
                        
                        _lastStatus = currentStatus;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during status check: {ex.Message}");
            }
        }
        
        private bool HasStatusChanged(SystemHealthStatus previous, SystemHealthStatus current)
        {
            return previous.IsHealthy != current.IsHealthy ||
                   previous.OverallQuality != current.OverallQuality ||
                   previous.Issues.Count != current.Issues.Count;
        }
        
        private void CheckForNewWarnings(SystemHealthStatus previous, SystemHealthStatus current)
        {
            var newIssues = current.Issues.Where(issue => 
                !previous.Issues.Any(prevIssue => prevIssue.Description == issue.Description))
                .ToList();
                
            foreach (var issue in newIssues)
            {
                if (issue.Severity >= IssueSeverity.Warning)
                {
                    HealthWarning?.Invoke(this, new HealthWarningEventArgs
                    {
                        Issue = issue,
                        CurrentStatus = current
                    });
                }
            }
        }
        
        private List<SystemIssue> GetResolvedIssues(SystemHealthStatus previous, SystemHealthStatus current)
        {
            return previous.Issues.Where(prevIssue => 
                !current.Issues.Any(issue => issue.Description == prevIssue.Description))
                .ToList();
        }
        
        private RecordingQuality GetWorstQuality(RecordingQuality[] qualities)
        {
            // 按照质量从差到好的顺序检查，返回最差的质量
            if (qualities.Contains(RecordingQuality.Unknown))
                return RecordingQuality.Unknown;
            if (qualities.Contains(RecordingQuality.Poor))
                return RecordingQuality.Poor;
            if (qualities.Contains(RecordingQuality.Fair))
                return RecordingQuality.Fair;
            if (qualities.Contains(RecordingQuality.Good))
                return RecordingQuality.Good;
            if (qualities.Contains(RecordingQuality.Excellent))
                return RecordingQuality.Excellent;
            return RecordingQuality.Unknown;
        }
        
        private List<SystemIssue> ConvertToSystemIssues(List<string> issues, string component)
        {
            return issues.Select(issue => new SystemIssue
            {
                Type = DetermineIssueType(issue),
                Description = issue,
                Severity = DetermineIssueSeverity(issue),
                Component = component
            }).ToList();
        }
        
        private SystemIssueType DetermineIssueType(string issue)
        {
            if (issue.Contains("data") || issue.Contains("flow"))
                return SystemIssueType.DataFlowInterruption;
            if (issue.Contains("recording"))
                return SystemIssueType.RecordingFailure;
            if (issue.Contains("file"))
                return SystemIssueType.FileSystemError;
            if (issue.Contains("performance") || issue.Contains("speed"))
                return SystemIssueType.PerformanceIssue;
            return SystemIssueType.Unknown;
        }
        
        private IssueSeverity DetermineIssueSeverity(string issue)
        {
            if (issue.Contains("critical") || issue.Contains("failed"))
                return IssueSeverity.Critical;
            if (issue.Contains("error"))
                return IssueSeverity.Error;
            if (issue.Contains("warning") || issue.Contains("slow"))
                return IssueSeverity.Warning;
            return IssueSeverity.Info;
        }
        
        private void OnDataFlowHealthChanged(object? sender, RecordingHealthStatus e)
        {
            _logger.LogInformation($"Data flow health changed: {e.Quality}");
        }
        
        private void OnDataFlowInterrupted(object? sender, DataFlowInterruptionEventArgs e)
        {
            _logger.LogWarning($"Data flow interrupted: {e}");
        }
        
        private void OnQualityAssessed(object? sender, QualityAssessmentEventArgs e)
        {
            _logger.LogDebug($"Quality assessed: {e}");
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            try
            {
                _statusCheckTimer?.Dispose();
                
                // 取消订阅事件
                if (_dataFlowMonitor != null)
                {
                    _dataFlowMonitor.HealthStatusChanged -= OnDataFlowHealthChanged;
                    _dataFlowMonitor.DataFlowInterrupted -= OnDataFlowInterrupted;
                    _dataFlowMonitor.QualityAssessed -= OnQualityAssessed;
                }
                
                _logger.LogInformation("RecordingStatusService disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RecordingStatusService");
            }
        }
    }
    
    /// <summary>
    /// 数据流监控状态
    /// </summary>
    public class DataFlowMonitorStatus
    {
        public bool IsHealthy { get; set; }
        public RecordingQuality Quality { get; set; }
        public DateTime LastDataReceived { get; set; }
        public TimeSpan TimeSinceLastData { get; set; }
        public long TotalDataReceived { get; set; }
        public long TotalFramesReceived { get; set; }
        public Dictionary<AudioSource, long> DataBySource { get; set; } = new();
        public List<string> CurrentIssues { get; set; } = new();
        public DateTime MonitoringStartTime { get; set; }
        
        public override string ToString()
        {
            return $"Healthy: {IsHealthy}, Quality: {Quality}, " +
                   $"Frames: {TotalFramesReceived}, Data: {TotalDataReceived} bytes, " +
                   $"Issues: {CurrentIssues.Count}";
        }
    }
}