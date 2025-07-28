using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 0字节文件检测器，检测和验证录音文件
    /// </summary>
    public class ZeroByteFileDetector
    {
        private readonly ILogger _logger;
        
        public ZeroByteFileDetector(ILogger<ZeroByteFileDetector> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// 验证录音文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>验证结果</returns>
        public async Task<FileValidationResult> ValidateRecordingFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return new FileValidationResult
                {
                    IsValid = false,
                    Issues = new List<string> { "File path is null or empty" }
                };
            }
            
            try
            {
                var result = new FileValidationResult();
                
                // 检查文件是否存在
                if (!File.Exists(filePath))
                {
                    result.IsValid = false;
                    result.Issues.Add("File does not exist");
                    return result;
                }
                
                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                
                // 检查文件大小
                if (fileInfo.Length == 0)
                {
                    result.IsValid = false;
                    result.Issues.Add("File is empty (0 bytes)");
                    _logger.LogWarning($"Detected 0-byte file: {filePath}");
                    return result;
                }
                
                // 检查文件扩展名
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension == ".wav")
                {
                    await ValidateWavFileAsync(filePath, result);
                }
                else if (extension == ".mp3")
                {
                    await ValidateMp3FileAsync(filePath, result);
                }
                else
                {
                    result.Issues.Add($"Unsupported file format: {extension}");
                }
                
                // 检查最小文件大小（至少应该有文件头）
                var minSize = extension == ".wav" ? 44 : 128; // WAV头44字节，MP3至少128字节
                if (fileInfo.Length < minSize)
                {
                    result.IsValid = false;
                    result.Issues.Add($"File too small ({fileInfo.Length} bytes), expected at least {minSize} bytes");
                }
                
                result.IsValid = result.Issues.Count == 0;
                
                if (result.IsValid)
                {
                    _logger.LogDebug($"File validation passed: {filePath} ({fileInfo.Length} bytes)");
                }
                else
                {
                    _logger.LogWarning($"File validation failed: {filePath} - {string.Join(", ", result.Issues)}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating file {filePath}: {ex.Message}");
                return new FileValidationResult
                {
                    IsValid = false,
                    Issues = new List<string> { $"Validation error: {ex.Message}" }
                };
            }
        }
        
        /// <summary>
        /// 验证WAV文件格式
        /// </summary>
        private async Task ValidateWavFileAsync(string filePath, FileValidationResult result)
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var buffer = new byte[44]; // WAV头部大小
                
                var bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead < 44)
                {
                    result.Issues.Add("WAV file header incomplete");
                    return;
                }
                
                // 检查RIFF标识
                var riffHeader = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
                if (riffHeader != "RIFF")
                {
                    result.Issues.Add("Invalid WAV file: missing RIFF header");
                    return;
                }
                
                // 检查WAVE标识
                var waveHeader = System.Text.Encoding.ASCII.GetString(buffer, 8, 4);
                if (waveHeader != "WAVE")
                {
                    result.Issues.Add("Invalid WAV file: missing WAVE header");
                    return;
                }
                
                // 检查fmt标识
                var fmtHeader = System.Text.Encoding.ASCII.GetString(buffer, 12, 4);
                if (fmtHeader != "fmt ")
                {
                    result.Issues.Add("Invalid WAV file: missing fmt header");
                    return;
                }
                
                // 获取文件大小信息
                var fileSizeFromHeader = BitConverter.ToUInt32(buffer, 4) + 8;
                var actualFileSize = (uint)new FileInfo(filePath).Length;
                
                if (Math.Abs((long)fileSizeFromHeader - actualFileSize) > 8) // 允许8字节误差
                {
                    result.Issues.Add($"WAV file size mismatch: header says {fileSizeFromHeader}, actual {actualFileSize}");
                }
                
                // 检查是否有数据块
                var hasDataChunk = await HasWavDataChunkAsync(fileStream);
                if (!hasDataChunk)
                {
                    result.Issues.Add("WAV file missing data chunk");
                }
                
                result.AudioFormat = ExtractWavFormatInfo(buffer);
                
            }
            catch (Exception ex)
            {
                result.Issues.Add($"WAV validation error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 检查WAV文件是否有数据块
        /// </summary>
        private async Task<bool> HasWavDataChunkAsync(FileStream fileStream)
        {
            try
            {
                fileStream.Seek(36, SeekOrigin.Begin); // 跳过标准WAV头部
                var buffer = new byte[8];
                
                while (fileStream.Position < fileStream.Length - 8)
                {
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, 8);
                    if (bytesRead < 8) break;
                    
                    var chunkId = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
                    if (chunkId == "data")
                    {
                        var dataSize = BitConverter.ToUInt32(buffer, 4);
                        return dataSize > 0;
                    }
                    
                    // 跳过当前块
                    var chunkSize = BitConverter.ToUInt32(buffer, 4);
                    fileStream.Seek(chunkSize, SeekOrigin.Current);
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 提取WAV格式信息
        /// </summary>
        private AudioFormat? ExtractWavFormatInfo(byte[] header)
        {
            try
            {
                var channels = BitConverter.ToUInt16(header, 22);
                var sampleRate = BitConverter.ToUInt32(header, 24);
                var bitsPerSample = BitConverter.ToUInt16(header, 34);
                
                return new AudioFormat((int)sampleRate, channels, bitsPerSample, AudioSampleFormat.PCM);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 验证MP3文件格式
        /// </summary>
        private async Task ValidateMp3FileAsync(string filePath, FileValidationResult result)
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var buffer = new byte[10];
                
                var bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead < 10)
                {
                    result.Issues.Add("MP3 file too small to contain valid header");
                    return;
                }
                
                // 检查ID3标签或MP3帧头
                if (buffer[0] == 'I' && buffer[1] == 'D' && buffer[2] == '3')
                {
                    // 有ID3标签，这是正常的
                }
                else if ((buffer[0] == 0xFF && (buffer[1] & 0xE0) == 0xE0))
                {
                    // MP3帧头
                }
                else
                {
                    result.Issues.Add("Invalid MP3 file: no valid header found");
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add($"MP3 validation error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 尝试从缓冲数据恢复文件
        /// </summary>
        /// <param name="filePath">目标文件路径</param>
        /// <param name="bufferedData">缓冲的音频数据</param>
        /// <param name="audioFormat">音频格式</param>
        /// <returns>是否成功恢复</returns>
        public async Task<bool> AttemptRecoveryAsync(string filePath, List<byte[]> bufferedData, AudioFormat audioFormat)
        {
            if (bufferedData == null || bufferedData.Count == 0)
            {
                _logger.LogWarning("No buffered data available for recovery");
                return false;
            }
            
            try
            {
                _logger.LogInformation($"Attempting to recover file: {filePath}");
                
                // 创建备份文件路径
                var backupPath = filePath + ".backup";
                if (File.Exists(filePath))
                {
                    File.Move(filePath, backupPath);
                }
                
                // 创建新的WAV文件
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                
                // 计算总数据大小
                var totalDataSize = bufferedData.Sum(data => data.Length);
                
                // 写入WAV头部
                await WriteWavHeaderAsync(fileStream, audioFormat, totalDataSize);
                
                // 写入音频数据
                foreach (var data in bufferedData)
                {
                    await fileStream.WriteAsync(data, 0, data.Length);
                }
                
                await fileStream.FlushAsync();
                
                // 验证恢复的文件
                var validationResult = await ValidateRecordingFileAsync(filePath);
                if (validationResult.IsValid)
                {
                    _logger.LogInformation($"File recovery successful: {filePath} ({totalDataSize} bytes)");
                    
                    // 删除备份文件
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    
                    return true;
                }
                else
                {
                    _logger.LogError($"File recovery failed validation: {string.Join(", ", validationResult.Issues)}");
                    
                    // 恢复备份文件
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, filePath);
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during file recovery: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 写入WAV文件头部
        /// </summary>
        private async Task WriteWavHeaderAsync(FileStream fileStream, AudioFormat audioFormat, int dataSize)
        {
            var header = new byte[44];
            var pos = 0;
            
            // RIFF头部
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, header, pos, 4);
            pos += 4;
            
            // 文件大小 - 8
            var fileSize = dataSize + 36;
            Array.Copy(BitConverter.GetBytes(fileSize), 0, header, pos, 4);
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
            Array.Copy(BitConverter.GetBytes((short)audioFormat.Channels), 0, header, pos, 2);
            pos += 2;
            
            // 采样率
            Array.Copy(BitConverter.GetBytes(audioFormat.SampleRate), 0, header, pos, 4);
            pos += 4;
            
            // 字节率
            Array.Copy(BitConverter.GetBytes(audioFormat.ByteRate), 0, header, pos, 4);
            pos += 4;
            
            // 块对齐
            Array.Copy(BitConverter.GetBytes((short)audioFormat.BlockAlign), 0, header, pos, 2);
            pos += 2;
            
            // 位深度
            Array.Copy(BitConverter.GetBytes((short)audioFormat.BitsPerSample), 0, header, pos, 2);
            pos += 2;
            
            // data块
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, header, pos, 4);
            pos += 4;
            
            // 数据大小
            Array.Copy(BitConverter.GetBytes(dataSize), 0, header, pos, 4);
            
            await fileStream.WriteAsync(header, 0, header.Length);
        }
    }
    
    /// <summary>
    /// 文件验证结果
    /// </summary>
    public class FileValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// 文件大小
        /// </summary>
        public long FileSize { get; set; }
        
        /// <summary>
        /// 音频格式信息
        /// </summary>
        public AudioFormat? AudioFormat { get; set; }
        
        /// <summary>
        /// 问题列表
        /// </summary>
        public List<string> Issues { get; set; } = new();
        
        /// <summary>
        /// 验证时间
        /// </summary>
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
        
        public override string ToString()
        {
            return $"Valid: {IsValid}, Size: {FileSize} bytes, Issues: {Issues.Count}";
        }
    }
}