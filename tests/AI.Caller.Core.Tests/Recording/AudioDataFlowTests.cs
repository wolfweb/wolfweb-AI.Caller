using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioDataFlowTests : IDisposable
    {
        private readonly Mock<ILogger<AudioDataFlow>> _mockLogger;
        private readonly Mock<IStreamingAudioEncoder> _mockEncoder;
        private readonly AudioDataFlow _audioDataFlow;
        private readonly AudioFormat _testAudioFormat;
        
        public AudioDataFlowTests()
        {
            _mockLogger = new Mock<ILogger<AudioDataFlow>>();
            _mockEncoder = new Mock<IStreamingAudioEncoder>();
            _audioDataFlow = new AudioDataFlow(_mockEncoder.Object, _mockLogger.Object);
            _testAudioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
        }
        
        public void Dispose()
        {
            _audioDataFlow?.Dispose();
        }
        
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.NotNull(_audioDataFlow);
            Assert.False(_audioDataFlow.IsInitialized);
            Assert.Null(_audioDataFlow.OutputPath);
        }
        
        [Fact]
        public async Task InitializeAsync_ShouldInitializeCorrectly()
        {
            // Arrange
            var outputPath = "test.wav";
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            
            // Act
            var result = await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            // Assert
            Assert.True(result);
            Assert.True(_audioDataFlow.IsInitialized);
            Assert.Equal(outputPath, _audioDataFlow.OutputPath);
            _mockEncoder.Verify(x => x.InitializeAsync(_testAudioFormat, outputPath), Times.Once);
        }
        
        [Fact]
        public async Task InitializeAsync_WhenEncoderFails_ShouldReturnFalse()
        {
            // Arrange
            var outputPath = "test.wav";
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(false);
            
            // Act
            var result = await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            // Assert
            Assert.False(result);
            Assert.False(_audioDataFlow.IsInitialized);
        }
        
        [Fact]
        public async Task WriteAudioDataAsync_WhenNotInitialized_ShouldReturnFalse()
        {
            // Arrange
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            // Act
            var result = await _audioDataFlow.WriteAudioDataAsync(audioData, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task WriteAudioDataAsync_WhenInitialized_ShouldWriteData()
        {
            // Arrange
            var outputPath = "test.wav";
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            _mockEncoder.Setup(x => x.WriteAudioFrameAsync(It.IsAny<AudioFrame>()))
                .ReturnsAsync(true);
            
            await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            // Act
            var result = await _audioDataFlow.WriteAudioDataAsync(audioData, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.True(result);
            _mockEncoder.Verify(x => x.WriteAudioFrameAsync(It.Is<AudioFrame>(
                frame => frame.Data.SequenceEqual(audioData) && 
                         frame.Source == AudioSource.RTP_Incoming
            )), Times.Once);
        }
        
        [Fact]
        public async Task WriteAudioDataAsync_WithEmptyData_ShouldReturnTrue()
        {
            // Arrange
            var outputPath = "test.wav";
            var audioData = new byte[0];
            
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            
            await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            // Act
            var result = await _audioDataFlow.WriteAudioDataAsync(audioData, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.True(result);
            _mockEncoder.Verify(x => x.WriteAudioFrameAsync(It.IsAny<AudioFrame>()), Times.Never);
        }
        
        [Fact]
        public async Task FlushAsync_ShouldCallEncoder()
        {
            // Arrange
            var outputPath = "test.wav";
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            _mockEncoder.Setup(x => x.FlushAsync())
                .ReturnsAsync(true);
            
            await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            // Act
            var result = await _audioDataFlow.FlushAsync();
            
            // Assert
            Assert.True(result);
            _mockEncoder.Verify(x => x.FlushAsync(), Times.Once);
        }
        
        [Fact]
        public async Task FinalizeAsync_ShouldCallEncoder()
        {
            // Arrange
            var outputPath = "test.wav";
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            _mockEncoder.Setup(x => x.FinalizeAsync())
                .ReturnsAsync(true);
            
            await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            // Act
            var result = await _audioDataFlow.FinalizeAsync();
            
            // Assert
            Assert.True(result);
            Assert.False(_audioDataFlow.IsInitialized);
            _mockEncoder.Verify(x => x.FinalizeAsync(), Times.Once);
        }
        
        [Fact]
        public async Task GetBytesWritten_ShouldReturnCorrectValue()
        {
            // Arrange
            var outputPath = "test.wav";
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            _mockEncoder.Setup(x => x.WriteAudioFrameAsync(It.IsAny<AudioFrame>()))
                .ReturnsAsync(true);
            
            await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            await _audioDataFlow.WriteAudioDataAsync(audioData, AudioSource.RTP_Incoming);
            
            // Act
            var bytesWritten = _audioDataFlow.GetBytesWritten();
            
            // Assert
            Assert.Equal(4, bytesWritten);
        }
        
        [Fact]
        public void IsHealthy_ShouldReturnTrue_Initially()
        {
            // Act & Assert
            Assert.True(_audioDataFlow.IsHealthy());
        }
        
        [Fact]
        public void GetStats_ShouldReturnCorrectStats()
        {
            // Act
            var stats = _audioDataFlow.GetStats();
            
            // Assert
            Assert.NotNull(stats);
            Assert.Equal(0, stats.TotalWrites);
            Assert.Equal(0, stats.TotalBytesWritten);
            Assert.True(stats.IsHealthy);
        }
        
        [Fact]
        public void ResetStats_ShouldClearStats()
        {
            // Act
            _audioDataFlow.ResetStats();
            var stats = _audioDataFlow.GetStats();
            
            // Assert
            Assert.Equal(0, stats.TotalWrites);
            Assert.Equal(0, stats.TotalBytesWritten);
            Assert.True(stats.IsHealthy);
        }
        
        [Fact]
        public async Task WriteAudioDataAsync_ShouldTriggerDataWrittenEvent()
        {
            // Arrange
            var outputPath = "test.wav";
            var audioData = new byte[] { 1, 2, 3, 4 };
            
            _mockEncoder.Setup(x => x.InitializeAsync(_testAudioFormat, outputPath))
                .ReturnsAsync(true);
            _mockEncoder.Setup(x => x.WriteAudioFrameAsync(It.IsAny<AudioFrame>()))
                .ReturnsAsync(true);
            
            await _audioDataFlow.InitializeAsync(_testAudioFormat, outputPath);
            
            DataWrittenEventArgs? receivedArgs = null;
            _audioDataFlow.DataWritten += (s, e) => receivedArgs = e;
            
            // Act
            await _audioDataFlow.WriteAudioDataAsync(audioData, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.NotNull(receivedArgs);
            Assert.Equal(AudioSource.RTP_Incoming, receivedArgs.Source);
            Assert.Equal(4, receivedArgs.BytesWritten);
            Assert.True(receivedArgs.Success);
        }
    }
}