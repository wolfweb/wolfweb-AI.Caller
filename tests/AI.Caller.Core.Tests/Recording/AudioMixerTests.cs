using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioMixerTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly AudioMixer _audioMixer;
        
        public AudioMixerTests()
        {
            _mockLogger = new Mock<ILogger>();
            _audioMixer = new AudioMixer(_mockLogger.Object);
        }
        
        [Fact]
        public void Constructor_WithValidLogger_ShouldInitialize()
        {
            // Arrange & Act
            var mixer = new AudioMixer(_mockLogger.Object);
            
            // Assert
            Assert.NotNull(mixer);
        }
        
        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioMixer(null!));
        }
        
        [Fact]
        public void MixFrames_WithEmptyCollection_ShouldReturnNull()
        {
            // Arrange
            var frames = new List<AudioFrame>();
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void MixFrames_WithSingleFrame_ShouldReturnMixedFrame()
        {
            // Arrange
            var audioData = new byte[] { 1, 2, 3, 4 };
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var frame = new AudioFrame(audioData, audioFormat, AudioSource.RTP_Incoming);
            var frames = new List<AudioFrame> { frame };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(audioData, result.Data);
            Assert.Equal(audioFormat.SampleRate, result.Format.SampleRate);
            Assert.Equal(audioFormat.Channels, result.Format.Channels);
        }
        
        [Fact]
        public void MixFrames_WithTwoCompatibleFrames_ShouldReturnMixedFrame()
        {
            // Arrange
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var frame1 = new AudioFrame(new byte[] { 0x00, 0x10, 0x00, 0x20 }, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 0x00, 0x10, 0x00, 0x20 }, audioFormat, AudioSource.RTP_Outgoing);
            var frames = new List<AudioFrame> { frame1, frame2 };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(4, result.Data.Length); // Should be the minimum length
        }
        
        [Fact]
        public void MixFrames_WithIncompatibleFormats_ShouldReturnNull()
        {
            // Arrange
            var format1 = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var format2 = new AudioFormat(16000, 1, 16, AudioSampleFormat.PCM); // Different sample rate
            var frame1 = new AudioFrame(new byte[] { 1, 2, 3, 4 }, format1, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 5, 6, 7, 8 }, format2, AudioSource.RTP_Outgoing);
            var frames = new List<AudioFrame> { frame1, frame2 };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void MixFrames_WithTwoFrames_ShouldReturnMixedFrame()
        {
            // Arrange
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var frame1 = new AudioFrame(new byte[] { 1, 2, 3, 4 }, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 5, 6, 7, 8 }, audioFormat, AudioSource.RTP_Outgoing);
            
            // Act
            var result = _audioMixer.MixFrames(frame1, frame2);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(4, result.Data.Length);
        }
        
        [Fact]
        public void MixFrames_WithAlawFormat_ShouldMixCorrectly()
        {
            // Arrange
            var audioFormat = new AudioFormat(8000, 1, 8, AudioSampleFormat.ALAW);
            var frame1 = new AudioFrame(new byte[] { 0x55, 0x55 }, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 0x55, 0x55 }, audioFormat, AudioSource.RTP_Outgoing);
            var frames = new List<AudioFrame> { frame1, frame2 };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(2, result.Data.Length);
        }
        
        [Fact]
        public void MixFrames_WithUlawFormat_ShouldMixCorrectly()
        {
            // Arrange
            var audioFormat = new AudioFormat(8000, 1, 8, AudioSampleFormat.ULAW);
            var frame1 = new AudioFrame(new byte[] { 0xFF, 0xFF }, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 0xFF, 0xFF }, audioFormat, AudioSource.RTP_Outgoing);
            var frames = new List<AudioFrame> { frame1, frame2 };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(2, result.Data.Length);
        }
        
        [Fact]
        public void MixFrames_WithFloatFormat_ShouldUseByteAveraging()
        {
            // Arrange
            var audioFormat = new AudioFormat(44100, 2, 32, AudioSampleFormat.Float);
            var frame1 = new AudioFrame(new byte[] { 100, 100, 100, 100 }, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 200, 200, 200, 200 }, audioFormat, AudioSource.RTP_Outgoing);
            var frames = new List<AudioFrame> { frame1, frame2 };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(4, result.Data.Length);
            // Should be average of 100 and 200 = 150
            Assert.All(result.Data, b => Assert.Equal(150, b));
        }
        
        [Fact]
        public void MixFrames_WithDifferentLengths_ShouldUseMinimumLength()
        {
            // Arrange
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var frame1 = new AudioFrame(new byte[] { 1, 2, 3, 4, 5, 6 }, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(new byte[] { 7, 8 }, audioFormat, AudioSource.RTP_Outgoing);
            var frames = new List<AudioFrame> { frame1, frame2 };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Data.Length); // Should use minimum length
        }
        
        [Fact]
        public void MixFrames_WithMultipleFrames_ShouldMixAll()
        {
            // Arrange
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var frames = new List<AudioFrame>
            {
                new AudioFrame(new byte[] { 10, 10 }, audioFormat, AudioSource.RTP_Incoming),
                new AudioFrame(new byte[] { 20, 20 }, audioFormat, AudioSource.RTP_Outgoing),
                new AudioFrame(new byte[] { 30, 30 }, audioFormat, AudioSource.WebRTC_Incoming)
            };
            
            // Act
            var result = _audioMixer.MixFrames(frames);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(AudioSource.Mixed, result.Source);
            Assert.Equal(2, result.Data.Length);
        }
        
        [Fact]
        public void Dispose_WhenCalled_ShouldNotThrow()
        {
            // Act & Assert
            _audioMixer.Dispose(); // Should not throw
        }
        
        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _audioMixer.Dispose();
            _audioMixer.Dispose(); // Should not throw
        }
        
        [Fact]
        public void MixFrames_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _audioMixer.Dispose();
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var frame = new AudioFrame(new byte[] { 1, 2, 3, 4 }, audioFormat, AudioSource.RTP_Incoming);
            var frames = new List<AudioFrame> { frame };
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _audioMixer.MixFrames(frames));
        }
        
        public void Dispose()
        {
            _audioMixer?.Dispose();
        }
    }
}