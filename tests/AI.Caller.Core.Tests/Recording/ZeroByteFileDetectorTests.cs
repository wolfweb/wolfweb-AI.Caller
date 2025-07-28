using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class ZeroByteFileDetectorTests : IDisposable
    {
        private readonly Mock<ILogger<ZeroByteFileDetector>> _mockLogger;
        private readonly ZeroByteFileDetector _detector;
        private readonly string _testDirectory;
        
        public ZeroByteFileDetectorTests()
        {
            _mockLogger = new Mock<ILogger<ZeroByteFileDetector>>();
            _detector = new ZeroByteFileDetector(_mockLogger.Object);
            _testDirectory = Path.Combine(Path.GetTempPath(), "ZeroByteFileDetectorTests");
            Directory.CreateDirectory(_testDirectory);
        }
        
        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        
        [Fact]
        public async Task ValidateRecordingFileAsync_WithNullPath_ShouldReturnInvalid()
        {
            // Act
            var result = await _detector.ValidateRecordingFileAsync(null!);
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("File path is null or empty", result.Issues);
        }
        
        [Fact]
        public async Task ValidateRecordingFileAsync_WithNonExistentFile_ShouldReturnInvalid()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "nonexistent.wav");
            
            // Act
            var result = await _detector.ValidateRecordingFileAsync(filePath);
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("File does not exist", result.Issues);
        }
        
        [Fact]
        public async Task ValidateRecordingFileAsync_WithZeroByteFile_ShouldReturnInvalid()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "empty.wav");
            File.WriteAllText(filePath, ""); // 创建0字节文件
            
            // Act
            var result = await _detector.ValidateRecordingFileAsync(filePath);
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(0, result.FileSize);
            Assert.Contains("File is empty (0 bytes)", result.Issues);
        }
        
        [Fact]
        public async Task ValidateRecordingFileAsync_WithTooSmallWavFile_ShouldReturnInvalid()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "small.wav");
            File.WriteAllBytes(filePath, new byte[20]); // 小于44字节的WAV头部
            
            // Act
            var result = await _detector.ValidateRecordingFileAsync(filePath);
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(20, result.FileSize);
            Assert.Contains("File too small", result.Issues);
        }
        
        [Fact]
        public async Task ValidateRecordingFileAsync_WithValidWavHeader_ShouldDetectFormat()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "valid.wav");
            var wavData = CreateValidWavFile();
            File.WriteAllBytes(filePath, wavData);
            
            // Act
            var result = await _detector.ValidateRecordingFileAsync(filePath);
            
            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(wavData.Length, result.FileSize);
            Assert.NotNull(result.AudioFormat);
        }
        
        [Fact]
        public async Task AttemptRecoveryAsync_WithValidData_ShouldCreateValidFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "recovery.wav");
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var bufferedData = new List<byte[]>
            {
                new byte[] { 1, 2, 3, 4 },
                new byte[] { 5, 6, 7, 8 }
            };
            
            // Act
            var result = await _detector.AttemptRecoveryAsync(filePath, bufferedData, audioFormat);
            
            // Assert
            Assert.True(result);
            Assert.True(File.Exists(filePath));
            
            var fileInfo = new FileInfo(filePath);
            Assert.True(fileInfo.Length > 44); // 应该有WAV头部 + 数据
        }
        
        [Fact]
        public async Task AttemptRecoveryAsync_WithNoData_ShouldReturnFalse()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "recovery_empty.wav");
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var bufferedData = new List<byte[]>();
            
            // Act
            var result = await _detector.AttemptRecoveryAsync(filePath, bufferedData, audioFormat);
            
            // Assert
            Assert.False(result);
        }
        
        private byte[] CreateValidWavFile()
        {
            var header = new byte[44];
            var pos = 0;
            
            // RIFF头部
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, header, pos, 4);
            pos += 4;
            
            // 文件大小 - 8
            Array.Copy(BitConverter.GetBytes(36), 0, header, pos, 4);
            pos += 4;
            
            // WAVE标识
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, header, pos, 4);
            pos += 4;
            
            // fmt块
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, header, pos, 4);
            pos += 4;
            
            // fmt块大小
            Array.Copy(BitConverter.GetBytes(16), 0, header, pos, 4);
            pos += 4;
            
            // 音频格式 (PCM = 1)
            Array.Copy(BitConverter.GetBytes((short)1), 0, header, pos, 2);
            pos += 2;
            
            // 声道数
            Array.Copy(BitConverter.GetBytes((short)1), 0, header, pos, 2);
            pos += 2;
            
            // 采样率
            Array.Copy(BitConverter.GetBytes(8000), 0, header, pos, 4);
            pos += 4;
            
            // 字节率
            Array.Copy(BitConverter.GetBytes(16000), 0, header, pos, 4);
            pos += 4;
            
            // 块对齐
            Array.Copy(BitConverter.GetBytes((short)2), 0, header, pos, 2);
            pos += 2;
            
            // 位深度
            Array.Copy(BitConverter.GetBytes((short)16), 0, header, pos, 2);
            pos += 2;
            
            // data块
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, header, pos, 4);
            pos += 4;
            
            // 数据大小
            Array.Copy(BitConverter.GetBytes(0), 0, header, pos, 4);
            
            return header;
        }
    }
}