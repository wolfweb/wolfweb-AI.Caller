using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class RecordingFileManagerTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly RecordingStorageOptions _defaultOptions;
        private readonly string _testDirectory;
        
        public RecordingFileManagerTests()
        {
            _mockLogger = new Mock<ILogger>();
            _testDirectory = Path.Combine(Path.GetTempPath(), "RecordingFileManagerTests", Guid.NewGuid().ToString());
            _defaultOptions = new RecordingStorageOptions
            {
                OutputDirectory = _testDirectory,
                FileNameTemplate = "{timestamp}_{caller}_{duration}",
                MinFreeSpaceGB = 0.1,
                AutoCleanup = new AutoCleanupOptions
                {
                    Enabled = true,
                    RetentionDays = 30
                }
            };
        }
        
        [Fact]
        public void Constructor_WithValidParameters_ShouldInitialize()
        {
            // Arrange & Act
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            
            // Assert
            Assert.NotNull(manager);
            Assert.True(Directory.Exists(_testDirectory));
        }
        
        [Fact]
        public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new RecordingFileManager(null!, _mockLogger.Object));
        }
        
        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new RecordingFileManager(_defaultOptions, null!));
        }
        
        [Fact]
        public void Constructor_WithInvalidOutputDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidOptions = new RecordingStorageOptions { OutputDirectory = "" };
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new RecordingFileManager(invalidOptions, _mockLogger.Object));
        }
        
        [Fact]
        public void Constructor_WithInvalidFileNameTemplate_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidOptions = new RecordingStorageOptions 
            { 
                OutputDirectory = _testDirectory,
                FileNameTemplate = ""
            };
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new RecordingFileManager(invalidOptions, _mockLogger.Object));
        }
        
        [Fact]
        public void GenerateFileName_WithValidMetadata_ShouldReturnFormattedName()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var metadata = new RecordingMetadata
            {
                CallerNumber = "1234567890",
                CalleeNumber = "0987654321",
                StartTime = new DateTime(2024, 1, 1, 10, 30, 0),
                EndTime = new DateTime(2024, 1, 1, 10, 35, 0),
                AudioCodec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1
            };
            
            // Act
            var fileName = manager.GenerateFileName(metadata);
            
            // Assert
            Assert.NotNull(fileName);
            Assert.True(fileName.EndsWith(".wav"));
            Assert.Contains("1234567890", fileName);
        }
        
        [Fact]
        public async Task CreateRecordingFileAsync_WithValidParameters_ShouldCreateFile()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var fileName = "test_recording.wav";
            var format = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            
            // Act
            var filePath = await manager.CreateRecordingFileAsync(fileName, format);
            
            // Assert
            Assert.NotNull(filePath);
            Assert.True(File.Exists(filePath));
            Assert.True(filePath.EndsWith(fileName));
        }
        
        [Fact]
        public async Task CreateRecordingFileAsync_WithEmptyFileName_ShouldThrowArgumentException()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var format = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                manager.CreateRecordingFileAsync("", format));
        }
        
        [Fact]
        public async Task SaveMetadataAsync_WithValidData_ShouldSaveMetadata()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var filePath = Path.Combine(_testDirectory, "test.wav");
            var metadata = new RecordingMetadata
            {
                CallerNumber = "1234567890",
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddMinutes(5),
                AudioCodec = AudioCodec.PCM_WAV
            };
            
            // Act
            var result = await manager.SaveMetadataAsync(filePath, metadata);
            
            // Assert
            Assert.True(result);
            
            var metadataPath = Path.Combine(_testDirectory, "test.metadata.json");
            Assert.True(File.Exists(metadataPath));
        }
        
        [Fact]
        public async Task LoadMetadataAsync_WithExistingMetadata_ShouldReturnMetadata()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var filePath = Path.Combine(_testDirectory, "test.wav");
            var originalMetadata = new RecordingMetadata
            {
                CallerNumber = "1234567890",
                CalleeNumber = "0987654321",
                StartTime = new DateTime(2024, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2024, 1, 1, 10, 5, 0),
                AudioCodec = AudioCodec.MP3,
                SampleRate = 44100,
                Channels = 2
            };
            
            await manager.SaveMetadataAsync(filePath, originalMetadata);
            
            // Act
            var loadedMetadata = await manager.LoadMetadataAsync(filePath);
            
            // Assert
            Assert.NotNull(loadedMetadata);
            Assert.Equal(originalMetadata.CallerNumber, loadedMetadata.CallerNumber);
            Assert.Equal(originalMetadata.CalleeNumber, loadedMetadata.CalleeNumber);
            Assert.Equal(originalMetadata.AudioCodec, loadedMetadata.AudioCodec);
            Assert.Equal(originalMetadata.SampleRate, loadedMetadata.SampleRate);
            Assert.Equal(originalMetadata.Channels, loadedMetadata.Channels);
        }
        
        [Fact]
        public async Task LoadMetadataAsync_WithNonExistentFile_ShouldReturnNull()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var filePath = Path.Combine(_testDirectory, "nonexistent.wav");
            
            // Act
            var metadata = await manager.LoadMetadataAsync(filePath);
            
            // Assert
            Assert.Null(metadata);
        }
        
        [Fact]
        public async Task GetRecordingHistoryAsync_WithExistingFiles_ShouldReturnRecordings()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var format = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            
            // Create test files
            var file1 = await manager.CreateRecordingFileAsync("recording1.wav", format);
            var file2 = await manager.CreateRecordingFileAsync("recording2.mp3", format);
            
            // Add some content to files
            await File.WriteAllTextAsync(file1, "test content 1");
            await File.WriteAllTextAsync(file2, "test content 2");
            
            // Act
            var recordings = await manager.GetRecordingHistoryAsync();
            
            // Assert
            Assert.NotNull(recordings);
            Assert.Equal(2, recordings.Length);
            Assert.Contains(recordings, r => r.FileName == "recording1.wav");
            Assert.Contains(recordings, r => r.FileName == "recording2.mp3");
        }
        
        [Fact]
        public async Task DeleteRecordingAsync_WithExistingFile_ShouldDeleteFile()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var format = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var filePath = await manager.CreateRecordingFileAsync("test_delete.wav", format);
            
            // Ensure file exists
            Assert.True(File.Exists(filePath));
            
            // Act
            var result = await manager.DeleteRecordingAsync(filePath);
            
            // Assert
            Assert.True(result);
            Assert.False(File.Exists(filePath));
        }
        
        [Fact]
        public async Task DeleteRecordingAsync_WithNonExistentFile_ShouldReturnFalse()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var filePath = Path.Combine(_testDirectory, "nonexistent.wav");
            
            // Act
            var result = await manager.DeleteRecordingAsync(filePath);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task DeleteRecordingAsync_WithMetadata_ShouldDeleteBothFiles()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var format = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var filePath = await manager.CreateRecordingFileAsync("test_with_metadata.wav", format);
            
            var metadata = new RecordingMetadata { CallerNumber = "1234567890" };
            await manager.SaveMetadataAsync(filePath, metadata);
            
            var metadataPath = Path.Combine(_testDirectory, "test_with_metadata.metadata.json");
            Assert.True(File.Exists(metadataPath));
            
            // Act
            var result = await manager.DeleteRecordingAsync(filePath, deleteMetadata: true);
            
            // Assert
            Assert.True(result);
            Assert.False(File.Exists(filePath));
            Assert.False(File.Exists(metadataPath));
        }
        
        [Fact]
        public async Task CleanupExpiredRecordingsAsync_WithExpiredFiles_ShouldDeleteThem()
        {
            // Arrange
            var options = new RecordingStorageOptions
            {
                OutputDirectory = _testDirectory,
                FileNameTemplate = "{timestamp}_{caller}",
                AutoCleanup = new AutoCleanupOptions
                {
                    Enabled = true,
                    RetentionDays = 1 // 1 day retention
                }
            };
            
            var manager = new RecordingFileManager(options, _mockLogger.Object);
            var format = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            
            // Create a file and modify its creation time to be old
            var filePath = await manager.CreateRecordingFileAsync("old_recording.wav", format);
            var fileInfo = new FileInfo(filePath);
            fileInfo.CreationTime = DateTime.Now.AddDays(-2); // 2 days old
            
            // Act
            var deletedCount = await manager.CleanupExpiredRecordingsAsync();
            
            // Assert
            Assert.Equal(1, deletedCount);
            Assert.False(File.Exists(filePath));
        }
        
        [Fact]
        public async Task CleanupExpiredRecordingsAsync_WithDisabledCleanup_ShouldReturnZero()
        {
            // Arrange
            var options = new RecordingStorageOptions
            {
                OutputDirectory = _testDirectory,
                AutoCleanup = new AutoCleanupOptions { Enabled = false }
            };
            
            var manager = new RecordingFileManager(options, _mockLogger.Object);
            
            // Act
            var deletedCount = await manager.CleanupExpiredRecordingsAsync();
            
            // Assert
            Assert.Equal(0, deletedCount);
        }
        
        [Fact]
        public void GetStorageInfo_ShouldReturnValidInfo()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            
            // Act
            var storageInfo = manager.GetStorageInfo();
            
            // Assert
            Assert.NotNull(storageInfo);
            Assert.Equal(_testDirectory, storageInfo.Directory);
            Assert.True(storageInfo.TotalSpace >= 0);
            Assert.True(storageInfo.FreeSpace >= 0);
        }
        
        [Fact]
        public void GenerateFileName_WithSpecialCharacters_ShouldSanitize()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var metadata = new RecordingMetadata
            {
                CallerNumber = "123<>:\"/\\|?*456",
                AudioCodec = AudioCodec.PCM_WAV
            };
            
            // Act
            var fileName = manager.GenerateFileName(metadata);
            var fileNameOnly = Path.GetFileName(fileName);
            
            // Assert
            Assert.NotNull(fileNameOnly);
            Assert.DoesNotContain("<", fileNameOnly);
            Assert.DoesNotContain(">", fileNameOnly);
            Assert.DoesNotContain("\"", fileNameOnly);
            Assert.DoesNotContain("|", fileNameOnly);
            Assert.DoesNotContain("?", fileNameOnly);
            Assert.DoesNotContain("*", fileNameOnly);
        }
        
        [Fact]
        public void GenerateFileName_WithDuplicateNames_ShouldCreateUniqueNames()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            var metadata = new RecordingMetadata
            {
                CallerNumber = "1234567890",
                AudioCodec = AudioCodec.PCM_WAV
            };
            
            // Create first file
            var fileName1 = manager.GenerateFileName(metadata);
            File.Create(fileName1).Dispose();
            
            // Act - Generate second file name
            var fileName2 = manager.GenerateFileName(metadata);
            var fileName2Only = Path.GetFileName(fileName2);
            
            // Assert
            Assert.NotEqual(fileName1, fileName2);
            Assert.Contains("_001", fileName2Only);
        }
        
        [Fact]
        public void Dispose_WhenCalled_ShouldNotThrow()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            
            // Act & Assert
            manager.Dispose(); // Should not throw
        }
        
        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            
            // Act & Assert
            manager.Dispose();
            manager.Dispose(); // Should not throw
        }
        
        [Fact]
        public void GenerateFileName_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            var manager = new RecordingFileManager(_defaultOptions, _mockLogger.Object);
            manager.Dispose();
            
            var metadata = new RecordingMetadata();
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => manager.GenerateFileName(metadata));
        }
        
        public void Dispose()
        {
            try
            {
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
    
    public class RecordingMetadataTests
    {
        [Fact]
        public void Duration_ShouldCalculateCorrectly()
        {
            // Arrange
            var metadata = new RecordingMetadata
            {
                StartTime = new DateTime(2024, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2024, 1, 1, 10, 5, 30)
            };
            
            // Act
            var duration = metadata.Duration;
            
            // Assert
            Assert.Equal(TimeSpan.FromMinutes(5.5), duration);
        }
        
        [Fact]
        public void Tags_ShouldInitializeAsEmptyList()
        {
            // Arrange & Act
            var metadata = new RecordingMetadata();
            
            // Assert
            Assert.NotNull(metadata.Tags);
            Assert.Empty(metadata.Tags);
        }
    }
    
    public class StorageInfoTests
    {
        [Fact]
        public void FreeSpacePercentage_ShouldCalculateCorrectly()
        {
            // Arrange
            var storageInfo = new StorageInfo
            {
                TotalSpace = 1000,
                FreeSpace = 250
            };
            
            // Act
            var percentage = storageInfo.FreeSpacePercentage;
            
            // Assert
            Assert.Equal(25.0, percentage);
        }
        
        [Fact]
        public void UsedSpacePercentage_ShouldCalculateCorrectly()
        {
            // Arrange
            var storageInfo = new StorageInfo
            {
                TotalSpace = 1000,
                UsedSpace = 750
            };
            
            // Act
            var percentage = storageInfo.UsedSpacePercentage;
            
            // Assert
            Assert.Equal(75.0, percentage);
        }
        
        [Fact]
        public void Percentages_WithZeroTotalSpace_ShouldReturnZero()
        {
            // Arrange
            var storageInfo = new StorageInfo
            {
                TotalSpace = 0,
                FreeSpace = 100,
                UsedSpace = 200
            };
            
            // Act & Assert
            Assert.Equal(0.0, storageInfo.FreeSpacePercentage);
            Assert.Equal(0.0, storageInfo.UsedSpacePercentage);
        }
    }
}