using AI.Caller.Core.Media.Encoders;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AI.Caller.Core.Media;

/// <summary>
/// 通用音频文件播放器 - 支持多种音频格式加载和转换
/// </summary>
public class AudioFilePlayer : IDisposable {
    private readonly ILogger _logger;
    private readonly G711Codec _g711Codec;

    private const int SampleRate = 8000;  // 8kHz
    private const int FrameSizeMs = 20;   // 20ms per frame
    private const int SamplesPerFrame = SampleRate * FrameSizeMs / 1000; // 160 samples
    private const int BytesPerFrame = SamplesPerFrame * 2; // 16-bit PCM = 2 bytes per sample

    public AudioFilePlayer(ILoggerFactory loggerFactory) {
        _logger = loggerFactory.CreateLogger<AudioFilePlayer>();

        try {
            var g711Logger = loggerFactory.CreateLogger<G711Codec>();
            _g711Codec = new G711Codec(g711Logger, SampleRate, 1);
            _logger.LogDebug("G711Codec initialized for AudioFilePlayer");
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize G711Codec");
            throw;
        }
    }

    /// <summary>
    /// 加载音频文件并转换为G.711 A-law编码的帧
    /// </summary>
    /// <param name="filePath">音频文件路径</param>
    /// <returns>G.711编码的音频帧列表</returns>
    public async Task<List<byte[]>> LoadAsync(string filePath) {
        if (string.IsNullOrEmpty(filePath)) {
            _logger.LogWarning("音频文件路径为空");
            return new List<byte[]>();
        }

        if (!File.Exists(filePath)) {
            _logger.LogError("音频文件不存在: {FilePath}", filePath);
            return new List<byte[]>();
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        _logger.LogInformation("加载音频文件: {FilePath}, 格式: {Extension}", filePath, extension);

        try {
            return extension switch {
                ".pcm" or ".raw" => await LoadPcmAsync(filePath),
                ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a" => await LoadWithFFmpegAsync(filePath),
                _ => throw new NotSupportedException($"不支持的音频格式: {extension}")
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "加载音频文件失败: {FilePath}", filePath);
            return new List<byte[]>();
        }
    }

    /// <summary>
    /// 加载PCM格式音频文件
    /// </summary>
    public async Task<List<byte[]>> LoadPcmAsync(string filePath) {
        try {
            var pcmData = await File.ReadAllBytesAsync(filePath);
            _logger.LogInformation("已加载PCM文件: {Size} 字节", pcmData.Length);

            var frames = ConvertPcmToFrames(pcmData);
            _logger.LogInformation("PCM文件已分割成 {Count} 帧", frames.Count);

            return frames;
        } catch (Exception ex) {
            _logger.LogError(ex, "加载PCM文件失败: {FilePath}", filePath);
            return new List<byte[]>();
        }
    }

    /// <summary>
    /// 使用FFmpeg加载并转换音频文件
    /// </summary>
    public async Task<List<byte[]>> LoadWithFFmpegAsync(string filePath) {
        try {
            // 创建临时PCM文件
            var tempPcmFile = Path.GetTempFileName();

            try {
                // 使用FFmpeg转换为PCM格式
                var ffmpegArgs = $"-i \"{filePath}\" -ar {SampleRate} -ac 1 -f s16le -y \"{tempPcmFile}\"";

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
                    return new List<byte[]>();
                }

                await process.WaitForExitAsync();

                if (process.ExitCode != 0) {
                    var error = await process.StandardError.ReadToEndAsync();
                    _logger.LogError("FFmpeg转换失败: {Error}", error);
                    return new List<byte[]>();
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
            return new List<byte[]>();
        }
    }

    /// <summary>
    /// 将PCM数据转换为G.711编码的帧
    /// </summary>
    private List<byte[]> ConvertPcmToFrames(byte[] pcmData) {
        var frames = new List<byte[]>();
        int totalSamples = pcmData.Length / 2; // 16-bit PCM = 2 bytes per sample
        int totalFrames = (totalSamples + SamplesPerFrame - 1) / SamplesPerFrame; // 向上取整

        _logger.LogDebug("转换PCM数据: {TotalSamples} 采样点, {TotalFrames} 帧", totalSamples, totalFrames);

        for (int i = 0; i < totalFrames; i++) {
            int offset = i * BytesPerFrame;
            int remainingBytes = pcmData.Length - offset;
            int bytesToCopy = Math.Min(BytesPerFrame, remainingBytes);

            var pcmFrame = new byte[BytesPerFrame];
            Array.Copy(pcmData, offset, pcmFrame, 0, bytesToCopy);

            // 如果最后一帧不完整，用静音填充
            if (bytesToCopy < BytesPerFrame) {
                Array.Fill<byte>(pcmFrame, 0, bytesToCopy, BytesPerFrame - bytesToCopy);
            }

            try {
                var g711Frame = _g711Codec.EncodeALaw(pcmFrame);
                if (g711Frame != null && g711Frame.Length > 0) {
                    frames.Add(g711Frame);
                } else {
                    _logger.LogWarning("G711编码返回空结果，帧索引: {FrameIndex}", i);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "G711编码失败，帧索引: {FrameIndex}", i);
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

        var silenceFrame = new byte[SamplesPerFrame];
        Array.Fill<byte>(silenceFrame, 0xD5); // G.711 A-law静音值

        for (int i = 0; i < frameCount; i++) {
            frames.Add((byte[])silenceFrame.Clone());
        }

        _logger.LogDebug("生成静音: {Duration}ms, {FrameCount} 帧", durationMs, frameCount);

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
        int frameCount = durationMs / FrameSizeMs;

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++) {
            var pcmBytes = new byte[BytesPerFrame];
            int startSample = frameIndex * SamplesPerFrame;

            for (int i = 0; i < SamplesPerFrame; i++) {
                int sampleIndex = startSample + i;
                double time = (double)sampleIndex / SampleRate;

                double value = Math.Sin(2 * Math.PI * frequency * time);
                short sample = (short)(value * amplitude * short.MaxValue);

                pcmBytes[i * 2] = (byte)(sample & 0xFF);
                pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            try {
                var encoded = _g711Codec.EncodeALaw(pcmBytes);
                if (encoded != null && encoded.Length > 0) {
                    frames.Add(encoded);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "生成音调时G711编码失败");
            }
        }

        _logger.LogDebug("生成音调: {Frequency}Hz, {Duration}ms, {FrameCount} 帧", frequency, durationMs, frameCount);

        return frames;
    }

    public void Dispose() {
        _g711Codec?.Dispose();
    }
}
