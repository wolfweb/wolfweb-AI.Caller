using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音核心实现，专门处理录音逻辑的独立组件
    /// </summary>
    public class RecordingCore : IRecordingCore, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IAudioRecordingManager _recordingManager;
        private readonly object _lockObject = new object();
        private readonly Timer _healthCheckTimer;
        
        private bool _disposed = false;
        private RecordingHealthStatus _healthStatus;
        private DateTime _lastDataReceived = DateTime.MinValue;
        private long _totalBytesProcessed = 0;
        
        public bool IsRecording => _recordingManager.IsRecording;
        public TimeSpan RecordingDuration => _recordingManager.RecordingDuration;
        
        public event EventHandler<RecordingStatusEventArgs>? StatusChanged;
        public event EventHandler<RecordingProgressEventArgs>? ProgressUpdated;
        public event EventHandler<RecordingErrorEventArgs>? ErrorOccurred;
        
        public RecordingCore(IAudioRecordingManager recordingManager, ILogger<RecordingCore> logger)
        {
            _recordingManager = recordingManager ?? throw new ArgumentNullException(nameof(recordingManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _healthStatus = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Unknown
            };
            
            // 订阅录音管理器事件
            _recordingManager.StatusChanged += OnRecordingManagerStatusChanged;
            _recordingManager.ProgressUpdated += OnRecordingManagerProgressUpdated;
            _recordingManager.ErrorOccurred += OnRecordingManagerErrorOccurred;
            
            // 创建健康检查定时器
            _healthCheckTimer = new Timer(PerformHealthCheck, null, 
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            
            _logger.LogInformation("RecordingCore initialized");
        }
        
        public async Task<bool> StartRecordingAsync(RecordingOptions options)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingCore));
                
            try
            {
                _logger.LogInformation("Starting recording with RecordingCore");
                
                // 重置健康状态
                lock (_lockObject)
                {
                    _healthStatus = new RecordingHealthStatus
                    {
                        Quality = RecordingQuality.Unknown,
                        LastDataReceived = DateTime.UtcNow
                    };
                    _totalBytesProcessed = 0;
                    _lastDataReceived = DateTime.UtcNow;
                }
                
                var result = await _recordingManager.StartRecordingAsync(options);
                
                if (result)
                {
                    _logger.LogInformation("Recording started successfully via RecordingCore");
                }
                else
                {
                    _logger.LogError("Failed to start recording via RecordingCore");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting recording: {ex.Message}");
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(
                    RecordingErrorCode.InitializationFailed, ex.Message, ex));
                return false;
            }
        }
        
        public async Task<string?> StopRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingCore));
                
            try
            {
                _logger.LogInformation("Stopping recording via RecordingCore");
                
                var result = await _recordingManager.StopRecordingAsync();
                
                if (result != null)
                {
                    _logger.LogInformation($"Recording stopped successfully: {result}");
                }
                else
                {
                    _logger.LogWarning("Recording stop returned null");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error stopping recording: {ex.Message}");
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(
                    RecordingErrorCode.Unknown, ex.Message, ex));
                return null;
            }
        }
        
        public async Task<bool> PauseRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingCore));
                
            try
            {
                _logger.LogInformation("Pausing recording via RecordingCore");
                return await _recordingManager.PauseRecordingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error pausing recording: {ex.Message}");
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(
                    RecordingErrorCode.Unknown, ex.Message, ex));
                return false;
            }
        }
        
        public async Task<bool> ResumeRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingCore));
                
            try
            {
                _logger.LogInformation("Resuming recording via RecordingCore");
                return await _recordingManager.ResumeRecordingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resuming recording: {ex.Message}");
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(
                    RecordingErrorCode.Unknown, ex.Message, ex));
                return false;
            }
        }
        
        public async Task<bool> CancelRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingCore));
                
            try
            {
                _logger.LogInformation("Cancelling recording via RecordingCore");
                return await _recordingManager.CancelRecordingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling recording: {ex.Message}");
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(
                    RecordingErrorCode.Unknown, ex.Message, ex));
                return false;
            }
        }
        
        public void ProcessAudioData(AudioSource source, byte[] data, AudioFormat format)
        {
            if (_disposed || data == null || data.Length == 0)
                return;
                
            try
            {
                // 更新健康状态
                lock (_lockObject)
                {
                    _lastDataReceived = DateTime.UtcNow;
                    _healthStatus.LastDataReceived = _lastDataReceived;
                    _healthStatus.IsDataFlowing = true;
                    _totalBytesProcessed += data.Length;
                    _healthStatus.BytesWritten = _totalBytesProcessed;
                    
                    // 清除数据流相关的问题
                    _healthStatus.Issues.RemoveAll(issue => 
                        issue.Contains("No audio data") || issue.Contains("Data flow"));
                }
                
                // 创建音频帧并传递给录音管理器
                var audioFrame = new AudioFrame(data, format, source);
                _recordingManager.ProcessAudioFrame(audioFrame);
                
                _logger.LogTrace($"Processed audio data: {data.Length} bytes from {source}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing audio data from {source}: {ex.Message}");
                
                lock (_lockObject)
                {
                    var issue = $"Audio processing error: {ex.Message}";
                    if (!_healthStatus.Issues.Contains(issue))
                    {
                        _healthStatus.Issues.Add(issue);
                    }
                }
                
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(
                    RecordingErrorCode.EncodingFailed, ex.Message, ex));
            }
        }
        
        public RecordingStatus GetStatus()
        {
            return _recordingManager.CurrentStatus;
        }
        
        public RecordingHealthStatus GetHealthStatus()
        {
            lock (_lockObject)
            {
                return new RecordingHealthStatus
                {
                    IsDataFlowing = _healthStatus.IsDataFlowing,
                    BytesWritten = _healthStatus.BytesWritten,
                    LastDataReceived = _healthStatus.LastDataReceived,
                    Issues = new List<string>(_healthStatus.Issues),
                    Quality = _healthStatus.Quality
                };
            }
        }
        
        private void OnRecordingManagerStatusChanged(object? sender, RecordingStatusEventArgs e)
        {
            _logger.LogDebug($"Recording status changed: {e.Status.State}");
            StatusChanged?.Invoke(this, e);
        }
        
        private void OnRecordingManagerProgressUpdated(object? sender, RecordingProgressEventArgs e)
        {
            _logger.LogTrace($"Recording progress: {e.Duration}, {e.BytesRecorded} bytes");
            ProgressUpdated?.Invoke(this, e);
        }
        
        private void OnRecordingManagerErrorOccurred(object? sender, RecordingErrorEventArgs e)
        {
            _logger.LogError($"Recording manager error: {e.ErrorCode} - {e.ErrorMessage}");
            
            lock (_lockObject)
            {
                var issue = $"Recording error: {e.ErrorMessage}";
                if (!_healthStatus.Issues.Contains(issue))
                {
                    _healthStatus.Issues.Add(issue);
                }
            }
            
            ErrorOccurred?.Invoke(this, e);
        }
        
        private void PerformHealthCheck(object? state)
        {
            if (_disposed)
                return;
                
            try
            {
                lock (_lockObject)
                {
                    var now = DateTime.UtcNow;
                    var timeSinceLastData = now - _lastDataReceived;
                    
                    // 检查数据流是否中断
                    if (IsRecording && timeSinceLastData > TimeSpan.FromSeconds(5))
                    {
                        _healthStatus.IsDataFlowing = false;
                        var issue = $"No audio data received for {timeSinceLastData.TotalSeconds:F1} seconds";
                        if (!_healthStatus.Issues.Contains(issue))
                        {
                            _healthStatus.Issues.Add(issue);
                            _logger.LogWarning($"RecordingCore health check: {issue}");
                        }
                    }
                    
                    // 评估录音质量
                    if (IsRecording)
                    {
                        if (_healthStatus.IsDataFlowing && _healthStatus.Issues.Count == 0)
                        {
                            _healthStatus.Quality = RecordingQuality.Good;
                        }
                        else if (_healthStatus.IsDataFlowing)
                        {
                            _healthStatus.Quality = RecordingQuality.Fair;
                        }
                        else
                        {
                            _healthStatus.Quality = RecordingQuality.Poor;
                        }
                    }
                    else
                    {
                        _healthStatus.Quality = RecordingQuality.Unknown;
                    }
                    
                    // 清理旧的问题（超过1分钟的）
                    var cutoffTime = now.AddMinutes(-1);
                    if (_lastDataReceived > cutoffTime)
                    {
                        _healthStatus.Issues.RemoveAll(issue => 
                            issue.Contains("No audio data") && _healthStatus.IsDataFlowing);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during RecordingCore health check: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            try
            {
                // 停止健康检查定时器
                _healthCheckTimer?.Dispose();
                
                // 取消订阅事件
                _recordingManager.StatusChanged -= OnRecordingManagerStatusChanged;
                _recordingManager.ProgressUpdated -= OnRecordingManagerProgressUpdated;
                _recordingManager.ErrorOccurred -= OnRecordingManagerErrorOccurred;
                
                _logger.LogInformation("RecordingCore disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RecordingCore");
            }
        }
    }
}