using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI.Caller.Core.Recording
{    
    public class RecordingFileManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly RecordingStorageOptions _storageOptions;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
                
        public event EventHandler<FileCreatedEventArgs>? FileCreated;
                
        public event EventHandler<FileDeletedEventArgs>? FileDeleted;
                
        public event EventHandler<StorageWarningEventArgs>? StorageWarning;
        
        public RecordingFileManager(RecordingStorageOptions storageOptions, ILogger logger)
        {
            _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            ValidateStorageOptions();
            EnsureDirectoryExists(_storageOptions.OutputDirectory);
        }
                
        public string GenerateFileName(RecordingMetadata metadata)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            try
            {
                var fileName = ApplyNamingStrategy(metadata);
                var uniqueFileName = EnsureUniqueFileName(Path.Combine(_storageOptions.OutputDirectory, fileName));
                
                _logger.LogDebug($"Generated file name: {uniqueFileName}");
                return uniqueFileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating file name: {ex.Message}");
                throw;
            }
        }
                
        public async Task<string> CreateRecordingFileAsync(string fileName, AudioFormat format)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty", nameof(fileName));
                
            try
            {
                // 检查存储空间
                await CheckStorageSpaceAsync();
                
                var fullPath = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_storageOptions.OutputDirectory, fileName);
                var directory = Path.GetDirectoryName(fullPath);
                
                if (!string.IsNullOrEmpty(directory))
                {
                    EnsureDirectoryExists(directory);
                }
                
                // 创建空文件
                using (var fileStream = File.Create(fullPath))
                {
                    // 文件已创建
                }
                
                _logger.LogInformation($"Created recording file: {fullPath}");
                FileCreated?.Invoke(this, new FileCreatedEventArgs(fullPath, format));
                
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating recording file: {ex.Message}");
                throw;
            }
        }
                
        public async Task<bool> SaveMetadataAsync(string filePath, RecordingMetadata metadata)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
                
            try
            {
                var metadataPath = GetMetadataPath(filePath);
                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                await File.WriteAllTextAsync(metadataPath, metadataJson);
                
                _logger.LogDebug($"Saved metadata for: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving metadata for {filePath}: {ex.Message}");
                return false;
            }
        }
                
        public async Task<RecordingMetadata?> LoadMetadataAsync(string filePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            try
            {
                var metadataPath = GetMetadataPath(filePath);
                if (!File.Exists(metadataPath))
                    return null;
                    
                var metadataJson = await File.ReadAllTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize<RecordingMetadata>(metadataJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading metadata for {filePath}: {ex.Message}");
                return null;
            }
        }
                
        public async Task<RecordingInfo[]> GetRecordingHistoryAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            try
            {
                var recordings = new List<RecordingInfo>();
                var directory = new DirectoryInfo(_storageOptions.OutputDirectory);
                
                if (!directory.Exists)
                    return recordings.ToArray();
                
                var audioFiles = directory.GetFiles("*.*")
                    .Where(f => IsAudioFile(f.Extension))
                    .OrderByDescending(f => f.CreationTime);
                
                foreach (var file in audioFiles)
                {
                    var metadata = await LoadMetadataAsync(file.FullName);
                    var recordingInfo = new RecordingInfo
                    {
                        FilePath = file.FullName,
                        FileName = file.Name,
                        FileSize = file.Length,
                        CreatedTime = file.CreationTime,
                        ModifiedTime = file.LastWriteTime,
                        Metadata = metadata
                    };
                    
                    recordings.Add(recordingInfo);
                }
                
                _logger.LogDebug($"Found {recordings.Count} recording files");
                return recordings.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting recording history: {ex.Message}");
                return Array.Empty<RecordingInfo>();
            }
        }
                
        public async Task<bool> DeleteRecordingAsync(string filePath, bool deleteMetadata = true)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"File not found for deletion: {filePath}");
                    return false;
                }
                
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;
                
                File.Delete(filePath);
                
                if (deleteMetadata)
                {
                    var metadataPath = GetMetadataPath(filePath);
                    if (File.Exists(metadataPath))
                    {
                        File.Delete(metadataPath);
                    }
                }
                
                _logger.LogInformation($"Deleted recording file: {filePath}");
                FileDeleted?.Invoke(this, new FileDeletedEventArgs(filePath, fileSize));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting recording file {filePath}: {ex.Message}");
                return false;
            }
        }
                
        public async Task<int> CleanupExpiredRecordingsAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            if (!_storageOptions.AutoCleanup.Enabled)
                return 0;
                
            try
            {
                var recordings = await GetRecordingHistoryAsync();
                var expiredRecordings = recordings
                    .Where(r => DateTime.Now - r.CreatedTime > TimeSpan.FromDays(_storageOptions.AutoCleanup.RetentionDays))
                    .ToArray();
                
                var deletedCount = 0;
                foreach (var recording in expiredRecordings)
                {
                    if (await DeleteRecordingAsync(recording.FilePath))
                    {
                        deletedCount++;
                    }
                }
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation($"Cleaned up {deletedCount} expired recording files");
                }
                
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during cleanup: {ex.Message}");
                return 0;
            }
        }
                
        public StorageInfo GetStorageInfo()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFileManager));
                
            try
            {
                var directory = new DirectoryInfo(_storageOptions.OutputDirectory);
                var drive = new DriveInfo(directory.Root.FullName);
                
                var usedSpace = directory.Exists ? 
                    directory.GetFiles("*.*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
                
                return new StorageInfo
                {
                    TotalSpace = drive.TotalSize,
                    FreeSpace = drive.AvailableFreeSpace,
                    UsedSpace = usedSpace,
                    UsedByRecordings = usedSpace,
                    Directory = _storageOptions.OutputDirectory
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting storage info: {ex.Message}");
                return new StorageInfo
                {
                    Directory = _storageOptions.OutputDirectory,
                    TotalSpace = 0,
                    FreeSpace = 0,
                    UsedSpace = 0,
                    UsedByRecordings = 0
                };
            }
        }
                
        private string ApplyNamingStrategy(RecordingMetadata metadata)
        {
            var template = _storageOptions.FileNameTemplate;
            var fileName = template;
            
            // 替换占位符
            fileName = fileName.Replace("{timestamp}", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            fileName = fileName.Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
            fileName = fileName.Replace("{time}", DateTime.Now.ToString("HHmmss"));
            fileName = fileName.Replace("{caller}", SanitizeFileName(metadata.CallerNumber ?? "unknown"));
            fileName = fileName.Replace("{callee}", SanitizeFileName(metadata.CalleeNumber ?? "unknown"));
            fileName = fileName.Replace("{duration}", FormatDuration(metadata.Duration));
            fileName = fileName.Replace("{codec}", metadata.AudioCodec.ToString().ToLower());
            fileName = fileName.Replace("{samplerate}", metadata.SampleRate.ToString());
            fileName = fileName.Replace("{channels}", metadata.Channels.ToString());
            
            // 添加文件扩展名
            var extension = GetFileExtension(metadata.AudioCodec);
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                fileName += extension;
            }
            
            return fileName;
        }
                
        private string EnsureUniqueFileName(string basePath)
        {
            if (!File.Exists(basePath))
                return basePath;
                
            var directory = Path.GetDirectoryName(basePath) ?? "";
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(basePath);
            var extension = Path.GetExtension(basePath);
            
            var counter = 1;
            string uniquePath;
            
            do
            {
                var uniqueFileName = $"{fileNameWithoutExtension}_{counter:D3}{extension}";
                uniquePath = Path.Combine(directory, uniqueFileName);
                counter++;
            }
            while (File.Exists(uniquePath) && counter < 1000);
            
            if (counter >= 1000)
            {
                throw new InvalidOperationException("Unable to generate unique file name after 1000 attempts");
            }
            
            return uniquePath;
        }
                
        private async Task CheckStorageSpaceAsync()
        {
            var storageInfo = GetStorageInfo();
            var freeSpaceGB = storageInfo.FreeSpace / (1024.0 * 1024.0 * 1024.0);
            
            if (freeSpaceGB < _storageOptions.MinFreeSpaceGB)
            {
                var message = $"Low disk space: {freeSpaceGB:F2}GB free, minimum required: {_storageOptions.MinFreeSpaceGB}GB";
                _logger.LogWarning(message);
                StorageWarning?.Invoke(this, new StorageWarningEventArgs(storageInfo, message));
                
                if (_storageOptions.AutoCleanup.Enabled)
                {
                    _logger.LogInformation("Attempting automatic cleanup due to low disk space");
                    await CleanupExpiredRecordingsAsync();
                }
            }
        }
                
        private void EnsureDirectoryExists(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogDebug($"Created directory: {directory}");
            }
        }
                
        private string GetMetadataPath(string audioFilePath)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? "";
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(audioFilePath);
            return Path.Combine(directory, $"{fileNameWithoutExtension}.metadata.json");
        }
                
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }
                
        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return duration.ToString(@"hh\hmm\mss\s");
            else if (duration.TotalMinutes >= 1)
                return duration.ToString(@"mm\mss\s");
            else
                return duration.ToString(@"ss\s");
        }
                
        private string GetFileExtension(AudioCodec codec)
        {
            return codec switch
            {
                AudioCodec.PCM_WAV => ".wav",
                AudioCodec.MP3 => ".mp3",
                AudioCodec.AAC => ".m4a",
                AudioCodec.OPUS => ".opus",
                _ => ".audio"
            };
        }
                
        private bool IsAudioFile(string extension)
        {
            var audioExtensions = new[] { ".wav", ".mp3", ".m4a", ".opus", ".audio" };
            return audioExtensions.Contains(extension.ToLowerInvariant());
        }
                
        private void ValidateStorageOptions()
        {
            if (string.IsNullOrWhiteSpace(_storageOptions.OutputDirectory))
                throw new ArgumentException("Output directory cannot be null or empty");
                
            if (string.IsNullOrWhiteSpace(_storageOptions.FileNameTemplate))
                throw new ArgumentException("File name template cannot be null or empty");
                
            if (_storageOptions.MinFreeSpaceGB < 0)
                throw new ArgumentException("Minimum free space cannot be negative");
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            lock (_lockObject)
            {
                if (_disposed)
                    return;
                    
                _disposed = true;
                _logger.LogInformation("RecordingFileManager disposed");
            }
        }
    }
    
    
    public class RecordingStorageOptions
    {        
        public string OutputDirectory { get; set; } = "./recordings";
                
        public long MaxFileSize { get; set; }
                
        public string FileNameTemplate { get; set; } = "{timestamp}_{caller}_{duration}";
                
        public double MinFreeSpaceGB { get; set; } = 1.0;
                
        public AutoCleanupOptions AutoCleanup { get; set; } = new AutoCleanupOptions();
    }
        
    public class AutoCleanupOptions
    {        
        public bool Enabled { get; set; } = true;
                
        public int RetentionDays { get; set; } = 30;
    }
    
    
    public class RecordingMetadata
    {        
        public string? CallerNumber { get; set; }
                
        public string? CalleeNumber { get; set; }
                
        public DateTime StartTime { get; set; }
                
        public DateTime EndTime { get; set; }
                
        public TimeSpan Duration => EndTime - StartTime;
                
        public AudioCodec AudioCodec { get; set; }
                
        public int SampleRate { get; set; }
                
        public int Channels { get; set; }
                
        public long FileSize { get; set; }
                
        public AudioQuality Quality { get; set; }
                
        public string? Notes { get; set; }
                
        public List<string> Tags { get; set; } = new List<string>();
    }


    public class RecordingInfo
    {
        public string FilePath { get; set; } = "";

        public string FileName { get; set; } = "";

        public long FileSize { get; set; }

        public DateTime CreatedTime { get; set; }

        public DateTime ModifiedTime { get; set; }

        public RecordingMetadata? Metadata { get; set; }
    }
        
    public class StorageInfo
    {        
        public string Directory { get; set; } = "";
                
        public long TotalSpace { get; set; }
                
        public long FreeSpace { get; set; }
                
        public long UsedSpace { get; set; }
                
        public long UsedByRecordings { get; set; }
                
        public double FreeSpacePercentage => TotalSpace > 0 ? (double)FreeSpace / TotalSpace * 100 : 0;
                
        public double UsedSpacePercentage => TotalSpace > 0 ? (double)UsedSpace / TotalSpace * 100 : 0;
    }
        
    public class FileCreatedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public AudioFormat AudioFormat { get; }
        
        public FileCreatedEventArgs(string filePath, AudioFormat audioFormat)
        {
            FilePath = filePath;
            AudioFormat = audioFormat;
        }
    }
        
    public class FileDeletedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public long FileSize { get; }
        
        public FileDeletedEventArgs(string filePath, long fileSize)
        {
            FilePath = filePath;
            FileSize = fileSize;
        }
    }
        
    public class StorageWarningEventArgs : EventArgs
    {
        public StorageInfo StorageInfo { get; }
        public string Message { get; }
        
        public StorageWarningEventArgs(StorageInfo storageInfo, string message)
        {
            StorageInfo = storageInfo;
            Message = message;
        }
    }
}