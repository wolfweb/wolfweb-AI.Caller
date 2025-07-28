using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音系统使用示例
    /// </summary>
    public class RecordingExample
    {
        private readonly ILogger<RecordingExample> _logger;

        public RecordingExample(ILogger<RecordingExample> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 基本录音示例
        /// </summary>
        /// <param name="outputDirectory">输出目录</param>
        /// <returns>录音结果</returns>
        public async Task<bool> BasicRecordingExampleAsync(string outputDirectory)
        {
            try
            {
                _logger.LogInformation("Starting basic recording example");

                // 1. 创建录音配置
                var recordingOptions = new RecordingOptions
                {
                    Codec = AudioCodec.PCM_WAV,
                    SampleRate = 8000,
                    Channels = 1,
                    OutputDirectory = outputDirectory,
                    FileNameTemplate = "example_{timestamp}.wav",
                    MaxDuration = TimeSpan.FromMinutes(5)
                };

                // 2. 验证配置
                var validation = recordingOptions.Validate();
                if (!validation.IsValid)
                {
                    _logger.LogError("Invalid recording options: {Errors}", string.Join(", ", validation.Errors));
                    return false;
                }

                // 3. 创建音频数据流监控器
                var monitorLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioDataFlowMonitor>.Instance;
                using var dataFlowMonitor = new AudioDataFlowMonitor(monitorLogger);
                dataFlowMonitor.StartMonitoring();

                // 4. 模拟音频数据处理
                await SimulateAudioProcessingAsync(dataFlowMonitor);

                // 5. 停止监控
                dataFlowMonitor.StopMonitoring();

                // 6. 获取健康状态
                var healthStatus = dataFlowMonitor.CurrentHealthStatus;
                _logger.LogInformation("Recording completed. Health: {IsHealthy}, Frames: {FrameCount}, Bytes: {BytesWritten}",
                    healthStatus.IsHealthy, healthStatus.AudioFrameCount, healthStatus.BytesWritten);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Basic recording example failed: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 文件验证示例
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>验证结果</returns>
        public async Task<bool> FileValidationExampleAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("Starting file validation example for: {FilePath}", filePath);

                // 创建零字节文件检测器
                var detectorLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ZeroByteFileDetector>.Instance;
                var detector = new ZeroByteFileDetector(detectorLogger);

                // 验证文件
                var result = await detector.ValidateRecordingFileAsync(filePath);

                if (result.IsValid)
                {
                    _logger.LogInformation("File validation passed: {FilePath} ({FileSize} bytes)", 
                        filePath, result.FileSize);
                    
                    if (result.AudioFormat != null)
                    {
                        _logger.LogInformation("Audio format: {AudioFormat}", result.AudioFormat);
                    }
                }
                else
                {
                    _logger.LogWarning("File validation failed: {FilePath} - Issues: {Issues}", 
                        filePath, string.Join(", ", result.Issues));
                }

                return result.IsValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File validation example failed: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 系统状态监控示例
        /// </summary>
        /// <returns>监控结果</returns>
        public async Task<bool> SystemMonitoringExampleAsync()
        {
            try
            {
                _logger.LogInformation("Starting system monitoring example");

                // 创建组件
                var monitorLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioDataFlowMonitor>.Instance;
                var statusLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordingStatusService>.Instance;
                using var dataFlowMonitor = new AudioDataFlowMonitor(monitorLogger);
                using var statusService = new RecordingStatusService(statusLogger);

                // 注册监控器
                statusService.RegisterDataFlowMonitor(dataFlowMonitor);

                // 启动监控
                dataFlowMonitor.StartMonitoring();

                // 模拟一些活动
                await SimulateAudioProcessingAsync(dataFlowMonitor);

                // 获取系统状态
                var systemStatus = statusService.GetSystemStatus();
                var healthStatus = statusService.GetHealthStatus();
                var dataFlowStatus = statusService.GetDataFlowStatus();

                _logger.LogInformation("System Status - Healthy: {IsHealthy}, Issues: {IssueCount}",
                    systemStatus.IsHealthy, systemStatus.Issues.Count);

                _logger.LogInformation("Health Status - Data Flowing: {IsDataFlowing}, Quality: {Quality}",
                    healthStatus.IsDataFlowing, healthStatus.Quality);

                _logger.LogInformation("Data Flow Status - Healthy: {IsHealthy}, Current Issues: {IssueCount}",
                    dataFlowStatus.IsHealthy, dataFlowStatus.CurrentIssues.Count);

                dataFlowMonitor.StopMonitoring();

                return systemStatus.IsHealthy && healthStatus.IsHealthy && dataFlowStatus.IsHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "System monitoring example failed: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 音频格式处理示例
        /// </summary>
        /// <returns>处理结果</returns>
        public bool AudioFormatExampleAsync()
        {
            try
            {
                _logger.LogInformation("Starting audio format example");

                // 创建不同的音频格式
                var format1 = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
                var format2 = new AudioFormat(16000, 2, 16, AudioSampleFormat.PCM);
                var format3 = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);

                _logger.LogInformation("Format 1: {Format}", format1);
                _logger.LogInformation("Format 2: {Format}", format2);
                _logger.LogInformation("Format 3: {Format}", format3);

                // 检查兼容性
                var compatible12 = format1.IsCompatibleWith(format2);
                var compatible13 = format1.IsCompatibleWith(format3);

                _logger.LogInformation("Format 1 compatible with Format 2: {Compatible}", compatible12);
                _logger.LogInformation("Format 1 compatible with Format 3: {Compatible}", compatible13);

                // 创建音频帧
                var audioData = new byte[160]; // 20ms @ 8kHz
                var frame = new AudioFrame(audioData, format1, AudioSource.RTP_Incoming);

                _logger.LogInformation("Created audio frame: Source={Source}, Size={Size}, Timestamp={Timestamp}",
                    frame.Source, frame.Data.Length, frame.Timestamp);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio format example failed: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 模拟音频处理
        /// </summary>
        /// <param name="monitor">数据流监控器</param>
        /// <returns>处理任务</returns>
        private async Task SimulateAudioProcessingAsync(AudioDataFlowMonitor monitor)
        {
            var random = new Random();
            var sequenceNumber = 1;

            // 模拟处理50帧音频数据
            for (int i = 0; i < 50; i++)
            {
                // 生成模拟音频数据
                var audioData = new byte[160]; // 20ms @ 8kHz
                random.NextBytes(audioData);

                // 记录音频数据
                monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, sequenceNumber);

                // 记录系统状态
                monitor.RecordBufferStatus(30 + random.Next(-10, 10), 100, false);
                monitor.RecordEncoderStatus(true, 1.5 + random.NextDouble(), "WAV");
                monitor.RecordFileSystemStatus("example.wav", true);

                sequenceNumber++;

                // 模拟20ms间隔
                await Task.Delay(20);
            }

            _logger.LogInformation("Simulated processing of {FrameCount} audio frames", sequenceNumber - 1);
        }
    }
}