using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Core;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Net;
using System.Net;

namespace AI.Caller.Phone.Services
{
    /// <summary>
    /// 音频流录音服务 - 通过事件订阅方式录制音频，不侵入SIPClient
    /// </summary>
    public class AudioStreamRecordingService : ISimpleRecordingService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<AudioStreamRecordingService> _logger;
        private readonly string _recordingsPath;
        private readonly ConcurrentDictionary<string, RecordingSession> _activeSessions;
        private readonly ApplicationContext _applicationContext;

        public AudioStreamRecordingService(
            AppDbContext dbContext,
            ILogger<AudioStreamRecordingService> logger,
            IConfiguration configuration,
            ApplicationContext applicationContext)
        {
            _dbContext = dbContext;
            _logger = logger;
            _recordingsPath = configuration.GetValue<string>("RecordingsPath") ?? "recordings";
            _activeSessions = new ConcurrentDictionary<string, RecordingSession>();
            _applicationContext = applicationContext;

            Directory.CreateDirectory(_recordingsPath);
        }

        public async Task<bool> StartRecordingAsync(string sipUsername)
        {
            try
            {
                if (_activeSessions.ContainsKey(sipUsername))
                {
                    _logger.LogWarning($"用户 {sipUsername} 已经在录音中");
                    return false;
                }

                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user == null)
                {
                    _logger.LogError($"未找到用户: {sipUsername}");
                    return false;
                }

                // 获取SIPClient但不修改它
                if (!_applicationContext.SipClients.TryGetValue(sipUsername, out var sipClient))
                {
                    _logger.LogError($"未找到用户 {sipUsername} 的SIP客户端");
                    return false;
                }

                if (!sipClient.IsCallActive)
                {
                    _logger.LogWarning($"用户 {sipUsername} 没有活跃的通话");
                    return false;
                }

                // 创建录音会话
                var session = await CreateRecordingSessionAsync(user, sipUsername, sipClient);
                if (session == null)
                {
                    return false;
                }

                _activeSessions.TryAdd(sipUsername, session);
                _logger.LogInformation($"开始录音 - 用户: {sipUsername}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"开始录音失败 - 用户: {sipUsername}");
                return false;
            }
        }

