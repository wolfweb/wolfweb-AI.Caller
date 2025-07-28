using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioBridgeTests : IDisposable
    {
        private readonly Mock<ILogger<AudioBridge>> _mockLogger;
        private readonly Mock<IAudioRecordingManager> _mockRecordingManager;
        private readonly AudioBridge _audioBridge;
        private readonly AudioFormat _testAudioFormat;
        
        public AudioBridgeTests()
        {
            _mockLogger = new Mock<ILogger<AudioBridge>>();
            _mockRecordingManager = new Mock<IAudioRecordingManager>();
            _audioBridge = new AudioBridge(_mockLogger.Object);
            _testAudioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
        }
        
        public void Dispose()
        {
            _audioBridge?.Dispose();
        }
        
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.False(_audioBridge.IsRunning);
            Assert.False(_audioBridge.IsRecordingActive);
        }
        
        [Fact]
        public void Start_ShouldSetRunningState()
        {
            // Act
            _audioBridge.Start();
            
            // Assert
            Assert.True(_audioBridge.IsRunning);
        }
        
        [Fact]
        public void Stop_ShouldClearRunningState()
        {
            // Arrange
            _audioBridge.Start();
            
            // Act
            _audioBridge.Stop();
            
            // Assert
            Assert.False(_audioBridge.IsRunning);
        }
        
        [Fact]
        public void RegisterRecordingManager_ShouldSetRecordingManager()
        {
            // Act
            _audioBridge.RegisterRecordingManager(_mockRecordingManager.Object);
            
            // Assert - 通过IsRecordingActive间接验证
            _mockRecordingManager.Setup(x => x.IsRecording).Returns(true);
            Assert.True(_audioBridge.IsRecordingActive);
        }
        
        [Fact]
        public void UnregisterRecordingManager_ShouldClearRecordingManager()
        {
            // Arrange
            _audioBridge.RegisterRecordingManager(_mockRecordingManager.Object);
            
            // Act
            _audioBridge.UnregisterRecordingManager();
            
            // Assert
            Assert.False(_audioBridge.IsRecordingActive);
        }
        
        [Fact]
        public void ForwardAudioData_WhenNotRunning_ShouldNotProcess()
        {
            // Arrange
            var audioData = new byte[] { 1, 2, 3, 4 };
            var eventTriggered = false;
            _audioBridge.AudioDataReceived += (s, e) => eventTriggered = true;
            
            // Act
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Assert
            Assert.False(eventTriggered);
        }
        
        [Fact]
        public void ForwardAudioData_WhenRunning_ShouldTriggerEvent()
        {
            // Arrange
            _audioBridge.Start();
            var audioData = new byte[] { 1, 2, 3, 4 };
            AudioBridgeDataEventArgs? receivedEventArgs = null;
            _audioBridge.AudioDataReceived += (s, e) => receivedEventArgs = e;
            
            // Act
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Assert
            Assert.NotNull(receivedEventArgs);
            Assert.Equal(AudioSource.RTP_Incoming, receivedEventArgs.Source);
            Assert.Equal(audioData, receivedEventArgs.AudioData);
            Assert.Equal(_testAudioFormat, receivedEventArgs.Format);
        }
        
        [Fact]
        public void ForwardAudioData_WithRecordingManager_ShouldForwardToRecordingManager()
        {
            // Arrange
            _audioBridge.Start();
            _audioBridge.RegisterRecordingManager(_mockRecordingManager.Object);
            _mockRecordingManager.Setup(x => x.IsRecording).Returns(true);
            
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            // Act
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Assert
            _mockRecordingManager.Verify(x => x.ProcessAudioFrame(It.Is<AudioFrame>(
                frame => frame.Data.SequenceEqual(audioData) && 
                         frame.Source == AudioSource.RTP_Incoming &&
                         frame.Format == _testAudioFormat
            )), Times.Once);
        }
        
        [Fact]
        public void ForwardAudioData_WithEmptyData_ShouldNotProcess()
        {
            // Arrange
            _audioBridge.Start();
            var eventTriggered = false;
            _audioBridge.AudioDataReceived += (s, e) => eventTriggered = true;
            
            // Act
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, new byte[0], _testAudioFormat);
            
            // Assert
            Assert.False(eventTriggered);
        }
        
        [Fact]
        public void GetStats_ShouldReturnCorrectStatistics()
        {
            // Arrange
            _audioBridge.Start();
            var audioData1 = new byte[] { 1, 2, 3, 4 };
            var audioData2 = new byte[] { 5, 6, 7, 8, 9 };
            
            // Act
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData1, _testAudioFormat);
            _audioBridge.ForwardAudioData(AudioSource.WebRTC_Outgoing, audioData2, _testAudioFormat);
            
            var stats = _audioBridge.GetStats();
            
            // Assert
            Assert.Equal(2, stats.TotalFramesForwarded);
            Assert.Equal(9, stats.TotalBytesForwarded); // 4 + 5 bytes
            Assert.Equal(1, stats.FramesBySource[AudioSource.RTP_Incoming]);
            Assert.Equal(1, stats.FramesBySource[AudioSource.WebRTC_Outgoing]);
            Assert.Equal(4, stats.BytesBySource[AudioSource.RTP_Incoming]);
            Assert.Equal(5, stats.BytesBySource[AudioSource.WebRTC_Outgoing]);
        }
        
        [Fact]
        public void ResetStats_ShouldClearStatistics()
        {
            // Arrange
            _audioBridge.Start();
            var audioData = new byte[] { 1, 2, 3, 4 };
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
            
            // Act
            _audioBridge.ResetStats();
            var stats = _audioBridge.GetStats();
            
            // Assert
            Assert.Equal(0, stats.TotalFramesForwarded);
            Assert.Equal(0, stats.TotalBytesForwarded);
            Assert.Empty(stats.FramesBySource);
            Assert.Empty(stats.BytesBySource);
        }
        
        [Fact]
        public void Dispose_ShouldStopBridgeAndCleanup()
        {
            // Arrange
            _audioBridge.Start();
            
            // Act
            _audioBridge.Dispose();
            
            // Assert
            Assert.False(_audioBridge.IsRunning);
        }
        
        [Fact]
        public void ForwardAudioData_AfterDispose_ShouldNotThrow()
        {
            // Arrange
            _audioBridge.Start();
            _audioBridge.Dispose();
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            // Act & Assert - Should not throw
            _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, _testAudioFormat);
        }
        
        [Fact]
        public void RegisterRecordingManager_AfterDispose_ShouldThrow()
        {
            // Arrange
            _audioBridge.Dispose();
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _audioBridge.RegisterRecordingManager(_mockRecordingManager.Object));
        }
        
        [Fact]
        public void Start_AfterDispose_ShouldThrow()
        {
            // Arrange
            _audioBridge.Dispose();
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _audioBridge.Start());
        }
    }
}