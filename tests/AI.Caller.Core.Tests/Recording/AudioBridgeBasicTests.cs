using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    /// <summary>
    /// AudioBridge基础功能测试
    /// </summary>
    public class AudioBridgeBasicTests
    {
        [Fact]
        public void AudioBridge_CanBeCreatedAndDisposed()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<AudioBridge>>();
            
            // Act & Assert
            using var audioBridge = new AudioBridge(mockLogger.Object);
            Assert.NotNull(audioBridge);
            Assert.False(audioBridge.IsRunning);
            Assert.False(audioBridge.IsRecordingActive);
        }
        
        [Fact]
        public void AudioBridge_StartStop_WorksCorrectly()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<AudioBridge>>();
            using var audioBridge = new AudioBridge(mockLogger.Object);
            
            // Act & Assert
            audioBridge.Start();
            Assert.True(audioBridge.IsRunning);
            
            audioBridge.Stop();
            Assert.False(audioBridge.IsRunning);
        }
        
        [Fact]
        public void AudioBridge_ForwardAudioData_UpdatesStats()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<AudioBridge>>();
            using var audioBridge = new AudioBridge(mockLogger.Object);
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            audioBridge.Start();
            
            // Act
            audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, audioFormat);
            
            // Assert
            var stats = audioBridge.GetStats();
            Assert.Equal(1, stats.TotalFramesForwarded);
            Assert.Equal(4, stats.TotalBytesForwarded);
        }
    }
}