using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioFormatConverterTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly AudioFormatConverter _converter;
        
        public AudioFormatConverterTests()
        {
            _mockLogger = new Mock<ILogger>();
            _converter = new AudioFormatConverter(_mockLogger.Object);
        }
        
        [Fact]
        public void Constructor_WithValidLogger_ShouldInitialize()
        {
            // Arrange & Act
            var converter = new AudioFormatConverter(_mockLogger.Object);
            
            // Assert
            Assert.NotNull(converter);
        }
        
        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioFormatConverter(null!));
        }
        
        [Fact]
        public void ConvertFormat_WithNullFrame_ShouldReturnNull()
        {
            // Arrange
            var targetFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var result = _converter.ConvertFormat(null!, targetFormat);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ConvertFormat_WithEmptyFrame_ShouldReturnNull()
        {
            // Arrange
            var sourceFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var targetFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var frame = new AudioFrame(new byte[0], sourceFormat, AudioSource.RTP_Incoming);
            
            // Act
            var result = _converter.ConvertFormat(frame, targetFormat);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ConvertFormat_WithCompatibleFormats_ShouldReturnOriginalFrame()
        {
            // Arrange
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var frame = new AudioFrame(new byte[] { 1, 2, 3, 4 }, format, AudioSource.RTP_Incoming);
            
            // Act
            var result = _converter.ConvertFormat(frame, format);
            
            // Assert
            Assert.Equal(frame, result);
        }
        
        [Fact]
        public void ConvertFormat_WithDifferentFormats_ShouldReturnConvertedFrame()
        {
            // Arrange
            var sourceFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var targetFormat = new AudioFormat(16000, 1, 16, AudioSampleFormat.PCM);
            var frame = new AudioFrame(new byte[] { 1, 2, 3, 4 }, sourceFormat, AudioSource.RTP_Incoming);
            
            // Act
            var result = _converter.ConvertFormat(frame, targetFormat);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(targetFormat.SampleRate, result.Format.SampleRate);
            Assert.Equal(frame.Source, result.Source);
            Assert.Equal(frame.SequenceNumber, result.SequenceNumber);
        }
        
        [Fact]
        public void ResampleAudio_WithSameSampleRate_ShouldReturnOriginalData()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            
            // Act
            var result = _converter.ResampleAudio(inputData, 44100, 44100, 2, 16);
            
            // Assert
            Assert.Equal(inputData, result);
        }
        
        [Fact]
        public void ResampleAudio_WithNullData_ShouldReturnNull()
        {
            // Act
            var result = _converter.ResampleAudio(null!, 44100, 22050, 2, 16);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ResampleAudio_WithEmptyData_ShouldReturnNull()
        {
            // Act
            var result = _converter.ResampleAudio(new byte[0], 44100, 22050, 2, 16);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ResampleAudio_WithDownsampling_ShouldReturnSmallerArray()
        {
            // Arrange
            var inputData = new byte[8]; // 2 samples, 2 channels, 16-bit
            for (int i = 0; i < inputData.Length; i++)
                inputData[i] = (byte)(i + 1);
            
            // Act
            var result = _converter.ResampleAudio(inputData, 44100, 22050, 2, 16);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length < inputData.Length);
        }
        
        [Fact]
        public void ResampleAudio_WithUpsampling_ShouldReturnLargerArray()
        {
            // Arrange
            var inputData = new byte[8]; // 2 samples, 2 channels, 16-bit
            for (int i = 0; i < inputData.Length; i++)
                inputData[i] = (byte)(i + 1);
            
            // Act
            var result = _converter.ResampleAudio(inputData, 22050, 44100, 2, 16);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > inputData.Length);
        }
        
        [Fact]
        public void ConvertChannels_WithSameChannelCount_ShouldReturnOriginalData()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            
            // Act
            var result = _converter.ConvertChannels(inputData, 2, 2, 16);
            
            // Assert
            Assert.Equal(inputData, result);
        }
        
        [Fact]
        public void ConvertChannels_WithNullData_ShouldReturnNull()
        {
            // Act
            var result = _converter.ConvertChannels(null!, 1, 2, 16);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ConvertChannels_WithEmptyData_ShouldReturnNull()
        {
            // Act
            var result = _converter.ConvertChannels(new byte[0], 1, 2, 16);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ConvertChannels_MonoToStereo_ShouldDoubleDataSize()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4 }; // 2 mono samples, 16-bit
            
            // Act
            var result = _converter.ConvertChannels(inputData, 1, 2, 16);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(inputData.Length * 2, result.Length);
        }
        
        [Fact]
        public void ConvertChannels_StereoToMono_ShouldHalveDataSize()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; // 2 stereo samples, 16-bit
            
            // Act
            var result = _converter.ConvertChannels(inputData, 2, 1, 16);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(inputData.Length / 2, result.Length);
        }
        
        [Fact]
        public void ConvertBitDepth_WithSameBitDepth_ShouldReturnOriginalData()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            
            // Act
            var result = _converter.ConvertBitDepth(inputData, 16, 16, 2);
            
            // Assert
            Assert.Equal(inputData, result);
        }
        
        [Fact]
        public void ConvertBitDepth_WithNullData_ShouldReturnNull()
        {
            // Act
            var result = _converter.ConvertBitDepth(null!, 16, 8, 2);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ConvertBitDepth_WithEmptyData_ShouldReturnNull()
        {
            // Act
            var result = _converter.ConvertBitDepth(new byte[0], 16, 8, 2);
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void ConvertBitDepth_From16To8_ShouldHalveDataSize()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; // 2 samples, 2 channels, 16-bit
            
            // Act
            var result = _converter.ConvertBitDepth(inputData, 16, 8, 2);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(inputData.Length / 2, result.Length);
        }
        
        [Fact]
        public void ConvertBitDepth_From8To16_ShouldDoubleDataSize()
        {
            // Arrange
            var inputData = new byte[] { 1, 2, 3, 4 }; // 2 samples, 2 channels, 8-bit
            
            // Act
            var result = _converter.ConvertBitDepth(inputData, 8, 16, 2);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(inputData.Length * 2, result.Length);
        }
        
        [Fact]
        public void Dispose_WhenCalled_ShouldNotThrow()
        {
            // Act & Assert
            _converter.Dispose(); // Should not throw
        }
        
        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _converter.Dispose();
            _converter.Dispose(); // Should not throw
        }
        
        [Fact]
        public void ConvertFormat_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _converter.Dispose();
            var sourceFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var targetFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var frame = new AudioFrame(new byte[] { 1, 2, 3, 4 }, sourceFormat, AudioSource.RTP_Incoming);
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _converter.ConvertFormat(frame, targetFormat));
        }
        
        [Fact]
        public void ResampleAudio_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _converter.Dispose();
            var inputData = new byte[] { 1, 2, 3, 4 };
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => 
                _converter.ResampleAudio(inputData, 44100, 22050, 2, 16));
        }
        
        [Fact]
        public void ConvertChannels_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _converter.Dispose();
            var inputData = new byte[] { 1, 2, 3, 4 };
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => 
                _converter.ConvertChannels(inputData, 1, 2, 16));
        }
        
        [Fact]
        public void ConvertBitDepth_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _converter.Dispose();
            var inputData = new byte[] { 1, 2, 3, 4 };
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => 
                _converter.ConvertBitDepth(inputData, 16, 8, 2));
        }
        
        public void Dispose()
        {
            _converter?.Dispose();
        }
    }
}