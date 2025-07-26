using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{    
    public class AudioRecordingException : Exception
    {        
        public RecordingErrorCode ErrorCode { get; }
        
        public AudioRecordingException(RecordingErrorCode errorCode, string message) 
            : base(message)
        {
            ErrorCode = errorCode;
        }
        
        public AudioRecordingException(RecordingErrorCode errorCode, string message, Exception innerException) 
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
    
    
    public class RecordingErrorHandler
    {
        private readonly ILogger _logger;
        
        public RecordingErrorHandler(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
                
        public async Task<bool> HandleEncodingErrorAsync(Exception ex, AudioFrame frame)
        {
            _logger.LogError(ex, "Encoding error occurred");
            
            // 尝试降级到更简单的格式
            try
            {
                // 简化处理：记录错误但继续
                return true;
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Failed to recover from encoding error");
                return false;
            }
        }
                
        public async Task<bool> HandleStorageErrorAsync(Exception ex, string filePath)
        {
            _logger.LogError(ex, $"Storage error for file: {filePath}");
            
            try
            {
                // 检查磁盘空间
                var drive = new DriveInfo(Path.GetPathRoot(filePath) ?? "C:");
                if (drive.AvailableFreeSpace < 100 * 1024 * 1024) // 100MB
                {
                    _logger.LogWarning("Low disk space detected");
                }
                
                return true;
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Failed to recover from storage error");
                return false;
            }
        }
    }
}