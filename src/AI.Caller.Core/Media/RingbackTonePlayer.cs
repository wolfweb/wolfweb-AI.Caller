using Microsoft.Extensions.Logging;
using System.Buffers;
using AI.Caller.Core.Media.Encoders;

namespace AI.Caller.Core.Media;

public class RingbackTonePlayer : IDisposable {
    private readonly ILogger _logger;
    private readonly MediaSessionManager _mediaSessionManager;
    private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    private readonly string? _audioFilePath;
    private readonly G711Codec _g711Codec;

    private CancellationTokenSource? _cts;
    private Task? _playbackTask;
    private bool _isPlaying;

    private const int SampleRate = 8000;  // 8kHz
    private const int FrameSizeMs = 20;   // 20ms per frame
    private const int SamplesPerFrame = SampleRate * FrameSizeMs / 1000; // 160 samples
    private const int BytesPerFrame = SamplesPerFrame * 2; // 16-bit PCM = 2 bytes per sample

    private const int ToneFrequency1 = 450; // 450 Hz (中国标准)
    private const int ToneDurationMs = 1000; // 响1秒
    private const int SilenceDurationMs = 4000; // 停4秒

    public RingbackTonePlayer(ILogger logger, MediaSessionManager mediaSessionManager, string? audioFilePath = null) {
        _logger = logger;
        _mediaSessionManager = mediaSessionManager;
        _audioFilePath = audioFilePath;
        
        try {
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var g711Logger = loggerFactory.CreateLogger<G711Codec>();
            _g711Codec = new G711Codec(g711Logger, SampleRate, 1);
            _logger.LogDebug("G711Codec initialized for ringback tone");
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize G711Codec, will use fallback");
            throw;
        }
    }

    public void Start() {
        if (_isPlaying) {
            _logger.LogWarning("回铃音已在播放中");
            return;
        }

        _logger.LogInformation("开始播放回铃音（通过 RTP），G711Codec状态: {CodecStatus}", _g711Codec != null ? "已初始化" : "未初始化");

        _cts = new CancellationTokenSource();
        _isPlaying = true;

        _playbackTask = Task.Run(() => PlaybackLoop(_cts.Token));
    }

    public void Stop() {
        if (!_isPlaying) {
            return;
        }

        _logger.LogInformation("停止播放回铃音");

        _cts?.Cancel();
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
                _logger.LogWarning("暂不支持的音频格式: {Extension}，请使用.pcm格式（8kHz, 16-bit, mono）", extension);
                _logger.LogInformation("提示：可以使用ffmpeg转换：ffmpeg -i input.mp3 -ar 8000 -ac 1 -f s16le output.pcm");
            }
            
            return frames;
        } catch (Exception ex) {
            _logger.LogError(ex, "加载音频文件失败: {FilePath}", filePath);
            return frames;
        }
    }

    private List<byte[]> ConvertPcmToFrames(byte[] pcmData) {
        var frames = new List<byte[]>();
        int totalSamples = pcmData.Length / 2; // 16-bit PCM = 2 bytes per sample
        int totalFrames = totalSamples / SamplesPerFrame;
        
        for (int i = 0; i < totalFrames; i++) {
            var pcmFrame = new byte[BytesPerFrame];
            int bytesToCopy = Math.Min(BytesPerFrame, pcmData.Length - i * BytesPerFrame);
            Array.Copy(pcmData, i * BytesPerFrame, pcmFrame, 0, bytesToCopy);
            
            var g711Frame = _g711Codec.EncodeALaw(pcmFrame);
            frames.Add(g711Frame);
        }
        
        return frames;
    }

    private List<byte[]> GenerateRingbackTone() {
        var frames = new List<byte[]>();

        int toneFrames = ToneDurationMs / FrameSizeMs; // 1000ms / 20ms = 50 帧
        int silenceFrames = SilenceDurationMs / FrameSizeMs; // 4000ms / 20ms = 200 帧

        for (int i = 0; i < toneFrames; i++) {
            var frame = GenerateToneFrame(i * SamplesPerFrame);
            frames.Add(frame);
        }

        var silenceFrame = new byte[SamplesPerFrame];
        Array.Fill<byte>(silenceFrame, 0xD5);

        for (int i = 0; i < silenceFrames; i++) {
            frames.Add((byte[])silenceFrame.Clone());
        }

        return frames;
    }

    private byte[] GenerateToneFrame(int startSample) {
        var pcmBytes = new byte[BytesPerFrame];

        for (int i = 0; i < SamplesPerFrame; i++) {
            int sampleIndex = startSample + i;
            double time = (double)sampleIndex / SampleRate;
            
            double value = Math.Sin(2 * Math.PI * ToneFrequency1 * time);
            short sample = (short)(value * 8000); // 适中音量
            
            pcmBytes[i * 2] = (byte)(sample & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        try {
            var encoded = _g711Codec.EncodeALaw(pcmBytes);
            if (encoded == null || encoded.Length == 0) {
                _logger.LogWarning("G711 encoding returned empty result, frame size: {Size}", pcmBytes.Length);
            }
            return encoded;
        } catch (Exception ex) {
            _logger.LogError(ex, "G711 encoding failed");
            return Array.Empty<byte>();
        }
    }

    public void Dispose() {
        Stop();
        _cts?.Dispose();
        _g711Codec?.Dispose();
    }
}
