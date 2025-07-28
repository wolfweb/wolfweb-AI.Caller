using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class RecordingCoreTests : IDisposable
    {
        private readonly Mock<ILogger<RecordingCore>> _mockLogger;
        private readonly Mock<IAudioRecordingManager> _mockRecordingManager;
        private readonly RecordingCore _recordingCore;
        private readonly AudioFormat _testAudioFormat;
        
        public RecordingCoreTests()
        {
            _mockLogger = new Mock<ILogger<RecordingCore>>();
            _mockRecordingManager = new Mock<IAudioRecordingManager>();
            _recordingCore = new RecordingCore(_mockRecordingManager.Object, _mockLogger.Object);
            _testAudioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
        }
        
        public void Dispose()
        {
            _recordingCore?.Dispose();
        }
        
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.NotNull(_recordingCore);
        }
        
        [Fact]
        public async Task StartRecordingAsync_ShouldCallRecordingManager()
        {
            // Arrange
            var options = new RecordingOptions();
            _mockRecordingManager.Setup(x => x.StartRecordingAsync(options))
                .ReturnsAsync(true);
            
            // Act
            var result = await _recordingCore.StartRecordingAsync(options);
            
            // Assert
            Assert.True(result);
            _mockRecordingManager.Verify(x => x.StartRecordingAsync(options), Times.Once);
        }
        
        [Fact]
        public async Task StopRecordingAsync_ShouldCallRecordingManager()
        {
            // Arrange
            var expectedPath = "test.wav";
            _mockRecordingManager.Setup(x => x.StopRecordingAsync())
                .ReturnsAsync(expectedPath);
            
            // Act
            var result = await _recordingCore.StopRecordingAsync();
            
            // Assert
            Assert.Equal(expectedPath, result);
            _mockRecordingManager.Verify(x => x.StopRecordingAsync(), Times.Once);
        }
        
        [Fact]
        public void ProcessAudioData_ShouldCallRecordingManager()
        {
            // Arrange
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            // Act
            _recordingCore.ProcessAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Assert
            _mockRecordingManager.Verify(x => x.ProcessAudioFrame(It.Is<AudioFrame>(
                frame => frame.Data.SequenceEqual(audioData) && 
                         frame.Source == AudioSource.RTP_Incoming &&
                         frame.Format == _testAudioFormat
            )), Times.Once);
        }
        
        [Fact]
        public void ProcessAudioData_WithEmptyData_ShouldNotCallRecordingManager()
        {
            // Arrange
            var audioData = new byte[0];
            
            // Act
            _recordingCore.ProcessAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Assert
            _mockRecordingManager.Verify(x => x.ProcessAudioFrame(It.IsAny<AudioFrame>()), Times.Never);
        }
        
        [Fact]
        public void GetStatus_ShouldReturnRecordingManagerStatus()
        {
            // Arrange
            var expectedStatus = new RecordingStatus();
            _mockRecordingManager.Setup(x => x.CurrentStatus).Returns(expectedStatus);
            
            // Act
            var result = _recordingCore.GetStatus();
            
            // Assert
            Assert.Equal(expectedStatus, result);
        }
        
        [Fact]
        public void GetHealthStatus_ShouldReturnHealthStatus()
        {
            // Act
            var healthStatus = _recordingCore.GetHealthStatus();
            
            // Assert
            Assert.NotNull(healthStatus);
            Assert.Equal(RecordingQuality.Unknown, healthStatus.Quality);
        }
        
        [Fact]
        public void ProcessAudioData_ShouldUpdateHealthStatus()
        {
            // Arrange
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            // Act
            _recordingCore.ProcessAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Assert
            var healthStatus = _recordingCore.GetHealthStatus();
            Assert.True(healthStatus.IsDataFlowing);
            Assert.Equal(4, healthStatus.BytesWritten);
            Assert.True(healthStatus.LastDataReceived > DateTime.MinValue);
        }
        
        [Fact]
        public void IsRecording_ShouldReturnRecordingManagerValue()
        {
            // Arrange
            _mockRecordingManager.Setup(x => x.IsRecording).Returns(true);
            
            // Act & Assert
            Assert.True(_recordingCore.IsRecording);
        }
        
        [Fact]
        public void RecordingDuration_ShouldReturnRecordingManagerValue()
        {
            // Arrange
            var expectedDuration = TimeSpan.FromMinutes(5);
            _mockRecordingManager.Setup(x => x.RecordingDuration).Returns(expectedDuration);
            
            // Act & Assert
            Assert.Equal(expectedDuration, _recordingCore.RecordingDuration);
        }
        
        [Fact]
        public void StatusChanged_ShouldForwardRecordingManagerEvent()
        {
            // Arrange
            RecordingStatusEventArgs? receivedArgs = null;
            _recordingCore.StatusChanged += (s, e) => receivedArgs = e;
            
            var testArgs = new RecordingStatusEventArgs(new RecordingStatus());
            
            // Act
            _mockRecordingManager.Raise(x => x.StatusChanged += null, _mockRecordingManager.Object, testArgs);
            
            // Assert
            Assert.NotNull(receivedArgs);
            Assert.Equal(testArgs, receivedArgs);
        }
        
        [Fact]
        public void ErrorOccurred_ShouldForwardRecordingManagerEvent()
        {
            // Arrange
            RecordingErrorEventArgs? receivedArgs = null;
            _recordingCore.ErrorOccurred += (s, e) => receivedArgs = e;
            
            var testArgs = new RecordingErrorEventArgs(RecordingErrorCode.EncodingFailed, "Test error");
            
            // Act
            _mockRecordingManager.Raise(x => x.ErrorOccurred += null, _mockRecordingManager.Object, testArgs);
            
            // Assert
            Assert.NotNull(receivedArgs);
            Assert.Equal(testArgs, receivedArgs);
        }
    }
}