using AI.Caller.Core.Recording;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class RecordingOptionsTests
    {
        [Fact]
        public void RecordingOptions_DefaultValues_ShouldBeValid()
        {
            // Arrange & Act
            var options = new RecordingOptions();
            var result = options.Validate();
            
            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Equal(AudioCodec.PCM_WAV, options.Codec);
            Assert.Equal(8000, options.SampleRate);
            Assert.Equal(1, options.Channels);
            Assert.Equal(64000, options.BitRate);
            Assert.Equal("./recordings", options.OutputDirectory);
            Assert.Equal("{timestamp}_{caller}_{duration}", options.FileNameTemplate);
            Assert.False(options.AutoStart);
            Assert.True(options.RecordBothParties);
            Assert.Equal(TimeSpan.FromHours(2), options.MaxDuration);
            Assert.Equal(100 * 1024 * 1024, options.MaxFileSize);
            Assert.Equal(AudioQuality.Standard, options.Quality);
            Assert.False(options.EnableNoiseReduction);
            Assert.False(options.EnableVolumeNormalization);
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(192001)]
        public void Validate_InvalidSampleRate_ShouldReturnError(int sampleRate)
        {
            // Arrange
            var options = new RecordingOptions { SampleRate = sampleRate };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("采样率必须在1-192000Hz之间", result.Errors);
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(9)]
        public void Validate_InvalidChannels_ShouldReturnError(int channels)
        {
            // Arrange
            var options = new RecordingOptions { Channels = channels };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("声道数必须在1-8之间", result.Errors);
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(320001)]
        public void Validate_InvalidBitRate_ShouldReturnError(int bitRate)
        {
            // Arrange
            var options = new RecordingOptions { BitRate = bitRate };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("比特率必须在1-320000bps之间", result.Errors);
        }
        
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Validate_InvalidOutputDirectory_ShouldReturnError(string outputDirectory)
        {
            // Arrange
            var options = new RecordingOptions { OutputDirectory = outputDirectory };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("输出目录不能为空", result.Errors);
        }
        
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Validate_InvalidFileNameTemplate_ShouldReturnError(string fileNameTemplate)
        {
            // Arrange
            var options = new RecordingOptions { FileNameTemplate = fileNameTemplate };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("文件名模板不能为空", result.Errors);
        }
        
        [Fact]
        public void Validate_InvalidMaxDuration_ShouldReturnError()
        {
            // Arrange
            var options = new RecordingOptions { MaxDuration = TimeSpan.Zero };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("最大录音时长必须大于0", result.Errors);
        }
        
        [Fact]
        public void Validate_InvalidMaxFileSize_ShouldReturnError()
        {
            // Arrange
            var options = new RecordingOptions { MaxFileSize = 0 };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("最大文件大小必须大于0", result.Errors);
        }
        
        [Fact]
        public void Validate_MultipleErrors_ShouldReturnAllErrors()
        {
            // Arrange
            var options = new RecordingOptions 
            { 
                SampleRate = 0,
                Channels = 0,
                BitRate = 0,
                OutputDirectory = "",
                FileNameTemplate = "",
                MaxDuration = TimeSpan.Zero,
                MaxFileSize = 0
            };
            
            // Act
            var result = options.Validate();
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(7, result.Errors.Count);
        }
    }
}