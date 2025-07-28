using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core.Recording {
    /// <summary>
    /// 音频数据流管理实现，确保音频数据正确流向文件写入器
    /// </summary>
    public class AudioDataFlow : IAudioDataFlow, IDisposable {
        private readonly ILogger _logger;
        private readonly IStreamingAudioEncoder _audioEncoder;
        private readonly object _lockObject = new object();
        private readonly Timer _healthCheckTimer;
        private readonly Timer _statsUpdateTimer;

        private bool _disposed = false;
        private bool _isInitialized = false;
        private AudioFormat? _inputFormat;
        private string? _outputPath;
        private AudioDataFlowStats _stats;
        private DateTime _initializationTime;
        private readonly ConcurrentQueue<byte[]> _writeBuffer;
        private volatile bool _isWriting = false;

        public bool IsInitialized => _isInitialized;
        public string? OutputPath => _outputPath;

        public event EventHandler<DataFlowHealthEventArgs>? HealthStatusChanged;
        public event EventHandler<DataWrittenEventArgs>? DataWritten;
        public event EventHandler<DataFlowErrorEventArgs>? ErrorOccurred;

        public AudioDataFlow(IStreamingAudioEncoder audioEncoder, ILogger<AudioDataFlow> logger) {
            _audioEncoder = audioEncoder ?? throw new ArgumentNullException(nameof(audioEncoder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _stats = new AudioDataFlowStats {
                IsHealthy = true
            };

            _writeBuffer = new ConcurrentQueue<byte[]>();

            // 创建健康检查定时器（每3秒检查一次）
            _healthCheckTimer = new Timer(PerformHealthCheck, null, Timeout.Infinite, Timeout.Infinite);

            // 创建统计更新定时器（每秒更新一次）
            _statsUpdateTimer = new Timer(UpdateStats, null, Timeout.Infinite, Timeout.Infinite);

            _logger.LogInformation("AudioDataFlow initialized");
        }

        public async Task<bool> InitializeAsync(AudioFormat inputFormat, string outputPath) {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioDataFlow));

            if (_isInitialized) {
                _logger.LogWarning("AudioDataFlow is already initialized");
                return true;
            }

            try {
                _inputFormat = inputFormat ?? throw new ArgumentNullException(nameof(inputFormat));
                _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));

                // 初始化音频编码器
                var success = await _audioEncoder.InitializeAsync(inputFormat, outputPath);
                if (!success) {
                    var error = "Failed to initialize audio encoder";
                    _logger.LogError(error);
                    ErrorOccurred?.Invoke(this, new DataFlowErrorEventArgs(
                        DataFlowErrorType.InitializationFailed, error));
                    return false;
                }

                _isInitialized = true;
                _initializationTime = DateTime.UtcNow;

                // 启动定时器
                _healthCheckTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
                _statsUpdateTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

                _logger.LogInformation($"AudioDataFlow initialized successfully: {outputPath}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error initializing AudioDataFlow: {ex.Message}");
                ErrorOccurred?.Invoke(this, new DataFlowErrorEventArgs(
                    DataFlowErrorType.InitializationFailed, ex.Message, ex));
                return false;
            }
        }

        public async Task<bool> WriteAudioDataAsync(byte[] audioData, AudioSource source) {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioDataFlow));

            if (!_isInitialized) {
                _logger.LogWarning("AudioDataFlow not initialized, cannot write data");
                return false;
            }

            if (audioData == null || audioData.Length == 0) {
                _logger.LogTrace("Empty audio data, skipping write");
                return true;
            }

            try {
                _isWriting = true;

                // 创建音频帧
                var audioFrame = new AudioFrame(audioData, _inputFormat!, source);

                // 写入音频数据
                var success = await _audioEncoder.WriteAudioFrameAsync(audioFrame);

                // 更新统计信息
                UpdateWriteStats(source, audioData.Length, success);

                // 触发事件
                DataWritten?.Invoke(this, new DataWrittenEventArgs(source, audioData.Length, success));

                if (success) {
                    _logger.LogTrace($"Successfully wrote {audioData.Length} bytes from {source}");
                } else {
                    _logger.LogWarning($"Failed to write {audioData.Length} bytes from {source}");
                }

                return success;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error writing audio data from {source}: {ex.Message}");

                UpdateWriteStats(source, audioData.Length, false);

                DataWritten?.Invoke(this, new DataWrittenEventArgs(source, audioData.Length, false, ex.Message));
                ErrorOccurred?.Invoke(this, new DataFlowErrorEventArgs(
                    DataFlowErrorType.WriteError, ex.Message, ex));

                return false;
            } finally {
                _isWriting = false;
            }
        }

        public async Task<bool> FlushAsync() {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioDataFlow));

            if (!_isInitialized)
                return false;

            try {
                var success = await _audioEncoder.FlushAsync();

                if (success) {
                    _logger.LogDebug("AudioDataFlow flushed successfully");
                } else {
                    _logger.LogWarning("AudioDataFlow flush failed");
                }

                return success;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error flushing AudioDataFlow: {ex.Message}");
                ErrorOccurred?.Invoke(this, new DataFlowErrorEventArgs(
                    DataFlowErrorType.FlushError, ex.Message, ex));
                return false;
            }
        }

        public async Task<bool> FinalizeAsync() {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioDataFlow));

            if (!_isInitialized)
                return false;

            try {
                // 停止定时器
                _healthCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _statsUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);

                // 完成音频编码器
                var success = await _audioEncoder.FinalizeAsync();

                _isInitialized = false;

                if (success) {
                    _logger.LogInformation($"AudioDataFlow finalized successfully: {_outputPath}");
                } else {
                    _logger.LogWarning($"AudioDataFlow finalization failed: {_outputPath}");
                }

                return success;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error finalizing AudioDataFlow: {ex.Message}");
                ErrorOccurred?.Invoke(this, new DataFlowErrorEventArgs(
                    DataFlowErrorType.Unknown, ex.Message, ex));
                return false;
            }
        }

        public long GetBytesWritten() {
            lock (_lockObject) {
                return _stats.TotalBytesWritten;
            }
        }

        public bool IsHealthy() {
            lock (_lockObject) {
                return _stats.IsHealthy;
            }
        }

        public AudioDataFlowStats GetStats() {
            lock (_lockObject) {
                return new AudioDataFlowStats {
                    TotalWrites = _stats.TotalWrites,
                    TotalBytesWritten = _stats.TotalBytesWritten,
                    WritesBySource = new Dictionary<AudioSource, long>(_stats.WritesBySource),
                    BytesBySource = new Dictionary<AudioSource, long>(_stats.BytesBySource),
                    LastWriteTime = _stats.LastWriteTime,
                    FailedWrites = _stats.FailedWrites,
                    AverageWriteSpeed = _stats.AverageWriteSpeed,
                    IsHealthy = _stats.IsHealthy,
                    Issues = new List<string>(_stats.Issues)
                };
            }
        }

        public void ResetStats() {
            lock (_lockObject) {
                _stats = new AudioDataFlowStats {
                    IsHealthy = true
                };
                _logger.LogInformation("AudioDataFlow statistics reset");
            }
        }

        private void UpdateWriteStats(AudioSource source, int dataLength, bool success) {
            lock (_lockObject) {
                _stats.TotalWrites++;
                _stats.LastWriteTime = DateTime.UtcNow;

                if (success) {
                    _stats.TotalBytesWritten += dataLength;

                    if (!_stats.WritesBySource.ContainsKey(source)) {
                        _stats.WritesBySource[source] = 0;
                        _stats.BytesBySource[source] = 0;
                    }

                    _stats.WritesBySource[source]++;
                    _stats.BytesBySource[source] += dataLength;
                } else {
                    _stats.FailedWrites++;
                }

                // 清除成功写入相关的问题
                if (success) {
                    _stats.Issues.RemoveAll(issue =>
                        issue.Contains("write failed") || issue.Contains("no data"));
                }
            }
        }

        private void UpdateStats(object? state) {
            if (_disposed || !_isInitialized)
                return;

            try {
                lock (_lockObject) {
                    // 计算平均写入速度
                    var elapsed = DateTime.UtcNow - _initializationTime;
                    if (elapsed.TotalSeconds > 0) {
                        _stats.AverageWriteSpeed = _stats.TotalBytesWritten / elapsed.TotalSeconds;
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error updating AudioDataFlow stats: {ex.Message}");
            }
        }

        private void PerformHealthCheck(object? state) {
            if (_disposed || !_isInitialized)
                return;

            try {
                bool wasHealthy;
                bool isHealthy;
                List<string> issues;

                lock (_lockObject) {
                    wasHealthy = _stats.IsHealthy;
                    issues = new List<string>();

                    var now = DateTime.UtcNow;
                    var timeSinceLastWrite = now - _stats.LastWriteTime;

                    // 检查是否长时间没有写入数据
                    if (_stats.TotalWrites > 0 && timeSinceLastWrite > TimeSpan.FromSeconds(10)) {
                        issues.Add($"No data written for {timeSinceLastWrite.TotalSeconds:F1} seconds");
                    }

                    // 检查写入失败率
                    if (_stats.TotalWrites > 0) {
                        var failureRate = (double)_stats.FailedWrites / _stats.TotalWrites;
                        if (failureRate > 0.1) // 超过10%失败率
                        {
                            issues.Add($"High write failure rate: {failureRate:P1}");
                        }
                    }

                    // 检查写入速度
                    if (_stats.AverageWriteSpeed < 1000 && _stats.TotalBytesWritten > 10000) // 低于1KB/s且已写入超过10KB
                    {
                        issues.Add($"Low write speed: {_stats.AverageWriteSpeed:F1} B/s");
                    }

                    // 检查是否正在写入但长时间卡住
                    if (_isWriting && timeSinceLastWrite > TimeSpan.FromSeconds(30)) {
                        issues.Add("Write operation appears to be stuck");
                    }

                    isHealthy = issues.Count == 0;
                    _stats.IsHealthy = isHealthy;
                    _stats.Issues = issues;
                }

                // 如果健康状态发生变化，触发事件
                if (wasHealthy != isHealthy) {
                    _logger.LogInformation($"AudioDataFlow health status changed: {isHealthy}");
                    HealthStatusChanged?.Invoke(this, new DataFlowHealthEventArgs(isHealthy, issues));
                }

                // 记录问题
                if (issues.Count > 0) {
                    _logger.LogWarning($"AudioDataFlow health issues: {string.Join(", ", issues)}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error during AudioDataFlow health check: {ex.Message}");
            }
        }

        public void Dispose() {
            if (_disposed)
                return;

            _disposed = true;

            try {
                // 停止定时器
                _healthCheckTimer?.Dispose();
                _statsUpdateTimer?.Dispose();

                // 如果还在初始化状态，尝试完成
                if (_isInitialized) {
                    _ = Task.Run(async () => await FinalizeAsync());
                }

                _logger.LogInformation("AudioDataFlow disposed");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error disposing AudioDataFlow");
            }
        }
    }
}