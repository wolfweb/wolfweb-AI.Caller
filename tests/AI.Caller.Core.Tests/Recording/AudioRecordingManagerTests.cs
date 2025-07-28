using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioRecordingManagerTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly AudioRecorder _audioRecorder;
        private readonly AudioMixer _audioMixer;
        private readonly Mock<IAudioEncoderFactory> _mockEncoderFactory;
        private readonly RecordingFileManager _fileManager;
        private readonly AudioFormatConverter _formatConverter;
        private readonly Mock<IAudioQualityMonitor> _mockQualityMonitor;
        private readonly Mock<IAudioDiagnostics> _mockDiagnostics;
        private readonly Mock<IAudioErrorRecoveryManager> _mockErrorRecovery;
        private readonly AudioRecordingManager _recordingManager;
        private readonly RecordingOptions _defaultOptions;
        private readonly string _testDirectory;
        
        public AudioRecordingManagerTests()
        {
            _mockLogger = new Mock<ILogger>();
            _testDirectory = Path.Combine(Path.GetTempPath(), "AudioRecordingManagerTests", Guid.NewGuid().ToString());
            
            // Create real instances for testing
            _audioRecorder = new AudioRecorder(_mockLogger.Object);
            _audioMixer = new AudioMixer(_mockLogger.Object);
            
            _mockEncoderFactory = new Mock<IAudioEncoderFactory>();
            
            var storageOptions = new RecordingStorageOptions
            {
                OutputDirectory = _testDirectory,
                FileNameTemplate = "test_{timestamp}",
                MinFreeSpaceGB = 0.001 // Very small for testing
            };
            _fileManager = new RecordingFileManager(storageOptions, _mockLogger.Object);
            
            _formatConverter = new AudioFormatConverter(_mockLogger.Object);
            
            // Create mock dependencies
            _mockQualityMonitor = new Mock<IAudioQualityMonitor>();
            _mockDiagnostics = new Mock<IAudioDiagnostics>();
            _mockErrorRecovery = new Mock<IAudioErrorRecoveryManager>();
            
            _recordingManager = new AudioRecordingManager(
                _audioRecorder,
                _audioMixer,
                _mockEncoderFactory.Object,
                _fileManager,
                _formatConverter,
                _mockQualityMonitor.Object,
                _mockDiagnostics.Object,
                _mockErrorRecovery.Object,
                _mockLogger.Object
            );
            
            _defaultOptions = new RecordingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1,
                BitRate = 64000,
                OutputDirectory = _testDirectory,
                FileNameTemplate = "test_{timestamp}",
                MaxDuration = TimeSpan.FromMinutes(10)
            };
        }
        
        [Fact]
        public void Constructor_WithValidParameters_ShouldInitialize()
        {
            // Assert
            Assert.NotNull(_recordingManager);
            Assert.False(_recordingManager.IsRecording);
            Assert.Equal(RecordingState.Idle, _recordingManager.CurrentStatus.State);
            Assert.Equal(TimeSpan.Zero, _recordingManager.RecordingDuration);
        }
        
        [Fact]
        public void Constructor_WithNullAudioRecorder_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioRecordingManager(
                null!,
                _audioMixer,
                _mockEncoderFactory.Object,
                _fileManager,
                _formatConverter,
                _mockQualityMonitor.Object,
                _mockDiagnostics.Object,
                _mockErrorRecovery.Object,
                _mockLogger.Object
            ));
        }
        
        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioRecordingManager(
                _audioRecorder,
                _audioMixer,
                _mockEncoderFactory.Object,
                _fileManager,
                _formatConverter,
                _mockQualityMonitor.Object,
                _mockDiagnostics.Object,
                _mockErrorRecovery.Object,
                null!
            ));
        }
        
        [Fact]
        public async Task StartRecordingAsync_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _recordingManager.StartRecordingAsync(null!));
        }
        
        [Fact]
        public async Task StartRecordingAsync_WithInvalidOptions_ShouldReturnFalse()
        {
            // Arrange
            var invalidOptions = new RecordingOptions
            {
                SampleRate = 0, // Invalid sample rate
                Channels = 1,
                OutputDirectory = "./test"
            };
            
            // Act
            var result = await _recordingManager.StartRecordingAsync(invalidOptions);
            
            // Assert
            Assert.False(result);
            Assert.Equal(RecordingState.Error, _recordingManager.CurrentStatus.State);
        }
        
        [Fact]
        public async Task StartRecordingAsync_WithValidOptions_ShouldReturnTrue()
        {
            // Act
            var result = await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Assert
            Assert.True(result);
            Assert.True(_recordingManager.IsRecording);
            Assert.Equal(RecordingState.Recording, _recordingManager.CurrentStatus.State);
        }
        
        [Fact]
        public async Task StartRecordingAsync_WhenAlreadyRecording_ShouldReturnFalse()
        {
            // Arrange
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Act
            var result = await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task StopRecordingAsync_WhenNotRecording_ShouldReturnNull()
        {
            // Act
            var result = await _recordingManager.StopRecordingAsync();
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public async Task StopRecordingAsync_WhenRecording_ShouldReturnFilePath()
        {
            // Arrange
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Act
            var result = await _recordingManager.StopRecordingAsync();
            
            // Assert
            Assert.NotNull(result);
            Assert.False(_recordingManager.IsRecording);
            Assert.Equal(RecordingState.Completed, _recordingManager.CurrentStatus.State);
        }
        
        [Fact]
        public async Task PauseRecordingAsync_WhenNotRecording_ShouldReturnFalse()
        {
            // Act
            var result = await _recordingManager.PauseRecordingAsync();
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task PauseRecordingAsync_WhenRecording_ShouldReturnTrue()
        {
            // Arrange
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Act
            var result = await _recordingManager.PauseRecordingAsync();
            
            // Assert
            Assert.True(result);
            Assert.Equal(RecordingState.Paused, _recordingManager.CurrentStatus.State);
        }
        
        [Fact]
        public async Task ResumeRecordingAsync_WhenNotPaused_ShouldReturnFalse()
        {
            // Act
            var result = await _recordingManager.ResumeRecordingAsync();
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task ResumeRecordingAsync_WhenPaused_ShouldReturnTrue()
        {
            // Arrange
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            await _recordingManager.PauseRecordingAsync();
            
            // Act
            var result = await _recordingManager.ResumeRecordingAsync();
            
            // Assert
            Assert.True(result);
            Assert.Equal(RecordingState.Recording, _recordingManager.CurrentStatus.State);
        }
        
        [Fact]
        public async Task CancelRecordingAsync_WhenRecording_ShouldReturnTrue()
        {
            // Arrange
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Act
            var result = await _recordingManager.CancelRecordingAsync();
            
            // Assert
            Assert.True(result);
            Assert.Equal(RecordingState.Cancelled, _recordingManager.CurrentStatus.State);
        }
        
        [Fact]
        public void CurrentStatus_ShouldReturnClonedStatus()
        {
            // Act
            var status1 = _recordingManager.CurrentStatus;
            var status2 = _recordingManager.CurrentStatus;
            
            // Assert
            Assert.NotSame(status1, status2);
            Assert.Equal(status1.State, status2.State);
        }
        
        [Fact]
        public async Task StatusChanged_Event_ShouldBeTriggered()
        {
            // Arrange
            RecordingStatusEventArgs? eventArgs = null;
            _recordingManager.StatusChanged += (sender, args) => eventArgs = args;
            
            // Act
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(RecordingState.Recording, eventArgs.Status.State);
        }
        
        [Fact]
        public async Task ProgressUpdated_Event_ShouldBeTriggered()
        {
            // Arrange
            RecordingProgressEventArgs? eventArgs = null;
            _recordingManager.ProgressUpdated += (sender, args) => eventArgs = args;
            
            await _recordingManager.StartRecordingAsync(_defaultOptions);
            
            // Act - Wait for progress update
            await Task.Delay(1100); // Wait for timer to trigger
            
            // Assert
            Assert.NotNull(eventArgs);
        }
        
        [Fact]
        public async Task ErrorOccurred_Event_ShouldBeTriggered()
        {
            // Arrange
            var invalidOptions = new RecordingOptions
            {
                SampleRate = 0 // Invalid
            };
            
            RecordingErrorEventArgs? eventArgs = null;
            _recordingManager.ErrorOccurred += (sender, args) => eventArgs = args;
            
            // Act
            await _recordingManager.StartRecordingAsync(invalidOptions);
            
            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(RecordingErrorCode.ConfigurationError, eventArgs.ErrorCode);
        }
        
        [Fact]
        public void Dispose_WhenCalled_ShouldNotThrow()
        {
            // Act & Assert
            _recordingManager.Dispose(); // Should not throw
        }
        
        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _recordingManager.Dispose();
            _recordingManager.Dispose(); // Should not throw
        }
        
        [Fact]
        public async Task StartRecordingAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _recordingManager.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _recordingManager.StartRecordingAsync(_defaultOptions));
        }
        
        [Fact]
        public async Task StopRecordingAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _recordingManager.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _recordingManager.StopRecordingAsync());
        }
        
        [Fact]
        public async Task PauseRecordingAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _recordingManager.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _recordingManager.PauseRecordingAsync());
        }
        
        [Fact]
        public async Task ResumeRecordingAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _recordingManager.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _recordingManager.ResumeRecordingAsync());
        }
        
        [Fact]
        public async Task CancelRecordingAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _recordingManager.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _recordingManager.CancelRecordingAsync());
        }
        
        public void Dispose()
        {
            try
            {
                _recordingManager?.Dispose();
                
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}