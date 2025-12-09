using Microsoft.Extensions.Logging;
using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media;

/// <summary>
/// 音频格式转换服务 - 使用 FFmpeg.AutoGen
/// 支持将各种音频格式转换为 PCM 格式
/// </summary>
public unsafe class AudioConverter : IAudioConverter {
    private readonly ILogger _logger;
    private bool _disposed;

    public AudioConverter(ILogger logger) {
        _logger = logger;
    }

    /// <summary>
    /// 将音频文件转换为 PCM 格式
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="sampleRate">目标采样率（默认8000Hz）</param>
    /// <param name="channels">目标声道数（默认1=单声道）</param>
    /// <returns>转换是否成功</returns>
    public Task<bool> ConvertToPcmAsync(string inputPath, string outputPath, int sampleRate = 8000, int channels = 1) {
        return Task.Run(() => ConvertToPcm(inputPath, outputPath, sampleRate, channels));
    }

    private bool ConvertToPcm(string inputPath, string outputPath, int sampleRate, int channels) {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioConverter));

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath)) {
            _logger.LogError("输入文件不存在: {InputPath}", inputPath);
            return false;
        }

        AVFormatContext* formatContext = null;
        AVCodecContext* codecContext = null;
        SwrContext* swrContext = null;
        AVFrame* frame = null;
        AVPacket* packet = null;

        try {
            // 1. 打开输入文件
            formatContext = ffmpeg.avformat_alloc_context();
            AVFormatContext* formatCtxPtr = formatContext;
            
            int ret = ffmpeg.avformat_open_input(&formatCtxPtr, inputPath, null, null);
            if (ret < 0) {
                _logger.LogError("无法打开输入文件: {InputPath}, 错误码: {ErrorCode}", inputPath, ret);
                return false;
            }
            formatContext = formatCtxPtr;

            // 2. 查找流信息
            ret = ffmpeg.avformat_find_stream_info(formatContext, null);
            if (ret < 0) {
                _logger.LogError("无法找到流信息, 错误码: {ErrorCode}", ret);
                return false;
            }

            // 3. 查找音频流
            int audioStreamIndex = -1;
            for (int i = 0; i < formatContext->nb_streams; i++) {
                if (formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) {
                    audioStreamIndex = i;
                    break;
                }
            }

            if (audioStreamIndex == -1) {
                _logger.LogError("未找到音频流");
                return false;
            }

            AVStream* audioStream = formatContext->streams[audioStreamIndex];
            AVCodecParameters* codecParams = audioStream->codecpar;

            // 4. 查找解码器
            AVCodec* codec = ffmpeg.avcodec_find_decoder(codecParams->codec_id);
            if (codec == null) {
                _logger.LogError("未找到解码器");
                return false;
            }

            // 5. 创建解码器上下文
            codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (codecContext == null) {
                _logger.LogError("无法分配解码器上下文");
                return false;
            }

            ret = ffmpeg.avcodec_parameters_to_context(codecContext, codecParams);
            if (ret < 0) {
                _logger.LogError("无法复制解码器参数, 错误码: {ErrorCode}", ret);
                return false;
            }

            ret = ffmpeg.avcodec_open2(codecContext, codec, null);
            if (ret < 0) {
                _logger.LogError("无法打开解码器, 错误码: {ErrorCode}", ret);
                return false;
            }

            _logger.LogInformation("输入音频: 采样率={SampleRate}Hz, 声道={Channels}, 格式={Format}",
                codecContext->sample_rate, codecContext->ch_layout.nb_channels, codecContext->sample_fmt);

            // 6. 创建重采样上下文
            swrContext = ffmpeg.swr_alloc();
            if (swrContext == null) {
                _logger.LogError("无法分配重采样上下文");
                return false;
            }

            AVChannelLayout outChannelLayout;
            ffmpeg.av_channel_layout_default(&outChannelLayout, channels);

            SwrContext* swrCtxPtr = null;
            ret = ffmpeg.swr_alloc_set_opts2(
                &swrCtxPtr,
                &outChannelLayout,
                AVSampleFormat.AV_SAMPLE_FMT_S16,  // 16-bit PCM
                sampleRate,
                &codecContext->ch_layout,
                codecContext->sample_fmt,
                codecContext->sample_rate,
                0,
                null
            );

            if (ret < 0 || swrCtxPtr == null) {
                _logger.LogError("无法设置重采样选项, 错误码: {ErrorCode}", ret);
                return false;
            }
            swrContext = swrCtxPtr;

            ret = ffmpeg.swr_init(swrContext);
            if (ret < 0) {
                _logger.LogError("无法初始化重采样上下文, 错误码: {ErrorCode}", ret);
                return false;
            }

            // 7. 分配帧和包
            frame = ffmpeg.av_frame_alloc();
            packet = ffmpeg.av_packet_alloc();

            if (frame == null || packet == null) {
                _logger.LogError("无法分配帧或包");
                return false;
            }

            // 8. 打开输出文件
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

            // 9. 读取和解码音频
            int frameCount = 0;
            while (ffmpeg.av_read_frame(formatContext, packet) >= 0) {
                if (packet->stream_index == audioStreamIndex) {
                    ret = ffmpeg.avcodec_send_packet(codecContext, packet);
                    if (ret < 0) {
                        _logger.LogWarning("发送包到解码器失败, 错误码: {ErrorCode}", ret);
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    while (ret >= 0) {
                        ret = ffmpeg.avcodec_receive_frame(codecContext, frame);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) {
                            break;
                        }
                        if (ret < 0) {
                            _logger.LogError("接收帧失败, 错误码: {ErrorCode}", ret);
                            break;
                        }

                        // 10. 重采样
                        int outSamples = (int)ffmpeg.av_rescale_rnd(
                            ffmpeg.swr_get_delay(swrContext, codecContext->sample_rate) + frame->nb_samples,
                            sampleRate,
                            codecContext->sample_rate,
                            AVRounding.AV_ROUND_UP
                        );

                        byte* outBuffer = (byte*)ffmpeg.av_malloc((ulong)(outSamples * channels * sizeof(short)));
                        byte** outBufferPtr = &outBuffer;

                        int convertedSamples = ffmpeg.swr_convert(
                            swrContext,
                            outBufferPtr,
                            outSamples,
                            frame->extended_data,
                            frame->nb_samples
                        );

                        if (convertedSamples > 0) {
                            int dataSize = convertedSamples * channels * sizeof(short);
                            byte[] managedBuffer = new byte[dataSize];
                            Marshal.Copy((IntPtr)outBuffer, managedBuffer, 0, dataSize);
                            
                            outputStream.Write(managedBuffer, 0, dataSize);
                            frameCount++;
                        }

                        ffmpeg.av_free(outBuffer);
                        ffmpeg.av_frame_unref(frame);
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }

            // 11. 刷新重采样器中的剩余数据
            byte* flushBuffer = (byte*)ffmpeg.av_malloc((ulong)(4096 * channels * sizeof(short)));
            byte** flushBufferPtr = &flushBuffer;

            int flushedSamples;
            do {
                flushedSamples = ffmpeg.swr_convert(swrContext, flushBufferPtr, 4096, null, 0);
                if (flushedSamples > 0) {
                    int dataSize = flushedSamples * channels * sizeof(short);
                    byte[] managedBuffer = new byte[dataSize];
                    Marshal.Copy((IntPtr)flushBuffer, managedBuffer, 0, dataSize);
                    outputStream.Write(managedBuffer, 0, dataSize);
                }
            } while (flushedSamples > 0);

            ffmpeg.av_free(flushBuffer);

            _logger.LogInformation("音频转换成功: {FrameCount} 帧, 输出: {OutputPath}", frameCount, outputPath);
            return true;

        } catch (Exception ex) {
            _logger.LogError(ex, "音频转换失败");
            return false;

        } finally {
            // 清理资源
            if (packet != null) {
                AVPacket* pkt = packet;
                ffmpeg.av_packet_free(&pkt);
            }

            if (frame != null) {
                AVFrame* frm = frame;
                ffmpeg.av_frame_free(&frm);
            }

            if (swrContext != null) {
                SwrContext* swr = swrContext;
                ffmpeg.swr_free(&swr);
            }

            if (codecContext != null) {
                AVCodecContext* ctx = codecContext;
                ffmpeg.avcodec_free_context(&ctx);
            }

            if (formatContext != null) {
                AVFormatContext* fmt = formatContext;
                ffmpeg.avformat_close_input(&fmt);
            }
        }
    }

    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            _logger.LogDebug("AudioConverter disposed");
        }
    }
}
