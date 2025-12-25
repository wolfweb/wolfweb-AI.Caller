using Microsoft.Extensions.Logging;
using System.Buffers;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media;

public class RingbackTonePlayer : IDisposable {
    private readonly ILogger _logger;
    private readonly MediaSessionManager _mediaSessionManager;
    private readonly AudioCodecFactory _codecFactory;
    private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    private readonly string? _audioFilePath;
    private readonly Dictionary<AudioCodec, IAudioCodec> _codecCache = new();

    private CancellationTokenSource? _cts;
    private Task? _playbackTask;
    private bool _isPlaying;

    private const int FrameSizeMs = 20;   // 20ms per frame

    private const int ToneFrequency1 = 450; // 450 Hz (中国标准)
    private const int ToneDurationMs = 1000; // 响1秒
    private const int SilenceDurationMs = 4000; // 停4秒

    public RingbackTonePlayer(ILogger logger, MediaSessionManager mediaSessionManager, AudioCodecFactory codecFactory, string? audioFilePath = null) {
        _logger = logger;
        _mediaSessionManager = mediaSessionManager;
        _codecFactory = codecFactory ?? throw new ArgumentNullException(nameof(codecFactory));
        _audioFilePath = audioFilePath;
        
        _logger.LogDebug("RingbackTonePlayer initialized with dynamic codec support");
    }

    public void Start() {
        if (_isPlaying) {
            _logger.LogWarning("回铃音已在播放中");
            return;
        }

        var currentCodec = GetCurrentCodec();
        _logger.LogInformation("开始播放回铃音（通过 RTP），使用编码器: {Codec}", currentCodec);

        _cts = new CancellationTokenSource();
        _isPlaying = true;

        _playbackTask = Task.Run(() => PlaybackLoop(_cts.Token));
    }

    public void Stop() {
        _cts?.Cancel();
        if (!_isPlaying) {
            return;
        }

        _logger.LogInformation("停止播放回铃音");

        _isPlaying = false;

        try {
            _playbackTask?.Wait(TimeSpan.FromSeconds(1));
        } catch (Exception ex) {
            _logger.LogWarning(ex, "等待回铃音播放任务结束时出错");
        }
    }

    private async Task PlaybackLoop(CancellationToken ct) {
        try {
            List<byte[]> toneFrames;
            
            if (!string.IsNullOrEmpty(_audioFilePath) && File.Exists(_audioFilePath)) {
                _logger.LogInformation("尝试从文件加载回铃音: {FilePath}", _audioFilePath);
                toneFrames = await LoadAudioFromFileAsync(_audioFilePath);
                
                if (toneFrames.Count == 0) {
                    _logger.LogWarning("从文件加载回铃音失败，使用生成的音调");
                    toneFrames = GenerateRingbackTone();
                }
            } else {
                _logger.LogInformation("使用生成的回铃音音调");
                toneFrames = GenerateRingbackTone();
            }

            _logger.LogInformation("回铃音音频已准备，共 {Count} 帧", toneFrames.Count);

            while (!ct.IsCancellationRequested) {
                foreach (var frame in toneFrames) {
                    if (ct.IsCancellationRequested)
                        break;
                    if(_mediaSessionManager.MediaSession == null || _mediaSessionManager.MediaSession.IsClosed) {
                        _cts?.Cancel();
                        break;
                    }
                    _mediaSessionManager.SendAudioFrame(frame);

                    await Task.Delay(FrameSizeMs, ct);
                }
            }
        } catch (OperationCanceledException) {
            _logger.LogDebug("回铃音播放被取消");
        } catch (Exception ex) {
            _logger.LogError(ex, "回铃音播放出错");
        } finally {
            _logger.LogInformation("回铃音播放循环结束");
        }
    }

    private async Task<List<byte[]>> LoadAudioFromFileAsync(string filePath) {
        var frames = new List<byte[]>();
        
        try {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            if (extension == ".pcm" || extension == ".raw") {
                var pcmData = await File.ReadAllBytesAsync(filePath);
                _logger.LogInformation("已加载PCM文件: {Size} 字节", pcmData.Length);
                
                frames = ConvertPcmToFrames(pcmData);
                _logger.LogInformation("PCM文件已分割成 {Count} 帧", frames.Count);
            } 
            else {
                var currentCodec = GetCurrentCodec();
                var sampleRate = GetSampleRateForCodec(currentCodec);
                _logger.LogWarning("暂不支持的音频格式: {Extension}，请使用.pcm格式（{SampleRate}Hz, 16-bit, mono）", extension, sampleRate);
                _logger.LogInformation("提示：可以使用ffmpeg转换：ffmpeg -i input.mp3 -ar {SampleRate} -ac 1 -f s16le output.pcm", sampleRate);
            }
            
            return frames;
        } catch (Exception ex) {
            _logger.LogError(ex, "加载音频文件失败: {FilePath}", filePath);
            return frames;
        }
    }

