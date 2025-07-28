using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioDataFlowMonitorTests : IDisposable
    {
        private readonly Mock<ILogger<AudioDataFlowMonitor>> _mockLogger;
        private readonly AudioDataFlowMonitor _monitor;
        
        public AudioDataFlowMonitorTests()
        {
            _mockLogger = new Mock<ILogger<AudioDataFlowMonitor>>();
            _monitor = new AudioDataFlowMonitor(_mockLogger.Object);
        }
        
        public void Dispose()
        {
            _monitor?.Dispose();
        }
        
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.NotNull(_monitor);
            Assert.False(_monitor.IsMonitoring);
            Assert.NotNull(_monitor.CurrentHealthStatus);
            Assert.Equal(RecordingQuality.Unknown, _monitor.CurrentHealthStatus.Quality);
        }
        
        [Fact]
        public void StartMonitoring_ShouldSetMonitoringState()
        {
            // Act
            _monitor.StartMonitoring();
            
            // Assert
            Assert.True(_monitor.IsMonitoring);
            Assert.NotNull(_monitor.CurrentHealthStatus.RecordingStartTime);
        }
        
        [Fact]
        public void StopMonitoring_ShouldClearMonitoringState()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act
            _monitor.StopMonitoring();
            
            // Assert
            Assert.False(_monitor.IsMonitoring);
        }
        
        [Fact]
        public void RecordAudioData_WhenMonitoring_ShouldUpdateHealthStatus()
        {
            // Arrange
            _monitor.StartMonitoring();
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            
            // Act
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.True(status.IsDataFlowing);
            Assert.Equal(5, status.BytesWritten);
            Assert.Equal(1, status.AudioFrameCount);
            Assert.True(status.LastDataReceived > DateTime.MinValue);
        }
        
        [Fact]
        public void RecordAudioData_WhenNotMonitoring_ShouldNotUpdateHealthStatus()
        {
            // Arrange
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            
            // Act
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.False(status.IsDataFlowing);
            Assert.Equal(0, status.BytesWritten);
            Assert.Equal(0, status.AudioFrameCount);
        }
        
        [Fact]
        public void RecordAudioData_WithSequenceGap_ShouldDetectLostFrames()
        {
            // Arrange
            _monitor.StartMonitoring();
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            
            // Act
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 1);
            _monitor.RecordAudioData(audioData, AudioSource.RTP_Incoming, 5); // Gap of 3 frames
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.Equal(3, status.LostFrameCount);
            Assert.True(status.FrameLossRate > 0);
        }
        
        [Fact]
        public void RecordBufferStatus_ShouldUpdateBufferUsage()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act
            _monitor.RecordBufferStatus(80, 100, false);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.Equal(80, status.BufferUsage.CurrentSize);
            Assert.Equal(100, status.BufferUsage.MaxSize);
            Assert.Equal(80.0, status.BufferUsage.UsagePercentage);
            Assert.False(status.BufferUsage.IsNearFull);
        }
        
        [Fact]
        public void RecordBufferStatus_WithOverflow_ShouldIncrementOverflowCount()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act
            _monitor.RecordBufferStatus(100, 100, true);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.Equal(1, status.BufferUsage.OverflowCount);
        }
        
        [Fact]
        public void RecordEncoderStatus_ShouldUpdateEncoderHealth()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act
            _monitor.RecordEncoderStatus(true, 50.0, "StreamingEncoder");
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.True(status.EncoderHealth.IsWorking);
            Assert.Equal(50.0, status.EncoderHealth.AverageEncodeTime);
            Assert.Equal("StreamingEncoder", status.EncoderHealth.EncoderType);
            Assert.Equal(0, status.EncoderHealth.FailureCount);
        }
        
        [Fact]
        public void RecordEncoderStatus_WithFailure_ShouldIncrementFailureCount()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act
            _monitor.RecordEncoderStatus(false);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.False(status.EncoderHealth.IsWorking);
            Assert.Equal(1, status.EncoderHealth.FailureCount);
        }
        
        [Fact]
        public void RecordFileSystemStatus_ShouldUpdateFileSystemHealth()
        {
            // Arrange
            _monitor.StartMonitoring();
            var outputPath = "test.wav";
            
            // Act
            _monitor.RecordFileSystemStatus(outputPath, true);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.Equal(outputPath, status.FileSystemHealth.OutputPath);
            Assert.True(status.FileSystemHealth.IsWritable);
            Assert.Equal(0, status.FileSystemHealth.WriteFailureCount);
        }
        
        [Fact]
        public void RecordFileSystemStatus_WithWriteFailure_ShouldIncrementFailureCount()
        {
            // Arrange
            _monitor.StartMonitoring();
            var outputPath = "test.wav";
            
            // Act
            _monitor.RecordFileSystemStatus(outputPath, false);
            
            // Assert
            var status = _monitor.CurrentHealthStatus;
            Assert.False(status.FileSystemHealth.IsWritable);
            Assert.Equal(1, status.FileSystemHealth.WriteFailureCount);
        }
        
        [Fact]
        public void HealthStatusChanged_ShouldTriggerWhenStatusChanges()
        {
            // Arrange
            _monitor.StartMonitoring();
            RecordingHealthStatus? receivedStatus = null;
            _monitor.HealthStatusChanged += (s, status) => receivedStatus = status;
            
            // Act
            _monitor.RecordAudioData(new byte[] { 1, 2, 3 }, AudioSource.RTP_Incoming);
            
            // Wait a bit for the timer to trigger
            Thread.Sleep(1100);
            
            // Assert
            Assert.NotNull(receivedStatus);
        }
        
        [Fact]
        public void CurrentHealthStatus_ShouldReturnClone()
        {
            // Arrange
            _monitor.StartMonitoring();
            _monitor.RecordAudioData(new byte[] { 1, 2, 3 }, AudioSource.RTP_Incoming);
            
            // Act
            var status1 = _monitor.CurrentHealthStatus;
            var status2 = _monitor.CurrentHealthStatus;
            
            // Assert
            Assert.NotSame(status1, status2);
            Assert.Equal(status1.BytesWritten, status2.BytesWritten);
        }
        
        [Fact]
        public void Dispose_ShouldStopMonitoringAndCleanup()
        {
            // Arrange
            _monitor.StartMonitoring();
            
            // Act
            _monitor.Dispose();
            
            // Assert
            Assert.False(_monitor.IsMonitoring);
        }
        
        [Fact]
        public void Dispose_MultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _monitor.Dispose();
            _monitor.Dispose(); // Should not throw
        }
    }
}