using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{    
    public class AudioRecordingManager : IAudioRecordingManager
    {
        private readonly ILogger _logger;
        private readonly AudioRecorder _audioRecorder;
        private readonly AudioMixer _audioMixer;
        private readonly IAudioEncoderFactory _encoderFactory;
        private IStreamingAudioEncoder? _audioEncoder;
        private readonly RecordingFileManager _fileManager;
        private readonly AudioFormatConverter _formatConverter;
        private readonly IAudioQualityMonitor _qualityMonitor;
        private readonly IAudioDiagnostics _diagnostics;
        private readonly IAudioErrorRecoveryManager _errorRecovery;
        private readonly object _lockObject = new object();
        
        private RecordingStatus _currentStatus;
        private RecordingOptions? _currentOptions;
        private string? _currentFilePath;
        private DateTime _recordingStartTime;
        private bool _disposed = false;
        private readonly Timer _progressTimer;
        private long _totalBytesRecorded = 0;
                
        public event EventHandler<RecordingStatusEventArgs>? StatusChanged;
                
        public event EventHandler<RecordingProgressEventArgs>? ProgressUpdated;
                
        public event EventHandler<RecordingErrorEventArgs>? ErrorOccurred;
                
        public RecordingStatus CurrentStatus => _currentStatus.Clone();
                
        public TimeSpan RecordingDuration => _currentStatus.Duration;
                
        public bool IsRecording => _currentStatus.IsRecording;
        
        public AudioRecordingManager(
            AudioRecorder audioRecorder,
            AudioMixer audioMixer,
            IAudioEncoderFactory encoderFactory,
            RecordingFileManager fileManager,
            AudioFormatConverter formatConverter,
            IAudioQualityMonitor qualityMonitor,
            IAudioDiagnostics diagnostics,
            IAudioErrorRecoveryManager errorRecovery,
            ILogger logger)
        {
            _audioRecorder = audioRecorder ?? throw new ArgumentNullException(nameof(audioRecorder));
            _audioMixer = audioMixer ?? throw new ArgumentNullException(nameof(audioMixer));
            _encoderFactory = encoderFactory ?? throw new ArgumentNullException(nameof(encoderFactory));
            _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
            _formatConverter = formatConverter ?? throw new ArgumentNullException(nameof(formatConverter));
            _qualityMonitor = qualityMonitor ?? throw new ArgumentNullException(nameof(qualityMonitor));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _errorRecovery = errorRecovery ?? throw new ArgumentNullException(nameof(errorRecovery));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _currentStatus = new RecordingStatus();
            
            // 创建进度更新定时器
            _progressTimer = new Timer(UpdateProgress, null, Timeout.Infinite, Timeout.Infinite);
            
            // 订阅音频录制器事件
            _audioRecorder.AudioDataReceived += OnAudioDataReceived;
            _audioRecorder.BufferOverflow += OnBufferOverflow;
            
            // 订阅质量监控器事件
            _qualityMonitor.QualityChanged += OnQualityChanged;
            _qualityMonitor.QualityWarning += OnQualityWarning;
            
            _logger.LogInformation("AudioRecordingManager initialized");
        }
                
        public async Task<bool> StartRecordingAsync(RecordingOptions options)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioRecordingManager));
                
            if (options == null)
                throw new ArgumentNullException(nameof(options));
                
            lock (_lockObject)
            {
                if (_currentStatus.IsRecording)
                {
                    _logger.LogWarning("Recording is already in progress");
                    return false;
                }
                
                if (!_currentStatus.CanStart)
                {
                    _logger.LogWarning($"Cannot start recording in current state: {_currentStatus.State}");
                    return false;
                }
            }
            
            try
            {
                // 验证录音选项
                var validation = options.Validate();
                if (!validation.IsValid)
                {
                    var errorMessage = $"Invalid recording options: {string.Join(", ", validation.Errors)}";
                    _logger.LogError(errorMessage);
                    SetErrorState(RecordingErrorCode.ConfigurationError, errorMessage);
                    return false;
                }
                
                _currentOptions = options;
                _recordingStartTime = DateTime.UtcNow;
                _totalBytesRecorded = 0;
                
                // 更新状态为启动中
                UpdateStatus(RecordingState.Starting, "Initializing recording...");
                
                // 生成录音文件路径
                var metadata = CreateRecordingMetadata();
                var fileName = _fileManager.GenerateFileName(metadata);
                _currentFilePath = await _fileManager.CreateRecordingFileAsync(fileName, 
                    new AudioFormat(options.SampleRate, options.Channels, 16, AudioSampleFormat.PCM));
                
                // 创建并初始化音频编码器
                var audioFormat = new AudioFormat(options.SampleRate, options.Channels, 16, AudioSampleFormat.PCM);
                var encodingOptions = new AudioEncodingOptions
                {
                    Codec = options.Codec,
                    SampleRate = options.SampleRate,
                    Channels = options.Channels,
                    BitRate = options.BitRate,
                    Quality = options.Quality
                };
                
                _audioEncoder = _encoderFactory.CreateStreamingEncoder(encodingOptions);
                
                // 订阅编码器事件
                _audioEncoder.AudioWritten += OnAudioWritten;
                _audioEncoder.EncodingProgress += OnEncodingProgress;
                _audioEncoder.EncodingError += OnEncodingError;
                
                if (!await _audioEncoder.InitializeAsync(audioFormat, _currentFilePath))
                {
                    SetErrorState(RecordingErrorCode.InitializationFailed, "Failed to initialize audio encoder");
                    return false;
                }
                
                // 开始音频捕获
                await _audioRecorder.StartCaptureAsync(
                    AudioSource.RTP_Incoming,
                    AudioSource.RTP_Outgoing,
                    AudioSource.WebRTC_Incoming,
                    AudioSource.WebRTC_Outgoing
                );
                
                // 更新状态为录音中
                lock (_lockObject)
                {
                    _currentStatus.UpdateState(RecordingState.Recording, "Recording started");
                    _currentStatus.CurrentFilePath = _currentFilePath;
                    _currentStatus.AudioFormat = audioFormat;
                    _currentStatus.Options = options;
                }
                
                // 启动进度更新定时器
                _progressTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                
                // 启动质量监控
                _qualityMonitor.StartMonitoring();
                
                _logger.LogInformation($"Recording started: {_currentFilePath}");
                _diagnostics.LogAudioEvent(AudioEventType.Info, AudioSource.Mixed, 
                    "Recording started successfully", new { FilePath = _currentFilePath, Options = options });
                
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(_currentStatus.Clone()));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting recording: {ex.Message}");
                _diagnostics.LogAudioError(AudioSource.Mixed, ex, "StartRecording", new { Options = options });
                SetErrorState(RecordingErrorCode.InitializationFailed, ex.Message);
                return false;
            }
        }
                
        public async Task<string?> StopRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioRecordingManager));
                
            lock (_lockObject)
            {
                if (!_currentStatus.CanStop)
                {
                    _logger.LogWarning($"Cannot stop recording in current state: {_currentStatus.State}");
                    return null;
                }
            }
            
            try
            {
                // 更新状态为停止中
                UpdateStatus(RecordingState.Stopping, "Stopping recording...");
                
                // 停止进度更新定时器
                _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
                
                // 停止质量监控
                _qualityMonitor.StopMonitoring();
                
                // 停止音频捕获
                await _audioRecorder.StopCaptureAsync();
                
                // 完成音频编码
                if (_audioEncoder != null)
                {
                    await _audioEncoder.FinalizeAsync();
                }
                
                // 保存元数据
                if (_currentFilePath != null && _currentOptions != null)
                {
                    var metadata = CreateRecordingMetadata();
                    metadata.FileSize = new FileInfo(_currentFilePath).Length;
                    await _fileManager.SaveMetadataAsync(_currentFilePath, metadata);
                }
                
                var filePath = _currentFilePath;
                
                // 更新状态为已完成
                lock (_lockObject)
                {
                    _currentStatus.UpdateState(RecordingState.Completed, "Recording completed");
                    _currentStatus.EndTime = DateTime.UtcNow;
                }
                
                _logger.LogInformation($"Recording completed: {filePath}, Duration: {RecordingDuration}, Bytes: {_totalBytesRecorded}");
                _diagnostics.LogAudioEvent(AudioEventType.Info, AudioSource.Mixed, 
                    "Recording completed successfully", 
                    new { FilePath = filePath, Duration = RecordingDuration, BytesRecorded = _totalBytesRecorded });
                
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(_currentStatus.Clone()));
                
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error stopping recording: {ex.Message}");
                _diagnostics.LogAudioError(AudioSource.Mixed, ex, "StopRecording");
                SetErrorState(RecordingErrorCode.EncodingFailed, ex.Message);
                return null;
            }
        }
                
        public async Task<bool> PauseRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioRecordingManager));
                
            lock (_lockObject)
            {
                if (!_currentStatus.CanPause)
                {
                    _logger.LogWarning($"Cannot pause recording in current state: {_currentStatus.State}");
                    return false;
                }
            }
            
            try
            {
                // 停止音频捕获（但不完成编码）
                await _audioRecorder.StopCaptureAsync();
                
                // 停止进度更新定时器
                _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
                
                // 更新状态为已暂停
                UpdateStatus(RecordingState.Paused, "Recording paused");
                
                _logger.LogInformation("Recording paused");
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(_currentStatus.Clone()));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error pausing recording: {ex.Message}");
                SetErrorState(RecordingErrorCode.Unknown, ex.Message);
                return false;
            }
        }
                
        public async Task<bool> ResumeRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioRecordingManager));
                
            lock (_lockObject)
            {
                if (!_currentStatus.CanResume)
                {
                    _logger.LogWarning($"Cannot resume recording in current state: {_currentStatus.State}");
                    return false;
                }
            }
            
            try
            {
                // 重新开始音频捕获
                await _audioRecorder.StartCaptureAsync(
                    AudioSource.RTP_Incoming,
                    AudioSource.RTP_Outgoing,
                    AudioSource.WebRTC_Incoming,
                    AudioSource.WebRTC_Outgoing
                );
                
                // 更新状态为录音中
                UpdateStatus(RecordingState.Recording, "Recording resumed");
                
                // 重新启动进度更新定时器
                _progressTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                
                _logger.LogInformation("Recording resumed");
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(_currentStatus.Clone()));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resuming recording: {ex.Message}");
                SetErrorState(RecordingErrorCode.Unknown, ex.Message);
                return false;
            }
        }
                
        public async Task<bool> CancelRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioRecordingManager));
                
            try
            {
                // 停止进度更新定时器
                _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
                
                // 停止音频捕获
                await _audioRecorder.StopCaptureAsync();
                
                // 删除录音文件
                if (_currentFilePath != null && File.Exists(_currentFilePath))
                {
                    try
                    {
                        File.Delete(_currentFilePath);
                        _logger.LogInformation($"Deleted cancelled recording file: {_currentFilePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to delete cancelled recording file: {_currentFilePath}");
                    }
                }
                
                // 更新状态为已取消
                UpdateStatus(RecordingState.Cancelled, "Recording cancelled");
                
                _logger.LogInformation("Recording cancelled");
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(_currentStatus.Clone()));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling recording: {ex.Message}");
                SetErrorState(RecordingErrorCode.Unknown, ex.Message);
                return false;
            }
        }
                
        public void ProcessAudioFrame(AudioFrame audioFrame)
        {
            if (_disposed || !IsRecording || audioFrame == null)
                return;
                
            try
            {
                // 模拟AudioDataReceived事件
                var eventArgs = new AudioDataEventArgs(audioFrame);
                OnAudioDataReceived(this, eventArgs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing external audio frame: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取音频质量统计信息
        /// </summary>
        public Dictionary<AudioSource, AudioStreamStats> GetQualityStats()
        {
            return _qualityMonitor.GetAllStreamStats();
        }
        
        /// <summary>
        /// 获取特定音频源的质量统计信息
        /// </summary>
        public AudioStreamStats GetQualityStats(AudioSource source)
        {
            return _qualityMonitor.GetStreamStats(source);
        }
        
        /// <summary>
        /// 重置质量统计信息
        /// </summary>
        public void ResetQualityStats()
        {
            _qualityMonitor.ResetStats();
        }
        
        /// <summary>
        /// 获取诊断报告
        /// </summary>
        public AudioDiagnosticReport GetDiagnosticReport()
        {
            return _diagnostics.GetDiagnosticReport();
        }
        
        /// <summary>
        /// 重置诊断数据
        /// </summary>
        public void ResetDiagnostics()
        {
            _diagnostics.Reset();
        }
        
        /// <summary>
        /// 获取错误恢复统计信息
        /// </summary>
        public RecoveryStatistics GetRecoveryStatistics()
        {
            return _errorRecovery.GetRecoveryStatistics();
        }
        
        /// <summary>
        /// 重置错误恢复统计信息
        /// </summary>
        public void ResetRecoveryStatistics()
        {
            _errorRecovery.ResetStatistics();
        }
                
        private async void OnAudioDataReceived(object? sender, AudioDataEventArgs e)
        {
            if (!IsRecording || _disposed)
                return;
                
            using var perfMeasurement = _diagnostics.StartPerformanceMeasurement("AudioDataProcessing", e.AudioFrame.Source);
            
            try
            {
                // 检查录音时长限制
                if (_currentOptions != null && RecordingDuration >= _currentOptions.MaxDuration)
                {
                    _logger.LogInformation("Maximum recording duration reached, stopping recording");
                    _diagnostics.LogAudioEvent(AudioEventType.Info, e.AudioFrame.Source, 
                        "Maximum recording duration reached", new { Duration = RecordingDuration });
                    _ = Task.Run(async () => await StopRecordingAsync());
                    return;
                }
                
                // 转换音频格式（如果需要）
                var audioFrame = e.AudioFrame;
                if (_currentOptions != null)
                {
                    var targetFormat = new AudioFormat(
                        _currentOptions.SampleRate,
                        _currentOptions.Channels,
                        16,
                        AudioSampleFormat.PCM
                    );
                    
                    if (!audioFrame.Format.IsCompatibleWith(targetFormat))
                    {
                        using var conversionMeasurement = _diagnostics.StartPerformanceMeasurement("AudioFormatConversion", e.AudioFrame.Source);
                        var convertedFrame = _formatConverter.ConvertFormat(audioFrame, targetFormat);
                        if (convertedFrame != null)
                        {
                            audioFrame = convertedFrame;
                            _diagnostics.LogAudioEvent(AudioEventType.Debug, e.AudioFrame.Source, 
                                "Audio format converted", new { OriginalFormat = e.AudioFrame.Format, TargetFormat = targetFormat });
                        }
                    }
                }
                
                // 写入音频帧到文件（实时写入）
                if (_audioEncoder != null)
                {
                    using var encodingMeasurement = _diagnostics.StartPerformanceMeasurement("AudioEncoding", audioFrame.Source);
                    await _audioEncoder.WriteAudioFrameAsync(audioFrame);
                }
                
                // 质量监控
                _qualityMonitor.ProcessAudioFrame(audioFrame, audioFrame.Source);
                
                // 更新统计信息
                lock (_lockObject)
                {
                    _totalBytesRecorded += audioFrame.Data.Length;
                    _currentStatus.BytesRecorded = _totalBytesRecorded;
                }
                
                // 记录处理的音频帧
                _diagnostics.LogPerformanceMetric("AudioFrameSize", audioFrame.Data.Length, "bytes", 
                    new { Source = audioFrame.Source });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing audio data: {ex.Message}");
                _diagnostics.LogAudioError(e.AudioFrame.Source, ex, "AudioDataProcessing", 
                    new { FrameSize = e.AudioFrame.Data.Length });
                ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(RecordingErrorCode.EncodingFailed, ex.Message, ex));
            }
        }
                
        private void OnBufferOverflow(object? sender, BufferOverflowEventArgs e)
        {
            _logger.LogWarning($"Audio buffer overflow: {e.RemovedFrameCount} frames removed, current buffer size: {e.CurrentBufferSize}");
        }
                
        private void OnAudioWritten(object? sender, AudioWrittenEventArgs e)
        {
            if (e.Success)
            {
                _logger.LogTrace($"Audio written: {e.BytesWritten} bytes at {e.WriteTime}");
            }
            else
            {
                _logger.LogError($"Audio write failed: {e.ErrorMessage}");
            }
        }
        
        private void OnEncodingProgress(object? sender, EncodingProgressEventArgs e)
        {
            // 编码进度已在OnAudioDataReceived中处理
        }
                
        private async void OnEncodingError(object? sender, EncodingErrorEventArgs e)
        {
            _logger.LogError($"Encoding error: {e.ErrorMessage}");
            
            // 尝试错误恢复
            try
            {
                var recovered = await _errorRecovery.RecoverFromEncodingError(e);
                if (recovered)
                {
                    _logger.LogInformation($"Successfully recovered from encoding error: {e.ErrorCode}");
                    _diagnostics.LogAudioEvent(AudioEventType.Info, AudioSource.Mixed, 
                        "Encoding error recovery successful", new { ErrorCode = e.ErrorCode });
                }
                else
                {
                    _logger.LogWarning($"Failed to recover from encoding error: {e.ErrorCode}");
                    _diagnostics.LogAudioEvent(AudioEventType.Warning, AudioSource.Mixed, 
                        "Encoding error recovery failed", new { ErrorCode = e.ErrorCode });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during encoding error recovery: {ex.Message}");
            }
            
            ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(e.ErrorCode, e.ErrorMessage, e.Exception));
        }
        
        private void OnQualityChanged(object? sender, StreamQualityEventArgs e)
        {
            _logger.LogTrace($"Audio quality changed: {e.QualityLevel} - {e.StreamStats}");
            
            // 如果质量严重下降，可以考虑调整录音参数
            if (e.QualityLevel == AudioQualityLevel.Critical)
            {
                _logger.LogWarning($"Critical audio quality detected: {e.Message}");
            }
        }
        
        private void OnQualityWarning(object? sender, QualityWarningEventArgs e)
        {
            _logger.LogWarning($"Audio quality warning for {e.Source}: {e.Message}");
            
            // 可以根据警告类型采取相应措施
            if (e.QualityLevel == AudioQualityLevel.Poor || e.QualityLevel == AudioQualityLevel.Critical)
            {
                // 例如：降低音频质量要求、增加缓冲区大小等
                _logger.LogInformation($"Considering quality adjustments due to {e.QualityLevel} quality");
            }
        }
                
        private void UpdateProgress(object? state)
        {
            if (_disposed || !IsRecording)
                return;
                
            try
            {
                lock (_lockObject)
                {
                    _currentStatus.LastUpdated = DateTime.UtcNow;
                }
                
                var progress = new RecordingProgressEventArgs(RecordingDuration, _totalBytesRecorded, 0.0);
                ProgressUpdated?.Invoke(this, progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating progress: {ex.Message}");
            }
        }
                
        private void UpdateStatus(RecordingState state, string? message = null)
        {
            lock (_lockObject)
            {
                _currentStatus.UpdateState(state, message);
            }
        }
                
        private void SetErrorState(RecordingErrorCode errorCode, string errorMessage)
        {
            lock (_lockObject)
            {
                _currentStatus.SetError(errorCode, errorMessage);
            }
            
            ErrorOccurred?.Invoke(this, new RecordingErrorEventArgs(errorCode, errorMessage));
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(_currentStatus.Clone()));
        }
                
        private RecordingMetadata CreateRecordingMetadata()
        {
            return new RecordingMetadata
            {
                StartTime = _recordingStartTime,
                EndTime = DateTime.UtcNow,
                AudioCodec = _currentOptions?.Codec ?? AudioCodec.PCM_WAV,
                SampleRate = _currentOptions?.SampleRate ?? 8000,
                Channels = _currentOptions?.Channels ?? 1,
                Quality = _currentOptions?.Quality ?? AudioQuality.Standard,
                Notes = "Recorded by AudioRecordingManager"
            };
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            try
            {
                // 如果正在录音，先停止
                if (IsRecording)
                {
                    _ = Task.Run(async () => await StopRecordingAsync());
                }
                
                // 停止定时器
                _progressTimer?.Dispose();
                
                // 取消订阅事件
                _audioRecorder.AudioDataReceived -= OnAudioDataReceived;
                _audioRecorder.BufferOverflow -= OnBufferOverflow;
                _qualityMonitor.QualityChanged -= OnQualityChanged;
                _qualityMonitor.QualityWarning -= OnQualityWarning;
                
                if (_audioEncoder != null)
                {
                    _audioEncoder.AudioWritten -= OnAudioWritten;
                    _audioEncoder.EncodingProgress -= OnEncodingProgress;
                    _audioEncoder.EncodingError -= OnEncodingError;
                }
                
                // 释放资源
                _audioRecorder?.Dispose();
                _audioMixer?.Dispose();
                _audioEncoder?.Dispose();
                _fileManager?.Dispose();
                _formatConverter?.Dispose();
                _qualityMonitor?.Dispose();
                
                _logger.LogInformation("AudioRecordingManager disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing AudioRecordingManager");
            }
        }
    }
}