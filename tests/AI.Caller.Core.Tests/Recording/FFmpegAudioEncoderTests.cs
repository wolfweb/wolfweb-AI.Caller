using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class FFmpegAudioEncoderTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly AudioEncodingOptions _defaultOptions;
        
        public FFmpegAudioEncoderTests()
        {
            _mockLogger = new Mock<ILogger>();
            _defaultOptions = AudioEncodingOptions.CreateDefault();
        }
        
        [Fact]
        public void Constructor_WithValidParameters_ShouldInitialize()
        {
            // Arrange & Act
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            
            // Assert
            Assert.False(encoder.IsInitialized);
            Assert.Null(encoder.OutputFilePath);
            Assert.Equal(_defaultOptions, encoder.Options);
        }
        
        [Fact]
        public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new FFmpegAudioEncoder(null!, _mockLogger.Object));
        }
        
        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new FFmpegAudioEncoder(_defaultOptions, null!));
        }
        
        [Fact]
        public void Constructor_WithUnsupportedCodec_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidOptions = new AudioEncodingOptions { Codec = (AudioCodec)999 };
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new FFmpegAudioEncoder(invalidOptions, _mockLogger.Object));
        }
        
        [Fact]
        public void Constructor_WithInvalidSampleRate_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidOptions = new AudioEncodingOptions { SampleRate = 0 };
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new FFmpegAudioEncoder(invalidOptions, _mockLogger.Object));
        }
        
        [Fact]
        public void Constructor_WithInvalidChannels_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidOptions = new AudioEncodingOptions { Channels = 0 };
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new FFmpegAudioEncoder(invalidOptions, _mockLogger.Object));
        }
        
        [Theory]
        [InlineData(AudioCodec.PCM_WAV)]
        [InlineData(AudioCodec.MP3)]
        [InlineData(AudioCodec.AAC)]
        [InlineData(AudioCodec.OPUS)]
        public void IsCodecSupported_WithSupportedCodecs_ShouldReturnTrue(AudioCodec codec)
        {
            // Act
            var isSupported = FFmpegAudioEncoder.IsCodecSupported(codec);
            
            // Assert
            Assert.True(isSupported);
        }
        
        [Fact]
        public void IsCodecSupported_WithUnsupportedCodec_ShouldReturnFalse()
        {
            // Act
            var isSupported = FFmpegAudioEncoder.IsCodecSupported((AudioCodec)999);
            
            // Assert
            Assert.False(isSupported);
        }
        
        [Theory]
        [InlineData(AudioCodec.PCM_WAV, ".wav")]
        [InlineData(AudioCodec.MP3, ".mp3")]
        [InlineData(AudioCodec.AAC, ".m4a")]
        [InlineData(AudioCodec.OPUS, ".opus")]
        public void GetRecommendedFileExtension_WithSupportedCodecs_ShouldReturnCorrectExtension(AudioCodec codec, string expectedExtension)
        {
            // Act
            var extension = FFmpegAudioEncoder.GetRecommendedFileExtension(codec);
            
            // Assert
            Assert.Equal(expectedExtension, extension);
        }
        
        [Fact]
        public void GetRecommendedFileExtension_WithUnsupportedCodec_ShouldReturnDefaultExtension()
        {
            // Act
            var extension = FFmpegAudioEncoder.GetRecommendedFileExtension((AudioCodec)999);
            
            // Assert
            Assert.Equal(".audio", extension);
        }
        
        [Fact]
        public void GetEncoderInfo_ShouldReturnCorrectInfo()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            
            // Act
            var info = encoder.GetEncoderInfo();
            
            // Assert
            Assert.Equal(_defaultOptions.Codec, info.Codec);
            Assert.Equal(_defaultOptions.SampleRate, info.SampleRate);
            Assert.Equal(_defaultOptions.Channels, info.Channels);
            Assert.Equal(_defaultOptions.BitRate, info.BitRate);
            Assert.False(info.IsInitialized);
            Assert.Null(info.OutputPath);
        }
        
        [Fact]
        public async Task EncodeAudioFrameAsync_WhenNotInitialized_ShouldReturnFalse()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var audioFrame = new AudioFrame(new byte[] { 1, 2, 3, 4 }, audioFormat, AudioSource.RTP_Incoming);
            
            // Act
            var result = await encoder.EncodeAudioFrameAsync(audioFrame);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task EncodeAudioFrameAsync_WithNullFrame_ShouldReturnFalse()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            
            // Act
            var result = await encoder.EncodeAudioFrameAsync(null!);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task EncodeAudioFrameAsync_WithEmptyFrame_ShouldReturnTrue()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            
            // 初始化编码器
            encoder.Initialize(audioFormat, "test.wav");
            
            var audioFrame = new AudioFrame(new byte[0], audioFormat, AudioSource.RTP_Incoming);
            
            // Act
            var result = await encoder.EncodeAudioFrameAsync(audioFrame);
            
            // Assert
            Assert.True(result); // 空帧应该返回true（跳过处理）
        }
        
        [Fact]
        public void Dispose_WhenCalled_ShouldNotThrow()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            
            // Act & Assert
            encoder.Dispose(); // Should not throw
        }
        
        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            
            // Act & Assert
            encoder.Dispose();
            encoder.Dispose(); // Should not throw
        }
        
        [Fact]
        public void Initialize_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            encoder.Dispose();
            
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => encoder.Initialize(audioFormat, "test.wav"));
        }
        
        [Fact]
        public async Task EncodeAudioFrameAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            encoder.Dispose();
            
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var audioFrame = new AudioFrame(new byte[] { 1, 2, 3, 4 }, audioFormat, AudioSource.RTP_Incoming);
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                encoder.EncodeAudioFrameAsync(audioFrame));
        }
        
        [Fact]
        public async Task FinalizeAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            var encoder = new FFmpegAudioEncoder(_defaultOptions, _mockLogger.Object);
            encoder.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                encoder.FinalizeAsync());
        }
        
        public void Dispose()
        {
            // Cleanup if needed
        }
    }
    
    public class AudioEncodingOptionsTests
    {
        [Fact]
        public void CreateDefault_ShouldReturnDefaultOptions()
        {
            // Act
            var options = AudioEncodingOptions.CreateDefault();
            
            // Assert
            Assert.Equal(AudioCodec.PCM_WAV, options.Codec);
            Assert.Equal(8000, options.SampleRate);
            Assert.Equal(1, options.Channels);
            Assert.Equal(64000, options.BitRate);
            Assert.Equal(AudioQuality.Standard, options.Quality);
        }
        
        [Fact]
        public void CreateHighQuality_ShouldReturnHighQualityOptions()
        {
            // Act
            var options = AudioEncodingOptions.CreateHighQuality();
            
            // Assert
            Assert.Equal(AudioCodec.AAC, options.Codec);
            Assert.Equal(44100, options.SampleRate);
            Assert.Equal(2, options.Channels);
            Assert.Equal(128000, options.BitRate);
            Assert.Equal(AudioQuality.High, options.Quality);
        }
        
        [Fact]
        public void CreateLowQuality_ShouldReturnLowQualityOptions()
        {
            // Act
            var options = AudioEncodingOptions.CreateLowQuality();
            
            // Assert
            Assert.Equal(AudioCodec.MP3, options.Codec);
            Assert.Equal(8000, options.SampleRate);
            Assert.Equal(1, options.Channels);
            Assert.Equal(32000, options.BitRate);
            Assert.Equal(AudioQuality.Low, options.Quality);
        }
    }
    
    public class EncoderInfoTests
    {
        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var info = new EncoderInfo
            {
                Codec = AudioCodec.MP3,
                SampleRate = 44100,
                Channels = 2,
                BitRate = 128000,
                IsInitialized = true,
                OutputPath = "/test/output.mp3"
            };
            
            // Act
            var result = info.ToString();
            
            // Assert
            Assert.Contains("MP3", result);
            Assert.Contains("44100Hz", result);
            Assert.Contains("2ch", result);
            Assert.Contains("128000bps", result);
            Assert.Contains("/test/output.mp3", result);
        }
    }
}