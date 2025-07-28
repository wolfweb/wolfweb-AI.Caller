using AI.Caller.Core.Recording;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioModelsTests
    {
        [Fact]
        public void AudioFormat_Constructor_ShouldSetProperties()
        {
            // Arrange & Act
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Assert
            Assert.Equal(44100, format.SampleRate);
            Assert.Equal(2, format.Channels);
            Assert.Equal(16, format.BitsPerSample);
            Assert.Equal(AudioSampleFormat.PCM, format.SampleFormat);
        }
        
        [Fact]
        public void AudioFormat_ByteRate_ShouldCalculateCorrectly()
        {
            // Arrange
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var byteRate = format.ByteRate;
            
            // Assert
            // 44100 * 2 * 16 / 8 = 176400
            Assert.Equal(176400, byteRate);
        }
        
        [Fact]
        public void AudioFormat_BlockAlign_ShouldCalculateCorrectly()
        {
            // Arrange
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var blockAlign = format.BlockAlign;
            
            // Assert
            // 2 * 16 / 8 = 4
            Assert.Equal(4, blockAlign);
        }
        
        [Fact]
        public void AudioFormat_IsCompatibleWith_SameFormat_ShouldReturnTrue()
        {
            // Arrange
            var format1 = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var format2 = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var isCompatible = format1.IsCompatibleWith(format2);
            
            // Assert
            Assert.True(isCompatible);
        }
        
        [Fact]
        public void AudioFormat_IsCompatibleWith_DifferentFormat_ShouldReturnFalse()
        {
            // Arrange
            var format1 = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var format2 = new AudioFormat(48000, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var isCompatible = format1.IsCompatibleWith(format2);
            
            // Assert
            Assert.False(isCompatible);
        }
        
        [Fact]
        public void AudioFormat_IsCompatibleWith_Null_ShouldReturnFalse()
        {
            // Arrange
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var isCompatible = format.IsCompatibleWith(null!);
            
            // Assert
            Assert.False(isCompatible);
        }
        
        [Fact]
        public void AudioFormat_ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            var result = format.ToString();
            
            // Assert
            Assert.Equal("44100Hz, 2ch, 16bit, PCM", result);
        }
        
        [Fact]
        public void AudioFrame_Constructor_ShouldSetProperties()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3, 4 };
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var source = AudioSource.RTP_Incoming;
            
            // Act
            var frame = new AudioFrame(data, format, source);
            
            // Assert
            Assert.Equal(data, frame.Data);
            Assert.Equal(format, frame.Format);
            Assert.Equal(source, frame.Source);
            Assert.True(frame.Timestamp <= DateTime.UtcNow);
            Assert.True(frame.Timestamp > DateTime.UtcNow.AddSeconds(-1));
        }
        
        [Fact]
        public void AudioFrame_Constructor_NullData_ShouldThrowArgumentNullException()
        {
            // Arrange
            var format = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var source = AudioSource.RTP_Incoming;
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioFrame(null!, format, source));
        }
        
        [Fact]
        public void AudioFrame_Constructor_NullFormat_ShouldThrowArgumentNullException()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3, 4 };
            var source = AudioSource.RTP_Incoming;
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioFrame(data, null!, source));
        }
    }
}