    private List<byte[]> ConvertPcmToFrames(byte[] pcmData) {
        var frames = new List<byte[]>();
        var currentCodec = GetCurrentCodec();
        var sampleRate = GetSampleRateForCodec(currentCodec);
        var samplesPerFrame = sampleRate * FrameSizeMs / 1000;
        var bytesPerFrame = samplesPerFrame * 2; // 16-bit PCM
        
        int totalSamples = pcmData.Length / 2; // 16-bit PCM = 2 bytes per sample
        int totalFrames = totalSamples / samplesPerFrame;
        
        // 获取编码器
        var codec = GetCodecForType(currentCodec);
        if (codec == null) {
            _logger.LogError("无法获取编码器: {Codec}", currentCodec);
            return frames;
        }
        
        for (int i = 0; i < totalFrames; i++) {
            var pcmFrame = new byte[bytesPerFrame];
            int bytesToCopy = Math.Min(bytesPerFrame, pcmData.Length - i * bytesPerFrame);
            Array.Copy(pcmData, i * bytesPerFrame, pcmFrame, 0, bytesToCopy);
            
            // 如果最后一帧不完整，用静音填充
            if (bytesToCopy < bytesPerFrame) {
                Array.Fill<byte>(pcmFrame, 0, bytesToCopy, bytesPerFrame - bytesToCopy);
            }
            
            try {
                var encodedFrame = codec.Encode(pcmFrame);
                if (encodedFrame != null && encodedFrame.Length > 0) {
                    frames.Add(encodedFrame);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "编码PCM帧失败");
            }
        }
        
        return frames;
    }

    private List<byte[]> GenerateRingbackTone() {
        var frames = new List<byte[]>();
        var currentCodec = GetCurrentCodec();
        var sampleRate = GetSampleRateForCodec(currentCodec);
        var samplesPerFrame = sampleRate * FrameSizeMs / 1000;

        int toneFrames = ToneDurationMs / FrameSizeMs; // 1000ms / 20ms = 50 帧
        int silenceFrames = SilenceDurationMs / FrameSizeMs; // 4000ms / 20ms = 200 帧

        // 生成音调帧
        for (int i = 0; i < toneFrames; i++) {
            var frame = GenerateToneFrame(i * samplesPerFrame, sampleRate, samplesPerFrame);
            if (frame != null && frame.Length > 0) {
                frames.Add(frame);
            }
        }

        // 生成静音帧
        var silenceFrame = GenerateSilenceFrame(samplesPerFrame);
        for (int i = 0; i < silenceFrames; i++) {
            if (silenceFrame != null && silenceFrame.Length > 0) {
                frames.Add((byte[])silenceFrame.Clone());
            }
        }

        return frames;
    }

    private byte[] GenerateToneFrame(int startSample, int sampleRate, int samplesPerFrame) {
        var bytesPerFrame = samplesPerFrame * 2;
        var pcmBytes = new byte[bytesPerFrame];

        for (int i = 0; i < samplesPerFrame; i++) {
            int sampleIndex = startSample + i;
            double time = (double)sampleIndex / sampleRate;
            
            double value = Math.Sin(2 * Math.PI * ToneFrequency1 * time);
            short sample = (short)(value * 8000); // 适中音量
            
            pcmBytes[i * 2] = (byte)(sample & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        try {
            var currentCodec = GetCurrentCodec();
            var codec = GetCodecForType(currentCodec);
            if (codec == null) {
                _logger.LogError("无法获取编码器生成音调: {Codec}", currentCodec);
                return Array.Empty<byte>();
            }
            
            var encoded = codec.Encode(pcmBytes);
            if (encoded == null || encoded.Length == 0) {
                _logger.LogWarning("编码返回空结果，帧大小: {Size}", pcmBytes.Length);
                return Array.Empty<byte>();
            }
            return encoded;
        } catch (Exception ex) {
            _logger.LogError(ex, "编码失败");
            return Array.Empty<byte>();
        }
    }

    private byte[] GenerateSilenceFrame(int samplesPerFrame) {
        var bytesPerFrame = samplesPerFrame * 2;
        var pcmBytes = new byte[bytesPerFrame];
        Array.Fill<byte>(pcmBytes, 0); // PCM静音值为0

        try {
            var currentCodec = GetCurrentCodec();
            var codec = GetCodecForType(currentCodec);
            if (codec == null) {
                _logger.LogError("无法获取编码器生成静音: {Codec}", currentCodec);
                return Array.Empty<byte>();
            }
            
            var encoded = codec.Encode(pcmBytes);
            if (encoded == null || encoded.Length == 0) {
                _logger.LogWarning("静音编码返回空结果");
                return Array.Empty<byte>();
            }
            return encoded;
        } catch (Exception ex) {
            _logger.LogError(ex, "静音编码失败");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// 获取当前协商的编码器类型
    /// </summary>
    private AudioCodec GetCurrentCodec() {
        var currentCodec = _mediaSessionManager?.SelectedCodec ?? AudioCodec.PCMA;
        _logger.LogTrace("获取当前编码器: {Codec}", currentCodec);
        return currentCodec;
    }

    /// <summary>
    /// 根据编码器类型获取采样率
    /// </summary>
    private int GetSampleRateForCodec(AudioCodec codec) {
        return codec switch {
            AudioCodec.G722 => 16000,
            AudioCodec.PCMA => 8000,
            AudioCodec.PCMU => 8000,
            _ => 8000
        };
    }

    /// <summary>
    /// 获取指定类型的编码器实例
    /// </summary>
    private IAudioCodec? GetCodecForType(AudioCodec codecType) {
        if (_codecCache.TryGetValue(codecType, out var cachedCodec)) {
            return cachedCodec;
        }

        try {
            var codec = _codecFactory.GetCodec(codecType);
            _codecCache[codecType] = codec;
            _logger.LogDebug("创建编码器: {CodecType}", codecType);
            return codec;
        } catch (Exception ex) {
            _logger.LogError(ex, "创建编码器失败: {CodecType}", codecType);
            return null;
        }
    }

    public void Dispose() {
        Stop();
        _cts?.Dispose();
        
        // 清理缓存的编码器
        foreach (var codec in _codecCache.Values) {
            try {
                codec?.Dispose();
            } catch (Exception ex) {
                _logger.LogWarning(ex, "释放编码器资源失败");
            }
        }
        _codecCache.Clear();
        
        GC.SuppressFinalize(this);
    }
}
