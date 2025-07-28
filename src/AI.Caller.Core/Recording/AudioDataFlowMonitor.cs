using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording {
    /// <summary>
    /// 音频数据流实时监控组件
    /// </summary>
    public class AudioDataFlowMonitor : IDisposable {
        private readonly ILogger _logger;
        private readonly Timer _monitorTimer;
        private readonly object _lockObject = new object();

        private bool _disposed = false;
        private RecordingHealthStatus _healthStatus;
        private DateTime _lastHealthCheck = DateTime.UtcNow;
        private DateTime _monitoringStartTime = DateTime.UtcNow;

        // 统计数据
        private long _totalBytesReceived = 0;
        private long _totalFramesReceived = 0;
        private long _lastSequenceNumber = 0;
        private bool _sequenceNumberInitialized = false;
        private Queue<DateTime> _recentDataTimes = new Queue<DateTime>();
        private Queue<long> _recentByteCounts = new Queue<long>();

        // 配置参数
        private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(1);
        private readonly TimeSpan _dataFlowTimeout = TimeSpan.FromSeconds(5);
        private readonly int _recentDataWindowSize = 10;

        public event EventHandler<RecordingHealthStatus>? HealthStatusChanged;
        public event EventHandler<DataFlowInterruptionEventArgs>? DataFlowInterrupted;
        public event EventHandler<QualityAssessmentEventArgs>? QualityAssessed;

        public RecordingHealthStatus CurrentHealthStatus {
            get {
                lock (_lockObject) {
                    return _healthStatus.Clone();
                }
            }
        }

        public bool IsMonitoring { get; private set; }

        public AudioDataFlowMonitor(ILogger<AudioDataFlowMonitor> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _healthStatus = new RecordingHealthStatus {
                Quality = RecordingQuality.Unknown,
                LastDataReceived = DateTime.UtcNow
            };

            // 创建监控定时器
            _monitorTimer = new Timer(PerformHealthCheck, null, Timeout.Infinite, Timeout.Infinite);

            _logger.LogInformation("AudioDataFlowMonitor initialized");
        }

        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartMonitoring() {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioDataFlowMonitor));

            lock (_lockObject) {
                if (IsMonitoring)
                    return;

                IsMonitoring = true;
                _monitoringStartTime = DateTime.UtcNow;
                _healthStatus.RecordingStartTime = _monitoringStartTime;

                // 启动定时器
                _monitorTimer.Change(_monitorInterval, _monitorInterval);

                _logger.LogInformation("AudioDataFlowMonitor started");
            }
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void StopMonitoring() {
            lock (_lockObject) {
                if (!IsMonitoring)
                    return;

                IsMonitoring = false;

                // 停止定时器
                _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);

                _logger.LogInformation("AudioDataFlowMonitor stopped");
            }
        }

        /// <summary>
        /// 记录音频数据接收
        /// </summary>
        /// <param name="audioData">音频数据</param>
        /// <param name="source">音频源</param>
        /// <param name="sequenceNumber">序列号（可选）</param>
        public void RecordAudioData(byte[] audioData, AudioSource source, long? sequenceNumber = null) {
            if (_disposed || !IsMonitoring || audioData == null)
                return;

            try {
                lock (_lockObject) {
                    var now = DateTime.UtcNow;

                    // 更新基本统计
                    _totalBytesReceived += audioData.Length;
                    _totalFramesReceived++;
                    _healthStatus.LastDataReceived = now;
                    _healthStatus.IsDataFlowing = true;
                    _healthStatus.BytesWritten = _totalBytesReceived;
                    _healthStatus.AudioFrameCount = _totalFramesReceived;

                    // 检查序列号丢失
                    if (sequenceNumber.HasValue) {
                        if (_sequenceNumberInitialized) {
                            var expectedSequence = _lastSequenceNumber + 1;
                            if (sequenceNumber.Value != expectedSequence) {
                                var lostFrames = sequenceNumber.Value - expectedSequence;
                                if (lostFrames > 0 && lostFrames < 1000) // 防止序列号重置导致的异常值
                                {
                                    _healthStatus.LostFrameCount += lostFrames;
                                    _logger.LogWarning($"Detected {lostFrames} lost audio frames. Expected: {expectedSequence}, Got: {sequenceNumber.Value}");
                                }
                            }
                        } else {
                            _sequenceNumberInitialized = true;
                        }

                        _lastSequenceNumber = sequenceNumber.Value;
                    }

                    // 维护最近数据窗口
                    _recentDataTimes.Enqueue(now);
                    _recentByteCounts.Enqueue(audioData.Length);

                    while (_recentDataTimes.Count > _recentDataWindowSize) {
                        _recentDataTimes.Dequeue();
                        _recentByteCounts.Dequeue();
                    }

                    // 计算平均数据率
                    if (_recentDataTimes.Count > 1) {
                        var timeSpan = _recentDataTimes.Last() - _recentDataTimes.First();
                        var totalBytes = _recentByteCounts.Sum();

                        if (timeSpan.TotalSeconds > 0) {
                            _healthStatus.AverageDataRate = totalBytes / timeSpan.TotalSeconds;
                        }
                    }

                    // 清除数据流相关的问题
                    _healthStatus.Issues.RemoveAll(issue =>
                        issue.Contains("No audio data") ||
                        issue.Contains("Data flow interrupted"));
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error recording audio data: {ex.Message}");
            }
        }

        /// <summary>
        /// 记录缓冲区状态
        /// </summary>
        /// <param name="currentSize">当前大小</param>
        /// <param name="maxSize">最大大小</param>
        /// <param name="overflowOccurred">是否发生溢出</param>
        public void RecordBufferStatus(int currentSize, int maxSize, bool overflowOccurred = false) {
            if (_disposed || !IsMonitoring)
                return;

            lock (_lockObject) {
                _healthStatus.BufferUsage.CurrentSize = currentSize;
                _healthStatus.BufferUsage.MaxSize = maxSize;

                if (overflowOccurred) {
                    _healthStatus.BufferUsage.OverflowCount++;
                    _logger.LogWarning($"Buffer overflow detected. Current: {currentSize}, Max: {maxSize}");
                }
            }
        }

        /// <summary>
        /// 记录编码器状态
        /// </summary>
        /// <param name="isWorking">是否正常工作</param>
        /// <param name="encodeTime">编码时间（毫秒）</param>
        /// <param name="encoderType">编码器类型</param>
        public void RecordEncoderStatus(bool isWorking, double? encodeTime = null, string? encoderType = null) {
            if (_disposed || !IsMonitoring)
                return;

            lock (_lockObject) {
                _healthStatus.EncoderHealth.IsWorking = isWorking;
                _healthStatus.EncoderHealth.LastEncodeTime = DateTime.UtcNow;

                if (!isWorking) {
                    _healthStatus.EncoderHealth.FailureCount++;
                }

                if (encodeTime.HasValue) {
                    // 计算移动平均编码时间
                    if (_healthStatus.EncoderHealth.AverageEncodeTime == 0) {
                        _healthStatus.EncoderHealth.AverageEncodeTime = encodeTime.Value;
                    } else {
                        _healthStatus.EncoderHealth.AverageEncodeTime =
                            (_healthStatus.EncoderHealth.AverageEncodeTime * 0.9) + (encodeTime.Value * 0.1);
                    }
                }

                if (!string.IsNullOrEmpty(encoderType)) {
                    _healthStatus.EncoderHealth.EncoderType = encoderType;
                }
            }
        }

        /// <summary>
        /// 记录文件系统状态
        /// </summary>
        /// <param name="outputPath">输出路径</param>
        /// <param name="writeSuccess">写入是否成功</param>
        public void RecordFileSystemStatus(string outputPath, bool writeSuccess = true) {
            if (_disposed || !IsMonitoring)
                return;

            try {
                lock (_lockObject) {
                    _healthStatus.FileSystemHealth.OutputPath = outputPath;
                    _healthStatus.FileSystemHealth.LastWriteTime = DateTime.UtcNow;
                    _healthStatus.FileSystemHealth.IsWritable = writeSuccess;

                    if (!writeSuccess) {
                        _healthStatus.FileSystemHealth.WriteFailureCount++;
                    }

                    // 检查磁盘空间
                    if (!string.IsNullOrEmpty(outputPath)) {
                        try {
                            var directory = Path.GetDirectoryName(outputPath) ?? outputPath;
                            var driveInfo = new DriveInfo(Path.GetPathRoot(directory) ?? directory);

                            _healthStatus.FileSystemHealth.AvailableDiskSpace = driveInfo.AvailableFreeSpace;
                            _healthStatus.FileSystemHealth.TotalDiskSpace = driveInfo.TotalSize;
                        } catch (Exception ex) {
                            _logger.LogWarning($"Could not get disk space info for {outputPath}: {ex.Message}");
                        }
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error recording file system status: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        private void PerformHealthCheck(object? state) {
            if (_disposed || !IsMonitoring)
                return;

            try {
                RecordingHealthStatus previousStatus;
                RecordingHealthStatus currentStatus;

                lock (_lockObject) {
                    previousStatus = _healthStatus.Clone();

                    var now = DateTime.UtcNow;
                    var timeSinceLastData = now - _healthStatus.LastDataReceived;

                    // 检查数据流中断
                    if (timeSinceLastData > _dataFlowTimeout) {
                        _healthStatus.IsDataFlowing = false;
                        var issue = $"No audio data received for {timeSinceLastData.TotalSeconds:F1} seconds";
                        if (!_healthStatus.Issues.Contains(issue)) {
                            _healthStatus.Issues.Add(issue);

                            // 触发数据流中断事件
                            DataFlowInterrupted?.Invoke(this, new DataFlowInterruptionEventArgs {
                                InterruptionDuration = timeSinceLastData,
                                LastDataTime = _healthStatus.LastDataReceived,
                                Timestamp = now
                            });
                        }
                    }

                    // 评估录音质量
                    AssessRecordingQuality();

                    // 检查缓冲区健康
                    CheckBufferHealth();

                    // 检查编码器健康
                    CheckEncoderHealth();

                    // 检查文件系统健康
                    CheckFileSystemHealth();

                    currentStatus = _healthStatus.Clone();
                    _lastHealthCheck = now;
                }

                // 如果健康状态发生变化，触发事件
                if (!AreHealthStatusesEqual(previousStatus, currentStatus)) {
                    HealthStatusChanged?.Invoke(this, currentStatus);
                }

                // 定期触发质量评估事件
                if (_lastHealthCheck.Second % 10 == 0) // 每10秒
                {
                    QualityAssessed?.Invoke(this, new QualityAssessmentEventArgs {
                        Quality = currentStatus.Quality,
                        FrameLossRate = currentStatus.FrameLossRate,
                        AverageDataRate = currentStatus.AverageDataRate,
                        Issues = new List<string>(currentStatus.Issues),
                        Timestamp = DateTime.UtcNow
                    });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error during health check: {ex.Message}");
            }
        }

        /// <summary>
        /// 评估录音质量
        /// </summary>
        private void AssessRecordingQuality() {
            var quality = RecordingQuality.Unknown;

            if (!_healthStatus.IsDataFlowing) {
                quality = RecordingQuality.Poor;
            } else if (_healthStatus.Issues.Count > 3 || _healthStatus.FrameLossRate > 0.1) {
                quality = RecordingQuality.Poor;
            } else if (_healthStatus.Issues.Count > 1 || _healthStatus.FrameLossRate > 0.05) {
                quality = RecordingQuality.Fair;
            } else if (_healthStatus.Issues.Count > 0 || _healthStatus.FrameLossRate > 0.01) {
                quality = RecordingQuality.Good;
            } else if (_healthStatus.IsDataFlowing && _healthStatus.Issues.Count == 0) {
                quality = RecordingQuality.Excellent;
            }

            _healthStatus.Quality = quality;
        }

        /// <summary>
        /// 检查缓冲区健康
        /// </summary>
        private void CheckBufferHealth() {
            if (_healthStatus.BufferUsage.IsNearFull) {
                var issue = $"Buffer usage high: {_healthStatus.BufferUsage.UsagePercentage:F1}%";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }

            if (_healthStatus.BufferUsage.OverflowCount > 0) {
                var issue = $"Buffer overflows detected: {_healthStatus.BufferUsage.OverflowCount}";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }
        }

        /// <summary>
        /// 检查编码器健康
        /// </summary>
        private void CheckEncoderHealth() {
            if (!_healthStatus.EncoderHealth.IsWorking) {
                var issue = "Encoder not working";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }

            if (_healthStatus.EncoderHealth.FailureCount > 0) {
                var issue = $"Encoder failures: {_healthStatus.EncoderHealth.FailureCount}";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }

            if (_healthStatus.EncoderHealth.AverageEncodeTime > 100) // 超过100ms认为编码慢
            {
                var issue = $"Slow encoding: {_healthStatus.EncoderHealth.AverageEncodeTime:F1}ms avg";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }
        }

        /// <summary>
        /// 检查文件系统健康
        /// </summary>
        private void CheckFileSystemHealth() {
            if (_healthStatus.FileSystemHealth.IsLowOnSpace) {
                var issue = $"Low disk space: {_healthStatus.FileSystemHealth.AvailableDiskSpace / (1024 * 1024)}MB available";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }

            if (!_healthStatus.FileSystemHealth.IsWritable) {
                var issue = "File system not writable";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }

            if (_healthStatus.FileSystemHealth.WriteFailureCount > 0) {
                var issue = $"File write failures: {_healthStatus.FileSystemHealth.WriteFailureCount}";
                if (!_healthStatus.Issues.Contains(issue)) {
                    _healthStatus.Issues.Add(issue);
                }
            }
        }

        /// <summary>
        /// 比较两个健康状态是否相等
        /// </summary>
        private bool AreHealthStatusesEqual(RecordingHealthStatus status1, RecordingHealthStatus status2) {
            return status1.IsHealthy == status2.IsHealthy &&
                   status1.Quality == status2.Quality &&
                   status1.Issues.Count == status2.Issues.Count &&
                   status1.Issues.All(issue => status2.Issues.Contains(issue));
        }

        public void Dispose() {
            if (_disposed)
                return;

            _disposed = true;

            try {
                StopMonitoring();
                _monitorTimer?.Dispose();

                _logger.LogInformation("AudioDataFlowMonitor disposed");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error disposing AudioDataFlowMonitor");
            }
        }
    }

    /// <summary>
    /// 数据流中断事件参数
    /// </summary>
    public class DataFlowInterruptionEventArgs : EventArgs {
        public TimeSpan InterruptionDuration { get; set; }
        public DateTime LastDataTime { get; set; }
        public DateTime Timestamp { get; set; }

        public override string ToString() {
            return $"Data flow interrupted for {InterruptionDuration.TotalSeconds:F1}s, last data at {LastDataTime:HH:mm:ss}";
        }
    }

    /// <summary>
    /// 质量评估事件参数
    /// </summary>
    public class QualityAssessmentEventArgs : EventArgs {
        public RecordingQuality Quality { get; set; }
        public double FrameLossRate { get; set; }
        public double AverageDataRate { get; set; }
        public List<string> Issues { get; set; } = new();
        public DateTime Timestamp { get; set; }

        public override string ToString() {
            return $"Quality: {Quality}, FrameLoss: {FrameLossRate:P2}, DataRate: {AverageDataRate:F1} B/s, Issues: {Issues.Count}";
        }
    }
}