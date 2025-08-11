using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Net;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// 音频流录音服务 - 通过事件订阅方式录制音频，不侵入SIPClient
    /// </summary>
    public class AudioStreamRecordingService : ISimpleRecordingService {
        private readonly string _recordingsPath;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<AudioStreamRecordingService> _logger;
        private readonly ConcurrentDictionary<string, RecordingSession> _activeSessions;
        private readonly ApplicationContext _applicationContext;

        public AudioStreamRecordingService(
            ILogger<AudioStreamRecordingService> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            ApplicationContext applicationContext) {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _recordingsPath = configuration.GetValue<string>("RecordingsPath") ?? "recordings";
            _activeSessions = new ConcurrentDictionary<string, RecordingSession>();
            _applicationContext = applicationContext;

            Directory.CreateDirectory(_recordingsPath);
        }

        public async Task<bool> StartRecordingAsync(string sipUsername) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (_activeSessions.ContainsKey(sipUsername)) {
                    _logger.LogWarning($"用户 {sipUsername} 已经在录音中");
                    return false;
                }

                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user == null) {
                    _logger.LogError($"未找到用户: {sipUsername}");
                    return false;
                }

                if (!_applicationContext.SipClients.TryGetValue(sipUsername, out var sipClient)) {
                    _logger.LogError($"未找到用户 {sipUsername} 的SIP客户端");
                    return false;
                }

                if (!sipClient.IsCallActive) {
                    _logger.LogWarning($"用户 {sipUsername} 没有活跃的通话");
                    return false;
                }

                var session = await CreateRecordingSessionAsync(user, sipUsername, sipClient);
                if (session == null) {
                    return false;
                }

                _activeSessions.TryAdd(sipUsername, session);
                _logger.LogInformation($"开始录音 - 用户: {sipUsername}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"开始录音失败 - 用户: {sipUsername}");
                return false;
            }
        }

        public async Task<bool> StopRecordingAsync(string sipUsername) {
            try {
                if (!_activeSessions.TryRemove(sipUsername, out var session)) {
                    _logger.LogWarning($"用户 {sipUsername} 没有活动的录音");
                    return false;
                }

                await session.StopAsync();

                session.Dispose();
                _logger.LogInformation($"停止录音 - 用户: {sipUsername}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"停止录音失败 - 用户: {sipUsername}");
                return false;
            }
        }

        private async Task<RecordingSession?> CreateRecordingSessionAsync(User user, string sipUsername, SIPClient sipClient) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var fileName = $"recording_{sipUsername}_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                var filePath = Path.Combine(_recordingsPath, fileName);

                var recording = new Recording {
                    UserId = user.Id,
                    SipUsername = sipUsername,
                    StartTime = DateTime.UtcNow,
                    FilePath = filePath,
                    Status = RecordingStatus.Recording
                };

                _dbContext.Recordings.Add(recording);
                await _dbContext.SaveChangesAsync();

                return new RecordingSession(_logger, recording, sipClient, _serviceScopeFactory);
            } catch (Exception ex) {
                _logger.LogError(ex, $"创建录音会话失败 - 用户: {sipUsername}");
                return null;
            }
        }

        // 其他接口方法实现...
        public async Task<List<Recording>> GetRecordingsAsync(int userId) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                return await _dbContext.Recordings
                    .Where(r => r.UserId == userId)
                    .OrderByDescending(r => r.StartTime)
                    .ToListAsync();
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取录音列表失败 - 用户ID: {userId}");
                return new List<Recording>();
            }
        }

        public async Task<bool> DeleteRecordingAsync(int recordingId, int userId) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var recording = await _dbContext.Recordings
                    .FirstOrDefaultAsync(r => r.Id == recordingId && r.UserId == userId);

                if (recording == null) return false;

                if (File.Exists(recording.FilePath)) {
                    File.Delete(recording.FilePath);
                }

                _dbContext.Recordings.Remove(recording);
                await _dbContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"删除录音失败 - ID: {recordingId}");
                return false;
            }
        }

        public async Task<bool> IsAutoRecordingEnabledAsync(int userId) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var user = await _dbContext.Users.FindAsync(userId);
                return user?.AutoRecording ?? false;
            } catch (Exception ex) {
                _logger.LogError(ex, $"检查自动录音设置失败 - 用户ID: {userId}");
                return false;
            }
        }

        public async Task<bool> SetAutoRecordingAsync(int userId, bool enabled) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null) return false;

                user.AutoRecording = enabled;
                await _dbContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"设置自动录音失败 - 用户ID: {userId}");
                return false;
            }
        }

        public async Task<RecordingStatus?> GetRecordingStatusAsync(string sipUsername) {
            try {
                if (_activeSessions.TryGetValue(sipUsername, out var session)) {
                    return session.Recording.Status;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user != null) {
                    var latestRecording = await _dbContext.Recordings
                        .Where(r => r.UserId == user.Id)
                        .OrderByDescending(r => r.StartTime)
                        .FirstOrDefaultAsync();

                    return latestRecording?.Status;
                }

                return null;
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取录音状态失败 - 用户: {sipUsername}");
                return null;
            }
        }
    }

    /// <summary>
    /// 录音会话 - 独立管理单个录音的生命周期
    /// </summary>
    internal class RecordingSession : IDisposable {
        public Recording Recording { get; }

        private readonly SIPClient _sipClient;

        private readonly ILogger _logger;
        private readonly AudioRecorder _audioRecorder;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private bool _disposed = false;

        public RecordingSession(ILogger logger, Recording recording, SIPClient sipClient, IServiceScopeFactory serviceScopeFactory) {
            _logger = logger;
            Recording = recording;
            _sipClient = sipClient;
            _serviceScopeFactory = serviceScopeFactory;
            _audioRecorder = new AudioRecorder(recording.FilePath);
            SubscribeToAudioEvents();
        }

        private void SubscribeToAudioEvents() {
            if (_sipClient.MediaSessionManager != null) {
                _sipClient.MediaSessionManager.AudioDataReceived += OnAudioPacketReceived; // 对方的声音（SIP → WebRTC）
                _sipClient.MediaSessionManager.AudioDataSent += OnAudioPacketSent;         // 本地用户的声音（WebRTC → SIP）
            }

            _sipClient.CallEnded += OnCallEnded;
        }

        private void UnsubscribeFromAudioEvents() {
            if (_sipClient.MediaSessionManager != null) {
                _sipClient.MediaSessionManager.AudioDataReceived -= OnAudioPacketReceived;
                _sipClient.MediaSessionManager.AudioDataSent -= OnAudioPacketSent;
            }
            _sipClient.CallEnded -= OnCallEnded;
        }

        private void OnAudioPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType == SDPMediaTypesEnum.audio && rtpPacket?.Payload != null && rtpPacket.Payload.Length > 0) {
                    _audioRecorder.WriteAudioData(rtpPacket.Payload, AudioDirection.Received, rtpPacket.Header.Timestamp, rtpPacket.Header.PayloadType);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "录音写入接收音频数据失败");
            }
        }

        private void OnAudioPacketSent(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType == SDPMediaTypesEnum.audio && rtpPacket?.Payload != null && rtpPacket.Payload.Length > 0) {
                    _audioRecorder.WriteAudioData(rtpPacket.Payload, AudioDirection.Sent, rtpPacket.Header.Timestamp, rtpPacket.Header.PayloadType);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "录音写入发送音频数据失败");
            }
        }

        private void OnCallEnded(SIPClient sipClient) {
            _ = Task.Run(async () => {
                try {
                    await StopAsync();
                } catch (Exception ex) {
                    _logger.LogError(ex, "通话结束时自动停止录音失败");
                }
            });
        }

        public async Task StopAsync() {
            if (_disposed) return;

            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try {
                UnsubscribeFromAudioEvents();

                await _audioRecorder.FinalizeAsync();

                Recording.EndTime = DateTime.UtcNow;
                Recording.Duration = Recording.EndTime.Value - Recording.StartTime;
                Recording.Status = RecordingStatus.Completed;

                if (File.Exists(Recording.FilePath)) {
                    var fileInfo = new FileInfo(Recording.FilePath);
                    Recording.FileSize = fileInfo.Length;
                }

                _dbContext.Recordings.Update(Recording);
                await _dbContext.SaveChangesAsync();
            } catch (Exception ex) {
                _logger.LogError(ex, "停止录音会话失败");
                Recording.Status = RecordingStatus.Failed;
                _dbContext.Recordings.Update(Recording);
                await _dbContext.SaveChangesAsync();
            }
        }

        public void Dispose() {
            if (!_disposed) {
                UnsubscribeFromAudioEvents();
                _audioRecorder?.Dispose();
                _disposed = true;
            }
        }
    }

    internal enum AudioDirection {
        Received, // 接收的音频（对方说话）
        Sent      // 发送的音频（本地用户说话）
    }

    internal class AudioRecorder : IDisposable {
        private readonly string _filePath;
        private readonly FileStream _fileStream;
        private readonly SortedList<uint, byte[]> _audioBuffer;
        private bool _disposed = false;

        public AudioRecorder(string filePath) {
            _filePath = filePath;
            _audioBuffer = new SortedList<uint, byte[]>();
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            WriteWavHeader(0);
        }

        public void WriteAudioData(byte[] rtpPayload, AudioDirection direction, uint timestamp, int payloadType) {
            if (_disposed || rtpPayload == null || rtpPayload.Length == 0) return;

            byte[] pcmData = Array.Empty<byte>();
            if (payloadType == 0) {
                pcmData = DecodeG711MuLaw(rtpPayload);
            } else if (payloadType == 8) {
                pcmData = DecodeG711ALaw(rtpPayload);
            }

            lock (_audioBuffer) {
                if (!_audioBuffer.ContainsKey(timestamp)) {
                    _audioBuffer[timestamp] = new byte[pcmData.Length * 2];
                }
                var buffer = _audioBuffer[timestamp];
                for (int i = 0; i < pcmData.Length; i += 2) {
                    int offset = i * 2;
                    if (direction == AudioDirection.Received) {
                        buffer[offset] = pcmData[i];  
                        buffer[offset + 1] = pcmData[i + 1];
                    } else {
                        buffer[offset + 2] = pcmData[i]; 
                        buffer[offset + 3] = pcmData[i + 1];
                    }
                }
            }
        }


        private byte[] DecodeG711MuLaw(byte[] muLawData) {
            var pcmData = new byte[muLawData.Length * 2];
            for (int i = 0; i < muLawData.Length; i++) {
                int muLawByte = ~muLawData[i] & 0xFF;
                int sign = (muLawByte & 0x80) != 0 ? -1 : 1;
                int exponent = (muLawByte >> 4) & 0x07;
                int mantissa = muLawByte & 0x0F;
                int sample = mantissa + 16;
                sample <<= exponent + 2;
                if (exponent >= 2) sample += (132 << (exponent - 2));
                sample = sign * sample;
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return pcmData;
        }

        private byte[] DecodeG711ALaw(byte[] aLawData) {
            var pcmData = new byte[aLawData.Length * 2];
            for (int i = 0; i < aLawData.Length; i++) {
                int aLawByte = aLawData[i] ^ 0x55;
                int sign = (aLawByte & 0x80) != 0 ? -1 : 1;
                int exponent = (aLawByte >> 4) & 0x07;
                int mantissa = aLawByte & 0x0F;
                int sample;

                if (exponent == 0) {
                    sample = (mantissa << 4) + 8;
                } else {
                    sample = (1 << (exponent + 3)) + (mantissa << (exponent + 4));
                }

                sample = sign * sample;
                sample = Math.Max(-32768, Math.Min(32767, sample));
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return pcmData;
        }

        public async Task FinalizeAsync() {
            if (_disposed) return;

            try {
                // 写入所有音频数据
                byte[] audioDataToWrite;
                lock (_audioBuffer) {
                    audioDataToWrite = _audioBuffer.Values.SelectMany(x => x).ToArray();
                }

                if (audioDataToWrite.Length > 0) {
                    await _fileStream.WriteAsync(audioDataToWrite);
                }

                // 更新WAV文件头
                _fileStream.Seek(0, SeekOrigin.Begin);
                WriteWavHeader(audioDataToWrite.Length);

                await _fileStream.FlushAsync();
            } catch (Exception ex) {
                Console.WriteLine($"完成录音文件时发生错误: {ex.Message}");
            }
        }

        private void WriteWavHeader(int audioDataSize) {
            var header = new byte[44];
            var fileSize = 36 + audioDataSize;
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, header, 0, 4);
            BitConverter.GetBytes(fileSize).CopyTo(header, 4);
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, header, 8, 4);
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, header, 12, 4);
            BitConverter.GetBytes(16).CopyTo(header, 16);
            BitConverter.GetBytes((short)1).CopyTo(header, 20); // PCM
            BitConverter.GetBytes((short)2).CopyTo(header, 22); // Stereo
            BitConverter.GetBytes(8000).CopyTo(header, 24); // Sample rate
            BitConverter.GetBytes(32000).CopyTo(header, 28); // Byte rate
            BitConverter.GetBytes((short)4).CopyTo(header, 32); // Block align
            BitConverter.GetBytes((short)16).CopyTo(header, 34); // Bits per sample
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, header, 36, 4);
            BitConverter.GetBytes(audioDataSize).CopyTo(header, 40);
            _fileStream.Write(header, 0, 44);
        }

        public void Dispose() {
            if (!_disposed) {
                _fileStream?.Dispose();
                _disposed = true;
            }
        }
    }
}