        public async Task<bool> StopRecordingAsync(string sipUsername)
        {
            try
            {
                if (!_activeSessions.TryRemove(sipUsername, out var session))
                {
                    _logger.LogWarning($"用户 {sipUsername} 没有活动的录音");
                    return false;
                }

                await session.StopAsync();
                _logger.LogInformation($"停止录音 - 用户: {sipUsername}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"停止录音失败 - 用户: {sipUsername}");
                return false;
            }
        }

        private async Task<RecordingSession?> CreateRecordingSessionAsync(User user, string sipUsername, SIPClient sipClient)
        {
            try
            {
                var fileName = $"recording_{sipUsername}_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                var filePath = Path.Combine(_recordingsPath, fileName);

                var recording = new Recording
                {
                    UserId = user.Id,
                    SipUsername = sipUsername,
                    StartTime = DateTime.UtcNow,
                    FilePath = filePath,
                    Status = RecordingStatus.Recording
                };

                _dbContext.Recordings.Add(recording);
                await _dbContext.SaveChangesAsync();

                return new RecordingSession(recording, sipClient, _dbContext, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"创建录音会话失败 - 用户: {sipUsername}");
                return null;
            }
        }

        // 其他接口方法实现...
        public async Task<List<Recording>> GetRecordingsAsync(int userId)
        {
            try
            {
                return await _dbContext.Recordings
                    .Where(r => r.UserId == userId)
                    .OrderByDescending(r => r.StartTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取录音列表失败 - 用户ID: {userId}");
                return new List<Recording>();
            }
        }

        public async Task<bool> DeleteRecordingAsync(int recordingId, int userId)
        {
            try
            {
                var recording = await _dbContext.Recordings
                    .FirstOrDefaultAsync(r => r.Id == recordingId && r.UserId == userId);

                if (recording == null) return false;

                if (File.Exists(recording.FilePath))
                {
                    File.Delete(recording.FilePath);
                }

                _dbContext.Recordings.Remove(recording);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"删除录音失败 - ID: {recordingId}");
                return false;
            }
        }

        public async Task<bool> IsAutoRecordingEnabledAsync(int userId)
        {
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                return user?.AutoRecording ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"检查自动录音设置失败 - 用户ID: {userId}");
                return false;
            }
        }

        public async Task<bool> SetAutoRecordingAsync(int userId, bool enabled)
        {
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null) return false;

                user.AutoRecording = enabled;
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"设置自动录音失败 - 用户ID: {userId}");
                return false;
            }
        }

        public async Task<RecordingStatus?> GetRecordingStatusAsync(string sipUsername)
        {
            try
            {
                if (_activeSessions.TryGetValue(sipUsername, out var session))
                {
                    return session.Recording.Status;
                }

                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user != null)
                {
                    var latestRecording = await _dbContext.Recordings
                        .Where(r => r.UserId == user.Id)
                        .OrderByDescending(r => r.StartTime)
                        .FirstOrDefaultAsync();

                    return latestRecording?.Status;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取录音状态失败 - 用户: {sipUsername}");
                return null;
            }
        }
    }

    /// <summary>
    /// 录音会话 - 独立管理单个录音的生命周期
    /// </summary>
    internal class RecordingSession : IDisposable
    {
        public Recording Recording { get; }
        
        private readonly SIPClient _sipClient;
        private readonly AppDbContext _dbContext;
        private readonly ILogger _logger;
        private readonly AudioRecorder _audioRecorder;
        private bool _disposed = false;

        public RecordingSession(Recording recording, SIPClient sipClient, AppDbContext dbContext, ILogger logger)
        {
            Recording = recording;
            _sipClient = sipClient;
            _dbContext = dbContext;
            _logger = logger;
            _audioRecorder = new AudioRecorder(recording.FilePath);

            // 订阅音频事件 - 这里是关键，我们通过事件订阅而不是修改SIPClient
            SubscribeToAudioEvents();
        }

        private void SubscribeToAudioEvents()
        {
            // 订阅双向音频数据事件
            _sipClient.AudioDataReceived += OnAudioPacketReceived; // 对方的声音（SIP → WebRTC）
            _sipClient.AudioDataSent += OnAudioPacketSent;         // 本地用户的声音（WebRTC → SIP）
            
            // 订阅通话结束事件，自动停止录音
            _sipClient.CallEnded += OnCallEnded;
        }

        private void UnsubscribeFromAudioEvents()
        {
            _sipClient.AudioDataReceived -= OnAudioPacketReceived;
            _sipClient.AudioDataSent -= OnAudioPacketSent;
            _sipClient.CallEnded -= OnCallEnded;
        }

        private void OnAudioPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            try
            {
                if (mediaType == SDPMediaTypesEnum.audio && rtpPacket?.Payload != null && rtpPacket.Payload.Length > 0)
                {
                    // 标记为接收的音频（对方的声音）
                    _audioRecorder.WriteAudioData(rtpPacket.Payload, AudioDirection.Received);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "录音写入接收音频数据失败");
            }
        }

        private void OnAudioPacketSent(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            try
            {
                if (mediaType == SDPMediaTypesEnum.audio && rtpPacket?.Payload != null && rtpPacket.Payload.Length > 0)
                {
                    // 标记为发送的音频（本地用户的声音）
                    _audioRecorder.WriteAudioData(rtpPacket.Payload, AudioDirection.Sent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "录音写入发送音频数据失败");
            }
        }

        private void OnCallEnded(SIPClient sipClient)
        {
            // 通话结束时自动停止录音
            _ = Task.Run(async () =>
            {
                try
                {
                    await StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "通话结束时自动停止录音失败");
                }
            });
        }

        public async Task StopAsync()
        {
            if (_disposed) return;

            try
            {
                // 取消事件订阅
                UnsubscribeFromAudioEvents();

                // 完成音频文件
                await _audioRecorder.FinalizeAsync();

                // 更新数据库记录
                Recording.EndTime = DateTime.UtcNow;
                Recording.Duration = Recording.EndTime.Value - Recording.StartTime;
                Recording.Status = RecordingStatus.Completed;

                if (File.Exists(Recording.FilePath))
                {
                    var fileInfo = new FileInfo(Recording.FilePath);
                    Recording.FileSize = fileInfo.Length;
                }

                _dbContext.Recordings.Update(Recording);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止录音会话失败");
                Recording.Status = RecordingStatus.Failed;
                _dbContext.Recordings.Update(Recording);
                await _dbContext.SaveChangesAsync();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                UnsubscribeFromAudioEvents();
                _audioRecorder?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 音频方向枚举
    /// </summary>
    internal enum AudioDirection
    {
        Received, // 接收的音频（对方说话）
        Sent      // 发送的音频（本地用户说话）
    }

    /// <summary>
    /// 音频录制器 - 负责将音频数据写入WAV文件
    /// </summary>
    internal class AudioRecorder : IDisposable
    {
        private readonly string _filePath;
        private readonly FileStream _fileStream;
        private readonly List<byte> _audioBuffer;
        private bool _disposed = false;

        public AudioRecorder(string filePath)
        {
            _filePath = filePath;
            _audioBuffer = new List<byte>();
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            
            // 写入WAV文件头（占位符，稍后更新）
            WriteWavHeader(0);
        }

        public void WriteAudioData(byte[] rtpPayload, AudioDirection direction)
        {
            if (_disposed || rtpPayload == null || rtpPayload.Length == 0) return;

            // 将RTP payload转换为PCM音频数据
            // 假设是G.711 μ-law编码，需要解码为16位PCM
            var pcmData = DecodeG711MuLaw(rtpPayload);
            
            lock (_audioBuffer)
            {
                // 混合双向音频数据
                // 在实际实现中，可能需要更复杂的混音算法
                _audioBuffer.AddRange(pcmData);
            }
        }

        /// <summary>
        /// 解码G.711 μ-law音频数据为16位PCM
        /// </summary>
        private byte[] DecodeG711MuLaw(byte[] muLawData)
        {
            var pcmData = new byte[muLawData.Length * 2]; // 16位PCM是8位μ-law的2倍
            
            for (int i = 0; i < muLawData.Length; i++)
            {
                // G.711 μ-law解码算法
                var muLawByte = muLawData[i];
                var sign = (muLawByte & 0x80) != 0;
                var exponent = (muLawByte >> 4) & 0x07;
                var mantissa = muLawByte & 0x0F;
                
                var sample = (short)(mantissa << (exponent + 3));
                if (exponent > 0)
                    sample += (short)(0x84 << exponent);
                
                if (sign)
                    sample = (short)-sample;
                
                // 写入16位PCM数据（小端序）
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            
            return pcmData;
        }

        public async Task FinalizeAsync()
        {
            if (_disposed) return;

            try
            {
                // 写入所有音频数据
                byte[] audioDataToWrite;
                lock (_audioBuffer)
                {
                    audioDataToWrite = _audioBuffer.ToArray();
                }

                if (audioDataToWrite.Length > 0)
                {
                    await _fileStream.WriteAsync(audioDataToWrite);
                }

                // 更新WAV文件头
                _fileStream.Seek(0, SeekOrigin.Begin);
                WriteWavHeader(audioDataToWrite.Length);
                
                await _fileStream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"完成录音文件时发生错误: {ex.Message}");
            }
        }

        private void WriteWavHeader(int audioDataSize)
        {
            var header = new byte[44];
            var fileSize = 36 + audioDataSize;

            // RIFF header
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, header, 0, 4);
            BitConverter.GetBytes(fileSize).CopyTo(header, 4);
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, header, 8, 4);

            // fmt chunk
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, header, 12, 4);
            BitConverter.GetBytes(16).CopyTo(header, 16); // chunk size
            BitConverter.GetBytes((short)1).CopyTo(header, 20); // audio format (PCM)
            BitConverter.GetBytes((short)1).CopyTo(header, 22); // channels (mono)
            BitConverter.GetBytes(8000).CopyTo(header, 24); // sample rate
            BitConverter.GetBytes(16000).CopyTo(header, 28); // byte rate
            BitConverter.GetBytes((short)2).CopyTo(header, 32); // block align
            BitConverter.GetBytes((short)16).CopyTo(header, 34); // bits per sample

            // data chunk
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, header, 36, 4);
            BitConverter.GetBytes(audioDataSize).CopyTo(header, 40);

            _fileStream.Write(header, 0, 44);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _fileStream?.Dispose();
                _disposed = true;
            }
        }
    }
}