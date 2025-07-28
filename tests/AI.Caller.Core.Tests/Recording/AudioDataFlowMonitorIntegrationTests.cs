using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioDataFlowMonitorIntegrationTests : IDisposable
    {
        private readonly Mock<ILogger<AudioDataFlowMonitor>> _mockLogger;
        private readonly Mock<ILogger<RecordingStatusService>> _mockStatusLogger;
        private readonly AudioDataFlowMonitor _monitor;
        private readonly RecordingStatusService _statusService;
        
        public AudioDataFlowMonitorIntegrationTests()
        {
            _mockLogger = new Mock<ILogger<AudioDataFlowMonitor>>();
            _mockStatusLogger = new Mock<ILogger<RecordingStatusService>>();
            _monitor = new AudioDataFlowMonitor(_mockLogger.Object);
            _statusService = new RecordingStatusService(_mockStatusLogger.Object);
        }
        
        public void Dispose()
        {
            _monitor?.Dispose();
            _statusService?.Dispose();
        }
        
        [Fact]
        public void IntegratedMonitoring_ShouldWorkTogether()
        {
            // Arrange
            _statusService.RegisterDataFlowMonitor(_monitor);
            _monitor.StartMonitoring();
            
            // Act - 模拟音频数据流
            var audioData = new byte[1024];
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 2);
            _monitor.RecordAudioData(audioData, AudioSource.WebRTC_Outgoing, 3);
            
            // 模拟缓冲区状态
            _monitor.RecordBufferStatus(512, 1024, false);
            
            // 模拟编码器状态
            _monitor.RecordEncoderStatus(true, 25.5, "WAV");
            
            // 模拟文件系统状态
            _monitor.RecordFileSystemStatus("C:\\temp\\recording.wav", true);
            
            // 等待健康检查执行
            Thread.Sleep(1500);
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.True(healthStatus.IsHealthy);
            Assert.True(healthStatus.Quality >= RecordingQuality.Good); // 放宽质量要求
            Assert.Equal(3072, healthStatus.BytesWritten); // 3 * 1024
            Assert.Equal(3, healthStatus.AudioFrameCount);
            Assert.Equal(0, healthStatus.LostFrameCount);
            
            var systemStatus = _statusService.GetSystemStatus();
            Assert.NotNull(systemStatus);
            Assert.NotNull(systemStatus.DataFlowStatus);
        }
        
        [Fact]
        public void DataFlowInterruption_ShouldBeDetected()
        {
            // Arrange
            bool interruptionDetected = false;
            _monitor.DataFlowInterrupted += (sender, args) =>
            {
                interruptionDetected = true;
            };
            
            _monitor.StartMonitoring();
            
            // Act - 发送一些数据然后停止
            var audioData = new byte[512];
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            
            // 等待足够长的时间让监控器检测到中断
            Thread.Sleep(6000); // 超过5秒的数据流超时时间
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.False(healthStatus.IsDataFlowing);
            Assert.Contains(healthStatus.Issues, issue => issue.Contains("No audio data"));
        }
        
        [Fact]
        public void QualityAssessment_ShouldReflectDataFlowHealth()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // 设置良好的文件系统状态
            _monitor.RecordFileSystemStatus("C:\\temp\\test.wav", true);
            
            // Act - 模拟良好的数据流
            var audioData = new byte[1024];
            for (int i = 0; i < 10; i++)
            {
                _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, i + 1);
                Thread.Sleep(100); // 每100ms发送一次数据
            }
            
            // 等待健康检查执行
            Thread.Sleep(1500);
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.True(healthStatus.IsHealthy, $"Health status should be healthy. Issues: {string.Join(", ", healthStatus.Issues)}");
            Assert.True(healthStatus.Quality >= RecordingQuality.Good, $"Quality should be Good or better, but was {healthStatus.Quality}");
            Assert.True(healthStatus.AverageDataRate > 0, $"Average data rate should be > 0, but was {healthStatus.AverageDataRate}");
        }
        
        [Fact]
        public void BufferOverflow_ShouldBeReported()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act - 模拟缓冲区溢出
            _monitor.RecordBufferStatus(1024, 1024, true); // 满载且溢出
            
            // 等待监控器处理
            Thread.Sleep(1500);
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.True(healthStatus.BufferUsage.OverflowCount > 0);
            Assert.Contains(healthStatus.Issues, issue => issue.Contains("Buffer overflows"));
        }
        
        [Fact]
        public void EncoderFailure_ShouldAffectQuality()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act - 模拟编码器故障
            _monitor.RecordEncoderStatus(false, null, "WAV");
            
            // 等待监控器处理
            Thread.Sleep(1500);
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.False(healthStatus.EncoderHealth.IsWorking);
            Assert.Contains(healthStatus.Issues, issue => issue.Contains("Encoder not working"));
            Assert.True(healthStatus.Quality <= RecordingQuality.Poor);
        }
        
        [Fact]
        public void FileSystemIssues_ShouldBeDetected()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act - 模拟文件系统问题
            _monitor.RecordFileSystemStatus("C:\\temp\\recording.wav", false); // 写入失败
            
            // 等待监控器处理
            Thread.Sleep(1500);
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.False(healthStatus.FileSystemHealth.IsWritable);
            Assert.Contains(healthStatus.Issues, issue => issue.Contains("File system not writable"));
        }
        
        [Fact]
        public void FrameLoss_ShouldBeCalculated()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act - 模拟丢帧（序列号跳跃）
            var audioData = new byte[512];
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 2);
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 5); // 跳过了3和4
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 6);
            
            // Assert
            var healthStatus = _monitor.CurrentHealthStatus;
            Assert.Equal(2, healthStatus.LostFrameCount); // 丢失了序列号3和4
            Assert.True(healthStatus.FrameLossRate > 0);
            Assert.Equal(4, healthStatus.AudioFrameCount); // 实际接收到4帧
        }
        
        [Fact]
        public void SystemStatusService_ShouldAggregateAllComponents()
        {
            // Arrange
            var mockRecordingCore = new Mock<IRecordingCore>();
            var mockAudioBridge = new Mock<IAudioBridge>();
            
            var recordingHealth = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Good,
                IsDataFlowing = true,
                Issues = new List<string>()
            };
            
            var bridgeStats = new AudioBridgeStats
            {
                IsHealthy = true,
                Issues = new List<string>()
            };
            
            mockRecordingCore.Setup(x => x.GetHealthStatus()).Returns(recordingHealth);
            mockAudioBridge.Setup(x => x.GetStats()).Returns(bridgeStats);
            
            _statusService.RegisterRecordingCore(mockRecordingCore.Object);
            _statusService.RegisterAudioBridge(mockAudioBridge.Object);
            _statusService.RegisterDataFlowMonitor(_monitor);
            
            _monitor.StartMonitoring();
            
            // Act
            var audioData = new byte[1024];
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            
            var systemStatus = _statusService.GetSystemStatus();
            
            // Assert
            Assert.NotNull(systemStatus);
            Assert.NotNull(systemStatus.RecordingStatus);
            Assert.NotNull(systemStatus.DataFlowStatus);
            Assert.NotNull(systemStatus.AudioBridgeStatus);
            Assert.True(systemStatus.Uptime > TimeSpan.Zero);
        }
    }
}