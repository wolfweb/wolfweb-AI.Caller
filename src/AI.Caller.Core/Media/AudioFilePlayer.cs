using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AI.Caller.Core.Media;

/// <summary>
/// 通用音频文件播放器 - 支持多种音频格式加载和转换，动态适配当前协商的编码器
/// </summary>
public class AudioFilePlayer : IDisposable {
    private readonly ILogger _logger;
    private readonly AudioCodecFactory _codecFactory;
    private readonly Dictionary<AudioCodec, IAudioCodec> _codecCache = new();
    
    private MediaSessionManager? _mediaSessionManager;
    private AudioCodec _fallbackCodec = AudioCodec.PCMA;
    private AudioCodec? _preferredCodec;

    private const int FrameSizeMs = 20;   // 20ms per frame

    public AudioFilePlayer(ILoggerFactory loggerFactory, AudioCodecFactory codecFactory) {
        _logger = loggerFactory.CreateLogger<AudioFilePlayer>();
        _codecFactory = codecFactory ?? throw new ArgumentNullException(nameof(codecFactory));
        
        _logger.LogDebug("AudioFilePlayer initialized with dynamic codec support");
    }

    /// <summary>
    /// 设置首选编码器（覆盖MediaSessionManager和默认值）
    /// </summary>
    public void SetPreferredCodec(AudioCodec codec) {
        _preferredCodec = codec;
    }

    /// <summary>
    /// 设置MediaSessionManager引用，用于获取当前协商的编码器
    /// </summary>
    public void SetMediaSessionManager(MediaSessionManager? mediaSessionManager) {
        _mediaSessionManager = mediaSessionManager;
        _logger.LogDebug("MediaSessionManager reference set for AudioFilePlayer");
    }

