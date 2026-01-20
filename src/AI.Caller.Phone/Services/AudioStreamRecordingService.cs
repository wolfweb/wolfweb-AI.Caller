using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using FFmpeg.AutoGen;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.SS.UserModel;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.CodeDom;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace AI.Caller.Phone.Services {
    public class AudioStreamRecordingService : ISimpleRecordingService {
        private static Random _rdn = new();
        private readonly ILogger _logger;        
        private readonly string _recordingsPath;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ConcurrentDictionary<int, RecordingSession> _activeSessions;

        public AudioStreamRecordingService(
            ILogger<AudioStreamRecordingService> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory) {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _recordingsPath = configuration.GetValue<string>("RecordingsPath") ?? "recordings";
            _activeSessions = new ConcurrentDictionary<int, RecordingSession>();

            Directory.CreateDirectory(_recordingsPath);
        }

        public async Task<bool> StartRecordingAsync(int userId, SIPClient sipClient) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (_activeSessions.ContainsKey(userId)) {
                    _logger.LogWarning($"用户 {userId} 已经在录音中");
                    return false;
                }

                if (!sipClient.IsCallActive) {
                    _logger.LogWarning($"用户 {userId} 没有活跃的通话");
                    return false;
                }

                var session = await CreateRecordingSessionAsync(userId, sipClient);
                if (session == null) {
                    return false;
                }

                _activeSessions.TryAdd(userId, session);
                _logger.LogInformation($"开始录音 - 用户: {userId}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"开始录音失败 - 用户: {userId}");
                return false;
            }
        }

        public async Task<bool> StopRecordingAsync(int userId, SIPClient sipClient) {
            try {
                if (!_activeSessions.TryRemove(userId, out var session)) {
                    _logger.LogWarning($"用户 {userId} 没有活动的录音");
                    return false;
                }

                await session.StopAsync();

                session.Dispose();
                _logger.LogInformation($"停止录音 - 用户: {userId}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"停止录音失败 - 用户: {userId}");
                return false;
            }
        }

        private async Task<RecordingSession?> CreateRecordingSessionAsync(int userId, SIPClient sipClient) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await _dbContext.Users.Include(u => u.SipAccount).FirstAsync(u => u.Id == userId);

                var fileName = $"recording_{userId}_{user.SipAccount!.SipUsername}_{sipClient.Dialogue.RemoteUserField.URI.User}_{DateTime.Now:yyyyMMdd_HHmmss}_{_rdn.Next(1000, 9999)}.wav";
                var filePath = Path.Combine(_recordingsPath, fileName);

                var recording = new Recording {
                    UserId = user.Id,
                    SipUsername = sipClient.Dialogue.RemoteUserField.URI.User,
                    StartTime = DateTime.UtcNow,
                    FilePath = filePath,
                    Status = Models.RecordingStatus.Recording
                };

                _dbContext.Recordings.Add(recording);
                await _dbContext.SaveChangesAsync();

                return new RecordingSession(_logger, recording, sipClient, _serviceScopeFactory);
            } catch (Exception ex) {
                _logger.LogError(ex, $"创建录音会话失败 - 用户: {userId}");
                return null;
            }
        }

        public async Task<PagedResult<Recording>> GetRecordingsAsync(RecordingFilter filter, int? userId = null) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                IQueryable<Recording> query = _dbContext.Recordings;

                if (filter.StartDate.HasValue) {
                    query = query.Where(r => r.StartTime >= filter.StartDate.Value);
                }
                if (filter.EndDate.HasValue) {
                    query = query.Where(r => r.StartTime <= filter.EndDate.Value.AddDays(1));
                }                
                if (filter.Status.HasValue) {
                    query = query.Where(r => r.Status == filter.Status.Value);
                }

                if (userId.HasValue) {
                    query = query.Where(r => r.UserId == userId);
                }

                var result = await query
                    .OrderByDescending(r => r.StartTime)
                    .Skip((filter.Page - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync();
                return new PagedResult<Recording> {
                    Items = result,
                    TotalCount = await query.CountAsync(),
                    Page = filter.Page,
                    PageSize = filter.PageSize
                };
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取录音列表失败 - 用户ID: {userId}");
                return new PagedResult<Recording>();
            }
        }

        public async Task<Recording?> FindRecordingBy(Expression<Func<Recording, bool>> predict) {
            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await _dbContext.Recordings.FirstOrDefaultAsync(predict);
        }

        public async Task<bool> DeleteRecordingAsync(int recordingId, int? userId = null) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                Recording? recording;
                if (userId.HasValue) {
                    recording = await _dbContext.Recordings
                        .FirstOrDefaultAsync(r => r.Id == recordingId && r.UserId == userId.Value);
                } else {
                    recording = await _dbContext.Recordings
                        .FirstOrDefaultAsync(r => r.Id == recordingId);
                }

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

        public async Task<Models.RecordingStatus?> GetRecordingStatusAsync(int userId) {
            try {
                if (_activeSessions.TryGetValue(userId, out var session)) {
                    return session.Recording.Status;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var user = await _dbContext.Users.Include(u => u.SipAccount).FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null) {
                    var latestRecording = await _dbContext.Recordings
                        .Where(r => r.UserId == user.Id)
                        .OrderByDescending(r => r.StartTime)
                        .FirstOrDefaultAsync();

                    return latestRecording?.Status;
                }

                return null;
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取录音状态失败 - 用户: {userId}");
                return null;
            }
        }

        public async Task<bool> PauseRecordingAsync(int userId) {
            try {
                if (_activeSessions.TryGetValue(userId, out var session)) {
                    session.SetPaused(true);
                    _logger.LogInformation("录音已暂停，用户ID: {UserId}", userId);
                    return true;
                }

                _logger.LogWarning("未找到用户 {UserId} 的活动录音会话", userId);
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, "暂停录音失败，用户ID: {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> ResumeRecordingAsync(int userId) {
            try {
                if (_activeSessions.TryGetValue(userId, out var session)) {
                    session.SetPaused(false);
                    _logger.LogInformation("录音已恢复，用户ID: {UserId}", userId);
                    return true;
                }

                _logger.LogWarning("未找到用户 {UserId} 的活动录音会话", userId);
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, "恢复录音失败，用户ID: {UserId}", userId);
                return false;
            }
        }
    }

    internal class RecordingSession : IDisposable {
        public Recording Recording { get; }

        private readonly ILogger _logger;
        private readonly SIPClient _sipClient;
        private readonly AudioRecorder _audioRecorder;
        private readonly System.Timers.Timer _timeoutTimer;
        private readonly SemaphoreSlim _semaphoreSlim = new(1,1);
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private bool _disposed = false;
        private bool _isPaused = false;

        public RecordingSession(ILogger logger, Recording recording, SIPClient sipClient, IServiceScopeFactory serviceScopeFactory) {
            _logger = logger;
            Recording = recording;
            _sipClient = sipClient;
            _serviceScopeFactory = serviceScopeFactory;
            _audioRecorder = new AudioRecorder(recording.FilePath, logger);
            SubscribeToAudioEvents();

            _timeoutTimer = new System.Timers.Timer(TimeSpan.FromSeconds(30).TotalMilliseconds);
            _timeoutTimer.Elapsed += async (s, e) => {
                if (!_sipClient.IsCallActive) {
                    _logger.LogWarning($"用户 {recording.UserId} 通话已结束，自动停止录音 - RecordingId: {recording.Id}");
                    await StopAsync();
                    _timeoutTimer.Stop();
                }
            };
            _timeoutTimer.AutoReset = true;
            _timeoutTimer.Start();
        }

        private void SubscribeToAudioEvents() {
            if (_sipClient.MediaSessionManager != null) {
                _sipClient.MediaSessionManager.AudioDataReceived += OnAudioPacketReceived; // 对方的声音（SIP → WebRTC）
                _sipClient.MediaSessionManager.AudioDataSent += OnAudioPacketSent;         // 本地用户的声音（WebRTC → SIP）
            }

            _sipClient.CallEnding += OnCallEnded;
        }

        private void UnsubscribeFromAudioEvents() {
            if (_sipClient.MediaSessionManager != null) {
                _sipClient.MediaSessionManager.AudioDataReceived -= OnAudioPacketReceived;
                _sipClient.MediaSessionManager.AudioDataSent -= OnAudioPacketSent;
            }
            _sipClient.CallEnding -= OnCallEnded;
        }

        private void OnAudioPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (_isPaused || _audioRecorder.IsDisposed) return;

                if (mediaType == SDPMediaTypesEnum.audio && rtpPacket?.Payload != null && rtpPacket.Payload.Length > 0) {
                    _= _audioRecorder.WriteAudioDataAsync(rtpPacket.Payload, AudioDirection.Received, rtpPacket.Header.Timestamp, rtpPacket.Header.PayloadType);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "录音写入接收音频数据失败");
            }
        }

        private void OnAudioPacketSent(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (_isPaused || _audioRecorder.IsDisposed) return;

                if (mediaType == SDPMediaTypesEnum.audio && rtpPacket?.Payload != null && rtpPacket.Payload.Length > 0) {
                    _ = _audioRecorder.WriteAudioDataAsync(rtpPacket.Payload, AudioDirection.Sent, rtpPacket.Header.Timestamp, rtpPacket.Header.PayloadType);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "录音写入发送音频数据失败");
            }
        }

        private void OnCallEnded(SIPClient sipClient, CallFinishStatus status) {
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

            _isPaused = true;

            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await _semaphoreSlim.WaitAsync();
            try {
                UnsubscribeFromAudioEvents();

                await _audioRecorder.FinalizeAsync();
                _audioRecorder.Dispose();

                Recording.EndTime = DateTime.UtcNow;
                Recording.Duration = Recording.EndTime.Value - Recording.StartTime;
                Recording.Status = Models.RecordingStatus.Completed;

                if (File.Exists(Recording.FilePath)) {
                    var fileInfo = new FileInfo(Recording.FilePath);
                    Recording.FileSize = fileInfo.Length;
                }

                _dbContext.Recordings.Update(Recording);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Recording stopped successfully, RecordingId: {RecordingId}, FileSize: {FileSize}", Recording.Id, Recording.FileSize);
            } catch (Exception ex) {
                _logger.LogError(ex, "停止录音会话失败，RecordingId: {RecordingId}", Recording.Id);
                Recording.Status = Models.RecordingStatus.Failed;
                _dbContext.Recordings.Update(Recording);
                await _dbContext.SaveChangesAsync();
            } finally {
                _semaphoreSlim.Release();
            }
        }

        public void SetPaused(bool isPaused) {
            _isPaused = isPaused;
            _logger.LogInformation("录音会话暂停状态已设置为: {IsPaused}, RecordingId: {RecordingId}", isPaused, Recording.Id);
        }

        public void Dispose() {
            if (!_disposed) {
                UnsubscribeFromAudioEvents();
                _timeoutTimer.Dispose();
                _disposed = true;
            }
        }
    }

    internal enum AudioDirection {
        Received, // 接收的音频（对方说话）
        Sent      // 发送的音频（本地用户说话）
    }

    internal class FFmpegAudioProcessor : IDisposable {
        private readonly ILogger _logger;
        private readonly int _inputSampleRate;
        private readonly int _channels = 1; // 统一为mono链路，避免跨通道耦合
        private readonly AVSampleFormat _outputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
        private readonly int _outputSampleRate = 16000;
        private readonly string _codecName;
        private const int AVERROR_EAGAIN = -11;
        private int _processedFrameCount = 0; // 添加计数器，用于跳过初始帧

        private unsafe AVCodecContext* _codecContext;
        private unsafe AVFilterGraph* _filterGraph;
        private unsafe AVFilterContext* _bufferSrcContext;
        private unsafe AVFilterContext* _bufferSinkContext;
        private unsafe AVFrame* _frame;
        private unsafe AVFrame* _filteredFrame;
        private unsafe AVPacket* _packet;
        private bool _disposed = false;

        public FFmpegAudioProcessor(ILogger logger, int inputSampleRate, string codecName) {
            _logger = logger;
            _codecName = codecName;
            _inputSampleRate = inputSampleRate;
            InitializeFFmpeg();
        }

        private unsafe void InitializeFFmpeg() {
            var codec = ffmpeg.avcodec_find_decoder_by_name(_codecName);
            if (codec == null) throw new Exception($"无法找到解码器: {_codecName}");

            _codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_codecContext == null) throw new Exception("无法分配解码器上下文");

            _codecContext->sample_rate = _inputSampleRate;
            _codecContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_U8;
            _codecContext->ch_layout = new AVChannelLayout { order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, nb_channels = 1, u = { mask = ffmpeg.AV_CH_LAYOUT_MONO } };

            int ret = ffmpeg.avcodec_open2(_codecContext, codec, null);
            if (ret < 0) throw new Exception($"无法打开解码器: {ret}");

            _filterGraph = ffmpeg.avfilter_graph_alloc();
            if (_filterGraph == null) throw new Exception("无法分配滤波器图");

            var bufferSrc = ffmpeg.avfilter_get_by_name("abuffer");
            var bufferSrcArgs = $"time_base=1/{_inputSampleRate}:sample_rate={_inputSampleRate}:sample_fmt=s16:channels={_channels}:channel_layout=mono";
            AVFilterContext* bufferSrcCtx;
            ret = ffmpeg.avfilter_graph_create_filter(&bufferSrcCtx, bufferSrc, "in", bufferSrcArgs, null, _filterGraph);
            if (ret < 0) throw new Exception($"无法创建buffer source: {ret}");
            _bufferSrcContext = bufferSrcCtx;

            var filterSpec = $"aresample={_outputSampleRate},highpass=f=100,lowpass=f=5000,volume=1.0";

            var bufferSink = ffmpeg.avfilter_get_by_name("abuffersink");
            AVFilterContext* bufferSinkCtx;
            ret = ffmpeg.avfilter_graph_create_filter(&bufferSinkCtx, bufferSink, "out", null, null, _filterGraph);
            if (ret < 0) throw new Exception($"无法创建buffer sink: {ret}");
            _bufferSinkContext = bufferSinkCtx;

            AVSampleFormat[] outSampleFmts = new[] { _outputSampleFormat, (AVSampleFormat)(-1) };
            int[] outSampleRates = new[] { _outputSampleRate, -1 };
            AVChannelLayout[] outChannelLayouts = new[] {
                new AVChannelLayout { order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, nb_channels = _channels, u = { mask = ffmpeg.AV_CH_LAYOUT_MONO } },
                new AVChannelLayout { order = AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC, nb_channels = 0 }
            };

            fixed (AVSampleFormat* fmtPtr = outSampleFmts)
            fixed (int* ratePtr = outSampleRates)
            fixed (AVChannelLayout* layoutPtr = outChannelLayouts) {
                ret = ffmpeg.av_opt_set_bin(_bufferSinkContext, "sample_fmts", (byte*)fmtPtr, sizeof(AVSampleFormat) * outSampleFmts.Length, ffmpeg.AV_OPT_SEARCH_CHILDREN);
                if (ret < 0) {
                    var errorStr = av_strerror(ret);
                    throw new Exception($"无法设置样本格式: {ret} ({errorStr})");
                }
                ret = ffmpeg.av_opt_set_bin(_bufferSinkContext, "sample_rates", (byte*)ratePtr, sizeof(int) * outSampleRates.Length, ffmpeg.AV_OPT_SEARCH_CHILDREN);
                if (ret < 0) {
                    var errorStr = av_strerror(ret);
                    throw new Exception($"无法设置采样率: {ret} ({errorStr})");
                }
            }

            AVFilterInOut* outputs = null;
            AVFilterInOut* inputs = null;
            try {
                outputs = ffmpeg.avfilter_inout_alloc();
                inputs = ffmpeg.avfilter_inout_alloc();
                if (outputs == null || inputs == null)
                    throw new Exception("无法分配滤波器输入/输出");

                outputs->name = ffmpeg.av_strdup("in");
                outputs->filter_ctx = _bufferSrcContext;
                outputs->pad_idx = 0;
                outputs->next = null;

                inputs->name = ffmpeg.av_strdup("out");
                inputs->filter_ctx = _bufferSinkContext;
                inputs->pad_idx = 0;
                inputs->next = null;

                ret = ffmpeg.avfilter_graph_parse_ptr(_filterGraph, filterSpec, &inputs, &outputs, null);
                if (ret < 0) {
                    var errorStr = av_strerror(ret);
                    throw new Exception($"无法解析滤波器图: {ret} ({errorStr})");
                }

                ret = ffmpeg.avfilter_graph_config(_filterGraph, null);
                if (ret < 0) {
                    var errorStr = av_strerror(ret);
                    throw new Exception($"无法配置滤波器图: {ret} ({errorStr})");
                }
            } finally {
                ffmpeg.avfilter_inout_free(&outputs);
                ffmpeg.avfilter_inout_free(&inputs);
            }

            _frame = ffmpeg.av_frame_alloc();
            _filteredFrame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            if (_frame == null || _filteredFrame == null || _packet == null)
                throw new Exception("无法分配帧或包");
        }

        public unsafe byte[] ProcessAudioFrame(byte[] audioData, AudioDirection direction, int payloadType) {
            var stopwatch = Stopwatch.StartNew();
            try {
                fixed (byte* dataPtr = audioData) {
                    _packet->data = dataPtr;
                    _packet->size = audioData.Length;
                }

                int ret = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                if (ret < 0) {
                    var errorStr = av_strerror(ret);
                    _logger.LogError($"无法发送包到解码器: {ret} ({errorStr}), 输入大小={audioData.Length}");
                    return Array.Empty<byte>();
                }

                byte[] pcmData = Array.Empty<byte>();
                while (ffmpeg.avcodec_receive_frame(_codecContext, _frame) >= 0) {
                    // 以frame实际format与声道计算
                    int dataSize = ffmpeg.av_samples_get_buffer_size(null, _frame->ch_layout.nb_channels, _frame->nb_samples, (AVSampleFormat)_frame->format, 1);
                    if (dataSize <= 0) {
                        _logger.LogWarning($"无效的解码数据大小: {dataSize}, 样本数={_frame->nb_samples}");
                        ffmpeg.av_frame_unref(_frame);
                        continue;
                    }

                    pcmData = new byte[dataSize];
                    Marshal.Copy((IntPtr)_frame->data[0], pcmData, 0, dataSize);
                    ffmpeg.av_frame_unref(_frame);
                }

                if (pcmData.Length == 0) {
                    _logger.LogWarning("未生成有效PCM数据，输入大小={InputSize}", audioData.Length);
                    return Array.Empty<byte>();
                }

                // 直接以mono PCM送入滤波链
                _frame->sample_rate = _inputSampleRate;
                _frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
                _frame->nb_samples = pcmData.Length / 2; // mono 16-bit
                _frame->ch_layout = new AVChannelLayout { order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, nb_channels = _channels, u = { mask = ffmpeg.AV_CH_LAYOUT_MONO } };

                fixed (byte* dataPtr2 = pcmData) {
                    _frame->data[0] = dataPtr2;
                    _frame->linesize[0] = pcmData.Length;
                }

                ret = ffmpeg.av_buffersrc_add_frame(_bufferSrcContext, _frame);
                if (ret < 0) {
                    var errorStr = av_strerror(ret);
                    _logger.LogError($"无法添加帧到buffer source: {ret} ({errorStr}), 样本数={_frame->nb_samples}");
                    return Array.Empty<byte>();
                }

                ffmpeg.av_frame_unref(_frame);

                var outputFrames = new List<byte[]>();

                while (true) {
                    ret = ffmpeg.av_buffersink_get_frame(_bufferSinkContext, _filteredFrame);
                    if (ret >= 0) {
                        AVFrame* filtered = _filteredFrame;
                        if (filtered->data[0] == null) {
                            _logger.LogWarning("滤波帧数据为空，样本数={Samples}", filtered->nb_samples);
                            ffmpeg.av_frame_unref(_filteredFrame);
                            continue;
                        }

                        int nbChannels = filtered->ch_layout.nb_channels;
                        int dataSize = ffmpeg.av_samples_get_buffer_size(null, nbChannels, filtered->nb_samples, _outputSampleFormat, 1);
                        if (dataSize <= 0) {
                            _logger.LogWarning($"无效的数据大小: {dataSize}, 样本数={filtered->nb_samples}, 通道数={nbChannels}");
                            ffmpeg.av_frame_unref(_filteredFrame);
                            continue;
                        }

                        byte[] filteredData = new byte[dataSize];
                        try {
                            Marshal.Copy((IntPtr)filtered->data[0], filteredData, 0, dataSize);
                            outputFrames.Add(filteredData);
                            _logger.LogDebug($"处理帧: 样本数={filtered->nb_samples}, 通道数={nbChannels}, 数据大小={dataSize}, 耗时={stopwatch.ElapsedMilliseconds}ms");
                        } catch (Exception ex) {
                            _logger.LogError(ex, $"无法复制滤波帧数据: 数据大小={dataSize}");
                        } finally {
                            ffmpeg.av_frame_unref(_filteredFrame);
                        }
                    } else if (ret == AVERROR_EAGAIN) {
                        break;
                    } else if (ret == ffmpeg.AVERROR_EOF) {
                        _logger.LogDebug("滤波器链达到EOF");
                        break;
                    } else {
                        var errorStr = av_strerror(ret);
                        _logger.LogError($"无法获取滤波帧: {ret} ({errorStr}), 输入数据大小={audioData.Length}, 样本数={_frame->nb_samples}");
                        break;
                    }
                }

                if (outputFrames.Count == 0) return Array.Empty<byte>();

                int totalSize = outputFrames.Sum(f => f.Length);
                byte[] result = new byte[totalSize];
                int offset = 0;
                foreach (var f in outputFrames) {
                    Buffer.BlockCopy(f, 0, result, offset, f.Length);
                    offset += f.Length;
                }

                return result;
            } finally {
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > 50) {
                    _logger.LogWarning($"ProcessAudioFrame 耗时过长: {stopwatch.ElapsedMilliseconds}ms, 输入大小={audioData.Length}, 方向={direction}");
                }
            }
        }

        private unsafe string? av_strerror(int error) {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }

        private unsafe void DisposeFFmpeg() {
            if (_frame != null) {
                fixed (AVFrame** frame = &_frame)
                    ffmpeg.av_frame_free(frame);
            }
            if (_filteredFrame != null) {
                fixed (AVFrame** filteredFrame = &_filteredFrame)
                    ffmpeg.av_frame_free(filteredFrame);
            }
            if (_packet != null) {
                fixed (AVPacket** packet = &_packet)
                    ffmpeg.av_packet_free(packet);
            }
            if (_codecContext != null) {
                fixed (AVCodecContext** codecContext = &_codecContext)
                    ffmpeg.avcodec_free_context(codecContext);
            }
            if (_filterGraph != null) {
                fixed (AVFilterGraph** filterGraph = &_filterGraph)
                    ffmpeg.avfilter_graph_free(filterGraph);
            }
        }

        public void Dispose() {
            if (!_disposed) {
                DisposeFFmpeg();
                _disposed = true;
            }
        }
    }

    internal class AudioRecorder : IDisposable {
        private readonly string _filePath;
        private readonly ILogger _logger;
        private readonly FileStream _fileStream;

        private const int OutputSampleRate = 16000;
        private const int Channels = 2;          // 立体声
        private const int BitsPerSample = 16;
        private const int BytesPerSample = BitsPerSample / 8;
        private const int SamplesPer20ms = OutputSampleRate / 50; // 320
        private const int BytesPer20msMono = SamplesPer20ms * BytesPerSample;
        private const int BytesPer20msStereo = BytesPer20msMono * 2;

        private readonly AudioBuffer _recvBuffer = new();
        private readonly AudioBuffer _sentBuffer = new();

        private readonly ConcurrentDictionary<int, FFmpegAudioProcessor> _processorsRecv = new();
        private readonly ConcurrentDictionary<int, FFmpegAudioProcessor> _processorsSent = new();

        private readonly Channel<(byte[] RtpPayload, AudioDirection Direction, uint RtpTimestamp, int PayloadType)> _inputChannel;
        private readonly Task _processingTask;
        private readonly Task _mixingTask;
        private readonly CancellationTokenSource _cts = new();

        private long _totalStereoSamplesWritten = 0;
        private bool _disposed = false;

        public bool IsDisposed => _disposed;

        public AudioRecorder(string filePath, ILogger logger, int inputSampleRate = 8000) {
            _logger = logger;
            _filePath = filePath;
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

            WriteWavHeader(0);

            _inputChannel = Channel.CreateUnbounded<(byte[], AudioDirection, uint, int)>();

            _processingTask = Task.Run(ProcessInputPacketsAsync, _cts.Token);

            _mixingTask = Task.Run(MixingLoopAsync, _cts.Token);

            _logger.LogInformation("AudioRecorder initialized: {FilePath}, Output: {SampleRate}Hz {Channels}ch {Bits}bit", filePath, OutputSampleRate, Channels, BitsPerSample);
        }

        public async Task WriteAudioDataAsync(byte[] rtpPayload, AudioDirection direction, uint rtpTimestamp, int payloadType) {
            if (_disposed || rtpPayload == null || rtpPayload.Length == 0) return;

            try {
                await _inputChannel.Writer.WriteAsync((rtpPayload, direction, rtpTimestamp, payloadType), _cts.Token);
            } catch (Exception ex) {
                _logger.LogError(ex, "WriteAudioDataAsync failed");
            }
        }

        private async Task ProcessInputPacketsAsync() {
            await foreach (var packet in _inputChannel.Reader.ReadAllAsync(_cts.Token)) {
                try {
                    string codecName = GetCodecName(packet.PayloadType);
                    var processor = GetProcessor(packet.Direction, packet.PayloadType, codecName);

                    byte[] pcmMono = await Task.Run(() => processor.ProcessAudioFrame(packet.RtpPayload, packet.Direction, packet.PayloadType));

                    if (pcmMono.Length > 0) {
                        var buffer = packet.Direction == AudioDirection.Received ? _recvBuffer : _sentBuffer;
                        buffer.Enqueue(pcmMono);
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Process packet failed, direction={Direction}, pt={PayloadType}", packet.Direction, packet.PayloadType);
                }
            }
        }

        private async Task MixingLoopAsync() {
            var stopwatch = new Stopwatch();
            var buffer = new byte[BytesPer20msStereo];

            while (!_cts.IsCancellationRequested) {
                stopwatch.Restart();

                byte[] recvSlice = _recvBuffer.GetNextSlice(BytesPer20msMono);
                byte[] sentSlice = _sentBuffer.GetNextSlice(BytesPer20msMono);

                for (int i = 0; i < SamplesPer20ms; i++) {
                    buffer[i * 4] = recvSlice[i * 2];
                    buffer[i * 4 + 1] = recvSlice[i * 2 + 1];
                    buffer[i * 4 + 2] = sentSlice[i * 2];
                    buffer[i * 4 + 3] = sentSlice[i * 2 + 1];
                }

                await _fileStream.WriteAsync(buffer, 0, BytesPer20msStereo, _cts.Token);
                _totalStereoSamplesWritten += SamplesPer20ms;

                stopwatch.Stop();

                int elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                int sleepMs = 20 - elapsedMs;
                if (sleepMs > 0)
                    await Task.Delay(sleepMs, _cts.Token);
            }
        }

        private FFmpegAudioProcessor GetProcessor(AudioDirection direction, int payloadType, string codecName) {
            var dict = direction == AudioDirection.Received ? _processorsRecv : _processorsSent;
            return dict.GetOrAdd(payloadType, _ => new FFmpegAudioProcessor(_logger, GetInputSampleRate(payloadType), codecName));
        }

        private static int GetInputSampleRate(int payloadType) => payloadType switch {
            9 => 16000, // G722
            _ => 8000   // PCMU/PCMA
        };

        private static string GetCodecName(int payloadType) => payloadType switch {
            0 => "pcm_mulaw",
            8 => "pcm_alaw",
            9 => "g722",
            _ => throw new NotSupportedException($"Unsupported payload type: {payloadType}")
        };

        public async Task FinalizeAsync() {
            if (_disposed || _cts.IsCancellationRequested) return;

            _logger.LogInformation("Finalizing AudioRecorder...");

            _inputChannel.Writer.Complete();

            try {
                await _processingTask.ConfigureAwait(false);
            } catch (Exception ex) {
                _logger.LogDebug(ex, "Processing task completed during shutdown (expected)");
            }
            _cts.Cancel();

            try {
                await _mixingTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {                
            } catch (Exception ex) {
                _logger.LogError(ex, "Error waiting for recorder tasks");
            }

            while (_recvBuffer.HasData || _sentBuffer.HasData) {
                byte[] recvSlice = _recvBuffer.GetNextSlice(BytesPer20msMono);
                byte[] sentSlice = _sentBuffer.GetNextSlice(BytesPer20msMono);

                var buffer = new byte[BytesPer20msStereo];
                for (int i = 0; i < SamplesPer20ms; i++) {
                    buffer[i * 4] = recvSlice[i * 2];
                    buffer[i * 4 + 1] = recvSlice[i * 2 + 1];
                    buffer[i * 4 + 2] = sentSlice[i * 2];
                    buffer[i * 4 + 3] = sentSlice[i * 2 + 1];
                }

                await _fileStream.WriteAsync(buffer, 0, BytesPer20msStereo);
                _totalStereoSamplesWritten += SamplesPer20ms;
            }

            long dataSize = _totalStereoSamplesWritten * Channels * BytesPerSample;
            _fileStream.Seek(0, SeekOrigin.Begin);
            WriteWavHeader((int)dataSize);
            await _fileStream.FlushAsync();

            _logger.LogInformation("AudioRecorder finalized. Duration: {Duration:F2}s, File: {FilePath}", _totalStereoSamplesWritten / (double)OutputSampleRate, _filePath);
        }

        private void WriteWavHeader(int audioDataSize) {
            var header = new byte[44];
            int byteRate = OutputSampleRate * Channels * BytesPerSample;
            short blockAlign = (short)(Channels * BytesPerSample);

            Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
            BitConverter.GetBytes(36 + audioDataSize).CopyTo(header, 4);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);

            Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
            BitConverter.GetBytes(16).CopyTo(header, 16);
            BitConverter.GetBytes((short)1).CopyTo(header, 20); // PCM
            BitConverter.GetBytes((short)Channels).CopyTo(header, 22);
            BitConverter.GetBytes(OutputSampleRate).CopyTo(header, 24);
            BitConverter.GetBytes(byteRate).CopyTo(header, 28);
            BitConverter.GetBytes(blockAlign).CopyTo(header, 32);
            BitConverter.GetBytes((short)BitsPerSample).CopyTo(header, 34);

            Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
            BitConverter.GetBytes(audioDataSize).CopyTo(header, 40);

            _fileStream.Write(header, 0, 44);
        }

        public void Dispose() {
            if (_disposed) return;

            try {
                _cts.Cancel();
                _processingTask?.Wait(TimeSpan.FromSeconds(5));
                _mixingTask?.Wait(TimeSpan.FromSeconds(5));
            } catch { }

            _fileStream?.Dispose();
            foreach (var p in _processorsRecv.Values) p.Dispose();
            foreach (var p in _processorsSent.Values) p.Dispose();
            _cts.Dispose();

            _disposed = true;
        }

        private class AudioBuffer {
            private readonly ConcurrentQueue<byte[]> _queue = new();
            private byte[] _currentBlock = null;
            private int _offsetInCurrent = 0;

            public bool HasData => _queue.Count > 0 || (_currentBlock != null && _offsetInCurrent < _currentBlock.Length);

            public void Enqueue(byte[] block) {
                if (block?.Length > 0)
                    _queue.Enqueue(block);
            }

            public byte[] GetNextSlice(int sliceBytes) {
                var slice = new byte[sliceBytes];
                int pos = 0;

                while (pos < sliceBytes) {
                    if (_currentBlock == null || _offsetInCurrent >= _currentBlock.Length) {
                        if (!_queue.TryDequeue(out _currentBlock))
                            break; 
                        _offsetInCurrent = 0;
                    }

                    int available = _currentBlock.Length - _offsetInCurrent;
                    int needed = sliceBytes - pos;
                    int copy = Math.Min(available, needed);

                    Buffer.BlockCopy(_currentBlock, _offsetInCurrent, slice, pos, copy);

                    pos += copy;
                    _offsetInCurrent += copy;
                }

                if (pos < sliceBytes)
                    Array.Clear(slice, pos, sliceBytes - pos);

                return slice;
            }
        }
    }
}
