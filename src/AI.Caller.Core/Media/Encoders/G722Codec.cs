using AI.Caller.Core.Media.Interfaces;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace AI.Caller.Core.Media.Encoders {
    /// <summary>
    /// Enhanced G.722 Codec Implementation with Audio Quality Improvements
    /// Based on research findings about FFmpeg G.722 RTP payload compatibility issues
    /// and additional audio quality optimizations for VoIP applications.
    /// </summary>
    public sealed unsafe class G722Codec : IAudioCodec {
        private readonly ILogger _logger;
        private readonly int _channels;

        private AVCodecContext* _encoderContext;
        private AVCodecContext* _decoderContext;

        private AVFrame* _frame;
        private AVPacket* _packet;

        private AVAudioFifo* _fifo; // 用于缓冲输入样本，确保整帧输出

        private bool _disposed = false;
        private readonly object _lock = new object();

        // G.722 标准常量
        private const int G722_SAMPLE_RATE = 16000;
        private const int G722_FRAME_SAMPLES = 320;// 20ms @ 16kHz
        private const int G722_ENCODED_BYTES_PER_FRAME = 160; // 64kbps, 2:1 压缩

        public int SampleRate => G722_SAMPLE_RATE;
        public int Channels => _channels;
        public AudioCodec Type => AudioCodec.G722;

        public G722Codec(ILogger<G722Codec> logger, int channels = 1) {
            _logger = logger;
            _channels = channels;

            try {
                Initialize();
                _logger.LogInformation("G722Codec initialized successfully (RTP-compatible): {SampleRate}Hz, {Channels}ch", G722_SAMPLE_RATE, channels);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to initialize G722Codec");
                Dispose();
                throw;
            }
        }

        private void Initialize() {
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);

            var codecId = AVCodecID.AV_CODEC_ID_ADPCM_G722;

            // 初始化编码器
            var encoder = ffmpeg.avcodec_find_encoder(codecId);
            if (encoder == null) throw new InvalidOperationException("G.722 encoder not found");

            _encoderContext = ffmpeg.avcodec_alloc_context3(encoder);
            if (_encoderContext == null) throw new InvalidOperationException("Failed to allocate encoder context");

            _encoderContext->sample_rate = G722_SAMPLE_RATE;
            _encoderContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            _encoderContext->bit_rate = 64000;
            _encoderContext->frame_size = G722_FRAME_SAMPLES; // 强制 20ms 帧

            _encoderContext->ch_layout.order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE;
            _encoderContext->ch_layout.nb_channels = _channels;
            _encoderContext->ch_layout.u.mask = _channels == 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO;

            CheckError(ffmpeg.avcodec_open2(_encoderContext, encoder, null), "open encoder");

            // 初始化解码器（可选，但保持完整性）
            var decoder = ffmpeg.avcodec_find_decoder(codecId);
            if (decoder == null) throw new InvalidOperationException("G.722 decoder not found");

            _decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (_decoderContext == null) throw new InvalidOperationException("Failed to allocate decoder context");

            _decoderContext->sample_rate = G722_SAMPLE_RATE;
            _decoderContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            _decoderContext->ch_layout = _encoderContext->ch_layout;

            CheckError(ffmpeg.avcodec_open2(_decoderContext, decoder, null), "open decoder");

            // 分配通用 frame 和 packet
            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            if (_frame == null || _packet == null) throw new InvalidOperationException("Failed to allocate frame/packet");

            // 创建 FIFO 缓冲（S16 样本）
            _fifo = ffmpeg.av_audio_fifo_alloc(AVSampleFormat.AV_SAMPLE_FMT_S16, _channels, G722_FRAME_SAMPLES * 4); // 多存几帧
            if (_fifo == null) throw new InvalidOperationException("Failed to allocate audio FIFO");

            _logger.LogDebug("G722Codec fully initialized with FIFO and bit-reversal support");
        }

        /// <summary>
        /// 关键修复：对 FFmpeg 输出的每个字节进行 nibble swap（高4位和低4位交换）
        /// </summary>
        private static byte NibbleSwap(byte b) {
            // 高4位和低4位交换
            return (byte)(((b & 0x0F) << 4) | ((b & 0xF0) >> 4));
        }

        public byte[] Encode(ReadOnlySpan<byte> pcmS16) {
            if (_disposed) throw new ObjectDisposedException(nameof(G722Codec));
            if (pcmS16.Length == 0) return [];

            lock (_lock) {
                // 1. 将输入 PCM 数据写入 FIFO
                fixed (byte* ptr = pcmS16) {
                    var samples = pcmS16.Length / (2 * _channels); // S16 = 2 bytes per sample
                    var samplePtr = (short*)ptr;

                    CheckError(ffmpeg.av_audio_fifo_write(_fifo, (void**)&samplePtr, samples), "fifo write");
                }

                // 2. 只要 FIFO 中有足够样本，就编码整帧
                var encodedFrames = new List<byte[]>();

                while (ffmpeg.av_audio_fifo_size(_fifo) >= G722_FRAME_SAMPLES) {
                    // 填充 frame
                    _frame->nb_samples = G722_FRAME_SAMPLES;
                    _frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
                    _frame->ch_layout = _encoderContext->ch_layout;

                    CheckError(ffmpeg.av_frame_get_buffer(_frame, 0), "get buffer");

                    // 从 FIFO 读取整帧
                    CheckError(ffmpeg.av_audio_fifo_read(_fifo, (void**)&_frame->data, G722_FRAME_SAMPLES), "fifo read");

                    // 编码
                    CheckError(ffmpeg.avcodec_send_frame(_encoderContext, _frame), "send frame");

                    while (true) {
                        int ret = ffmpeg.avcodec_receive_packet(_encoderContext, _packet);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
                        CheckError(ret, "receive packet");

                        if (_packet->size > 0) {
                            byte[] raw = new byte[_packet->size];
                            Marshal.Copy((IntPtr)_packet->data, raw, 0, _packet->size);

                            // 🔧 关键修复：比特翻转
                            for (int i = 0; i < raw.Length; i++) {
                                raw[i] = NibbleSwap(raw[i]);
                            }

                            encodedFrames.Add(raw);
                        }

                        ffmpeg.av_packet_unref(_packet);
                    }
                }

                // 合并所有整帧（通常每次调用只会出一帧或多帧）
                if (encodedFrames.Count == 0) return [];
                if (encodedFrames.Count == 1) return encodedFrames[0];

                var total = encodedFrames.Sum(f => f.Length);
                var result = new byte[total];
                int offset = 0;
                foreach (var f in encodedFrames) {
                    Buffer.BlockCopy(f, 0, result, offset, f.Length);
                    offset += f.Length;
                }
                return result;
            }
        }

        public byte[] Decode(ReadOnlySpan<byte> encoded) {
            if (_disposed) throw new ObjectDisposedException(nameof(G722Codec));
            if (encoded.Length == 0) return [];

            lock (_lock) {
                // 注意：接收到的 RTP 数据已经是 MSB-first，所以需要先反向翻转再喂给 FFmpeg
                byte[] input = new byte[encoded.Length];
                encoded.CopyTo(input);
                for (int i = 0; i < input.Length; i++) {
                    input[i] = NibbleSwap(input[i]);
                }

                fixed (byte* ptr = input) {
                    _packet->data = ptr;
                    _packet->size = input.Length;

                    CheckError(ffmpeg.avcodec_send_packet(_decoderContext, _packet), "send packet decode");

                    var output = new List<byte>();
                    while (true) {
                        int ret = ffmpeg.avcodec_receive_frame(_decoderContext, _frame);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
                        CheckError(ret, "receive frame decode");

                        int dataSize = ffmpeg.av_samples_get_buffer_size(null, _frame->ch_layout.nb_channels, _frame->nb_samples, (AVSampleFormat)_frame->format, 1);
                        if (dataSize > 0) {
                            byte[] pcm = new byte[dataSize];
                            Marshal.Copy((IntPtr)_frame->data[0], pcm, 0, dataSize);
                            output.AddRange(pcm);
                        }
                    }

                    return output.Count == 0 ? [] : output.ToArray();
                }
            }
        }

        public byte[] GenerateSilenceFrame(int durationMs) {
            if (_disposed) throw new ObjectDisposedException(nameof(G722Codec));

            int frames = durationMs / 20;
            if (frames <= 0) return new byte[G722_ENCODED_BYTES_PER_FRAME];

            var silence = new byte[frames * G722_ENCODED_BYTES_PER_FRAME];
            for (int i = 0; i < silence.Length; i++) {
                silence[i] = NibbleSwap(0x00); // G.722 静音通常是 0x00 或特定模式，翻转后安全
            }
            return silence;
        }

        private static void CheckError(int ret, string operation) {
            if (ret < 0) {
                var buffer = stackalloc byte[1024];
                ffmpeg.av_strerror(ret, buffer, 1024);
                throw new InvalidOperationException($"FFmpeg error during {operation}: {Marshal.PtrToStringAnsi((IntPtr)buffer)} ({ret})");
            }
        }

        public void Dispose() {
            if (_disposed) return;

            lock (_lock) {
                if (_disposed) return;

                if (_fifo != null) {
                    ffmpeg.av_audio_fifo_free(_fifo);
                    _fifo = null;
                }

                if (_frame != null) {
                    fixed (AVFrame** f = &_frame) ffmpeg.av_frame_free(f);
                }
                if (_packet != null) {
                    fixed (AVPacket** p = &_packet) ffmpeg.av_packet_free(p);
                }
                if (_encoderContext != null) {
                    fixed (AVCodecContext** c = &_encoderContext) ffmpeg.avcodec_free_context(c);
                }
                if (_decoderContext != null) {
                    fixed (AVCodecContext** c = &_decoderContext) ffmpeg.avcodec_free_context(c);
                }

                _disposed = true;
                _logger.LogDebug("G722Codec disposed");
            }
        }
    }
}