    /// <summary>
    /// 加载音频文件并转换为当前协商编码格式的帧
    /// </summary>
    /// <param name="filePath">音频文件路径</param>
    /// <returns>编码后的音频帧列表</returns>
    public async Task<List<byte[]>> LoadAsync(string filePath) {
        if (string.IsNullOrEmpty(filePath)) {
            _logger.LogWarning("音频文件路径为空");
            return [];
        }

        if (!File.Exists(filePath)) {
            _logger.LogError("音频文件不存在: {FilePath}", filePath);
            return [];
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        try {
            var result = extension switch {
                ".pcm" or ".raw" => await LoadPcmAsync(filePath),
                ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a" => await LoadWithFFmpegAsync(filePath),
                _ => throw new NotSupportedException($"不支持的音频格式: {extension}")
            };
            
            _logger.LogDebug("音频文件加载完成: {FilePath}, 帧数: {FrameCount}", filePath, result.Count);
            
            return result;
        } catch (Exception ex) {
            _logger.LogError(ex, "加载音频文件失败: {FilePath}", filePath);
            return [];
        }
    }

    /// <summary>
    /// 加载PCM格式音频文件
    /// </summary>
    public async Task<List<byte[]>> LoadPcmAsync(string filePath) {
        try {
            var pcmData = await File.ReadAllBytesAsync(filePath);
            _logger.LogDebug("已加载PCM文件: {Size} 字节", pcmData.Length);

            if (pcmData.Length == 0) {
                _logger.LogError("PCM文件为空: {FilePath}", filePath);
                return [];
            }

            var frames = ConvertPcmToFrames(pcmData);
            _logger.LogDebug("PCM文件已分割成 {Count} 帧", frames.Count);

            return frames;
        } catch (Exception ex) {
            _logger.LogError(ex, "加载PCM文件失败: {FilePath}", filePath);
            return [];
        }
    }

    /// <summary>
    /// 使用FFmpeg加载并转换音频文件
    /// </summary>
    public async Task<List<byte[]>> LoadWithFFmpegAsync(string filePath) {
        try {
            // 获取当前编码器信息
            var currentCodec = GetCurrentCodec();
            var sampleRate = GetSampleRateForCodec(currentCodec);
            
            // 创建临时PCM文件
            var tempPcmFile = Path.GetTempFileName();

            try {
                // 使用FFmpeg转换为PCM格式，使用正确的采样率
                var ffmpegArgs = $"-i \"{filePath}\" -ar {sampleRate} -ac 1 -f s16le -y \"{tempPcmFile}\"";

                _logger.LogDebug("执行FFmpeg转换: {Args}", ffmpegArgs);

                var processStartInfo = new ProcessStartInfo {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processStartInfo);
                if (process == null) {
                    _logger.LogError("无法启动FFmpeg进程");
                    return [];
                }

                await process.WaitForExitAsync();

                if (process.ExitCode != 0) {
                    var error = await process.StandardError.ReadToEndAsync();
                    _logger.LogError("FFmpeg转换失败: {Error}", error);
                    return [];
                }

                _logger.LogInformation("FFmpeg转换成功，加载PCM数据");

                // 加载转换后的PCM文件
                return await LoadPcmAsync(tempPcmFile);
            } finally {
                // 清理临时文件
                try {
                    if (File.Exists(tempPcmFile)) {
                        File.Delete(tempPcmFile);
                    }
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "删除临时文件失败: {TempFile}", tempPcmFile);
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "使用FFmpeg加载音频失败: {FilePath}", filePath);
            return [];
        }
    }

    /// <summary>
    /// 将PCM数据转换为当前协商编码格式的帧
    /// </summary>
    private List<byte[]> ConvertPcmToFrames(byte[] pcmData) {
        var frames = new List<byte[]>();
        var currentCodec = GetCurrentCodec();
        var sampleRate = GetSampleRateForCodec(currentCodec);
        var samplesPerFrame = sampleRate * FrameSizeMs / 1000;
        var bytesPerFrame = samplesPerFrame * 2; // 16-bit PCM = 2 bytes per sample
        
        int totalSamples = pcmData.Length / 2; // 16-bit PCM = 2 bytes per sample
        int totalFrames = (totalSamples + samplesPerFrame - 1) / samplesPerFrame; // 向上取整

        _logger.LogDebug("转换PCM数据: {TotalSamples} 采样点, {TotalFrames} 帧, 原始数据大小: {DataSize} 字节, 使用编码器: {Codec}@{SampleRate}Hz", 
            totalSamples, totalFrames, pcmData.Length, currentCodec, sampleRate);

        // 获取编码器
        var codec = GetCodecForType(currentCodec);
        if (codec == null) {
            _logger.LogError("无法获取编码器: {Codec}", currentCodec);
            return frames;
        }

        for (int i = 0; i < totalFrames; i++) {
            int offset = i * bytesPerFrame;
            int remainingBytes = pcmData.Length - offset;
            int bytesToCopy = Math.Min(bytesPerFrame, remainingBytes);

            var pcmFrame = new byte[bytesPerFrame];
            Array.Copy(pcmData, offset, pcmFrame, 0, bytesToCopy);

            // 如果最后一帧不完整，用静音填充
            if (bytesToCopy < bytesPerFrame) {
                Array.Fill<byte>(pcmFrame, 0, bytesToCopy, bytesPerFrame - bytesToCopy);
            }

            try {
                var encodedFrame = codec.Encode(pcmFrame);
                if (encodedFrame != null && encodedFrame.Length > 0) {
                    frames.Add(encodedFrame);
                } else {
                    _logger.LogError("编码返回空结果，帧索引: {FrameIndex}", i);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "编码失败，帧索引: {FrameIndex}", i);
            }
        }

        return frames;
    }

    /// <summary>
    /// 生成静音帧
    /// </summary>
    /// <param name="durationMs">静音时长（毫秒）</param>
    public List<byte[]> GenerateSilence(int durationMs) {
        var frames = new List<byte[]>();
        int frameCount = durationMs / FrameSizeMs;
        
        var currentCodec = GetCurrentCodec();
        var sampleRate = GetSampleRateForCodec(currentCodec);
        var samplesPerFrame = sampleRate * FrameSizeMs / 1000;
        
        // 生成PCM静音帧
        var silencePcmFrame = new byte[samplesPerFrame * 2]; // 16-bit PCM
        Array.Fill<byte>(silencePcmFrame, 0); // PCM静音值为0
        
        // 获取编码器
        var codec = GetCodecForType(currentCodec);
        if (codec == null) {
            _logger.LogError("无法获取编码器生成静音: {Codec}", currentCodec);
            return frames;
        }

        for (int i = 0; i < frameCount; i++) {
            try {
                var encodedFrame = codec.Encode(silencePcmFrame);
                if (encodedFrame != null && encodedFrame.Length > 0) {
                    frames.Add(encodedFrame);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "生成静音帧编码失败");
            }
        }

        _logger.LogDebug("生成静音: {Duration}ms, {FrameCount} 帧, 编码器: {Codec}", durationMs, frameCount, currentCodec);

        return frames;
    }

    /// <summary>
    /// 生成单频音调
    /// </summary>
    /// <param name="frequency">频率（Hz）</param>
    /// <param name="durationMs">时长（毫秒）</param>
    /// <param name="amplitude">振幅（0.0-1.0）</param>
    public List<byte[]> GenerateTone(int frequency, int durationMs, double amplitude = 0.5) {
        var frames = new List<byte[]>();
        var currentCodec = GetCurrentCodec();
        var sampleRate = GetSampleRateForCodec(currentCodec);
        var samplesPerFrame = sampleRate * FrameSizeMs / 1000;
        var bytesPerFrame = samplesPerFrame * 2;
        
        int frameCount = durationMs / FrameSizeMs;
        
        // 获取编码器
        var codec = GetCodecForType(currentCodec);
        if (codec == null) {
            _logger.LogError("无法获取编码器生成音调: {Codec}", currentCodec);
            return frames;
        }

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++) {
            var pcmBytes = new byte[bytesPerFrame];
            int startSample = frameIndex * samplesPerFrame;

            for (int i = 0; i < samplesPerFrame; i++) {
                int sampleIndex = startSample + i;
                double time = (double)sampleIndex / sampleRate;

                double value = Math.Sin(2 * Math.PI * frequency * time);
                short sample = (short)(value * amplitude * short.MaxValue);

                pcmBytes[i * 2] = (byte)(sample & 0xFF);
                pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            try {
                var encoded = codec.Encode(pcmBytes);
                if (encoded != null && encoded.Length > 0) {
                    frames.Add(encoded);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "生成音调时编码失败");
            }
        }

        _logger.LogDebug("生成音调: {Frequency}Hz, {Duration}ms, {FrameCount} 帧, 编码器: {Codec}", frequency, durationMs, frameCount, currentCodec);

        return frames;
    }

    /// <summary>
    /// 获取当前协商的编码器类型
    /// </summary>
    private AudioCodec GetCurrentCodec() {
        if (_preferredCodec.HasValue) {
            return _preferredCodec.Value;
        }

        var currentCodec = _mediaSessionManager?.SelectedCodec ?? _fallbackCodec;
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
