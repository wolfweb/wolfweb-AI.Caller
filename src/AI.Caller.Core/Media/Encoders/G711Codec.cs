using AI.Caller.Core.Media.Interfaces;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;

namespace AI.Caller.Core.Media.Encoders {
    /// <summary>
    /// G.711编解码器（基于FFmpeg）
    /// 使用FFmpeg标准实现，保证音质和稳定性
    /// 支持A-Law和μ-Law编码/解码
    /// </summary>
    public sealed unsafe class G711Codec : IAudioEncoder, IAudioDecoder, IDisposable {
        private readonly ILogger _logger;
        private readonly int _sampleRate;
        private readonly int _channels;
        
        private AVCodecContext* _alawEncoderContext;
        private AVCodecContext* _mulawEncoderContext;
        
        private AVCodecContext* _alawDecoderContext;
        private AVCodecContext* _mulawDecoderContext;
        
        private AVFrame* _encodeFrame;
        private AVPacket* _encodePacket;
        private AVFrame* _decodeFrame;
        private AVPacket* _decodePacket;
        
        private bool _disposed = false;
        
        private const byte ALAW_SILENCE = 0xD5;
        private const byte MULAW_SILENCE = 0xFF;

        public G711Codec(ILogger<G711Codec> logger, int sampleRate = 8000, int channels = 1) {
            _logger = logger;
            _sampleRate = sampleRate;
            _channels = channels;
            
            InitializeCodecs();
            _logger.LogInformation("G711Codec (FFmpeg) initialized: {SampleRate}Hz, {Channels} channel(s)", sampleRate, channels);
        }

        private void InitializeCodecs() {
            InitializeEncoder(ref _alawEncoderContext, "pcm_alaw");
            InitializeEncoder(ref _mulawEncoderContext, "pcm_mulaw");
            
            InitializeDecoder(ref _alawDecoderContext, "pcm_alaw");
            InitializeDecoder(ref _mulawDecoderContext, "pcm_mulaw");
            
            _encodeFrame = ffmpeg.av_frame_alloc();
            _encodePacket = ffmpeg.av_packet_alloc();
            _decodeFrame = ffmpeg.av_frame_alloc();
            _decodePacket = ffmpeg.av_packet_alloc();
            
            if (_encodeFrame == null || _encodePacket == null || _decodeFrame == null || _decodePacket == null) {
                throw new Exception("Failed to allocate AVFrame or AVPacket");
            }
        }

        private void InitializeEncoder(ref AVCodecContext* context, string codecName) {
            var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
            if (codec == null) {
                throw new Exception($"Encoder not found: {codecName}");
            }

            context = ffmpeg.avcodec_alloc_context3(codec);
            if (context == null) {
                throw new Exception($"Failed to allocate encoder context: {codecName}");
            }

            context->sample_rate = _sampleRate;
            context->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            context->ch_layout = new AVChannelLayout { 
                order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, 
                nb_channels = _channels, 
                u = { mask = _channels == 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO } 
            };

            int ret = ffmpeg.avcodec_open2(context, codec, null);
            if (ret < 0) {
                throw new Exception($"Failed to open encoder: {codecName}, error: {GetErrorString(ret)}");
            }
        }

        private void InitializeDecoder(ref AVCodecContext* context, string codecName) {
            var codec = ffmpeg.avcodec_find_decoder_by_name(codecName);
            if (codec == null) {
                throw new Exception($"Decoder not found: {codecName}");
            }

            context = ffmpeg.avcodec_alloc_context3(codec);
            if (context == null) {
                throw new Exception($"Failed to allocate decoder context: {codecName}");
            }

            context->sample_rate = _sampleRate;
            context->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_U8; // G.711输入是8-bit
            context->ch_layout = new AVChannelLayout { 
                order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, 
                nb_channels = _channels, 
                u = { mask = _channels == 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO } 
            };

            int ret = ffmpeg.avcodec_open2(context, codec, null);
            if (ret < 0) {
                throw new Exception($"Failed to open decoder: {codecName}, error: {GetErrorString(ret)}");
            }
        }

        public byte[] EncodeALaw(ReadOnlySpan<byte> pcmBytes) {
            return Encode(pcmBytes, _alawEncoderContext);
        }

        public byte[] EncodeMuLaw(ReadOnlySpan<byte> pcmBytes) {
            return Encode(pcmBytes, _mulawEncoderContext);
        }

        private byte[] Encode(ReadOnlySpan<byte> pcmBytes, AVCodecContext* encoderContext) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(G711Codec));
            }

            if (pcmBytes.Length % 2 != 0) {
                throw new ArgumentException("PCM byte array length must be even for 16-bit audio");
            }

            int numSamples = pcmBytes.Length / 2; // 16-bit samples
            
            _encodeFrame->nb_samples = numSamples;
            _encodeFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
            _encodeFrame->sample_rate = _sampleRate;
            _encodeFrame->ch_layout = encoderContext->ch_layout;

            int ret = ffmpeg.av_frame_get_buffer(_encodeFrame, 0);
            if (ret < 0) {
                _logger.LogError("Failed to allocate frame buffer: {Error}", GetErrorString(ret));
                return Array.Empty<byte>();
            }

            fixed (byte* pcmPtr = pcmBytes) {
                Buffer.MemoryCopy(pcmPtr, _encodeFrame->data[0], pcmBytes.Length, pcmBytes.Length);
            }

            ret = ffmpeg.avcodec_send_frame(encoderContext, _encodeFrame);
            ffmpeg.av_frame_unref(_encodeFrame);
            
            if (ret < 0) {
                _logger.LogError("Failed to send frame to encoder: {Error}", GetErrorString(ret));
                return Array.Empty<byte>();
            }

            ret = ffmpeg.avcodec_receive_packet(encoderContext, _encodePacket);
            if (ret < 0) {
                if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF) {
                    _logger.LogError("Failed to receive packet from encoder: {Error}", GetErrorString(ret));
                }
                return Array.Empty<byte>();
            }

            byte[] encodedData = new byte[_encodePacket->size];
            Marshal.Copy((IntPtr)_encodePacket->data, encodedData, 0, _encodePacket->size);
            
            ffmpeg.av_packet_unref(_encodePacket);
            
            return encodedData;
        }

        public byte[] DecodeG711ALaw(ReadOnlySpan<byte> payload) {
            return Decode(payload, _alawDecoderContext);
        }

        public byte[] DecodeG711MuLaw(ReadOnlySpan<byte> payload) {
            return Decode(payload, _mulawDecoderContext);
        }

        private byte[] Decode(ReadOnlySpan<byte> payload, AVCodecContext* decoderContext) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(G711Codec));
            }

            if (payload.Length == 0) {
                return Array.Empty<byte>();
            }

            fixed (byte* payloadPtr = payload) {
                _decodePacket->data = payloadPtr;
                _decodePacket->size = payload.Length;
            }

            int ret = ffmpeg.avcodec_send_packet(decoderContext, _decodePacket);
            if (ret < 0) {
                _logger.LogError("Failed to send packet to decoder: {Error}", GetErrorString(ret));
                return Array.Empty<byte>();
            }

            ret = ffmpeg.avcodec_receive_frame(decoderContext, _decodeFrame);
            if (ret < 0) {
                if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF) {
                    _logger.LogError("Failed to receive frame from decoder: {Error}", GetErrorString(ret));
                }
                return Array.Empty<byte>();
            }

            int dataSize = ffmpeg.av_samples_get_buffer_size(
                null, 
                _decodeFrame->ch_layout.nb_channels, 
                _decodeFrame->nb_samples, 
                (AVSampleFormat)_decodeFrame->format, 
                1
            );

            if (dataSize <= 0) {
                _logger.LogWarning("Invalid decoded data size: {Size}", dataSize);
                ffmpeg.av_frame_unref(_decodeFrame);
                return Array.Empty<byte>();
            }

            byte[] pcmData = new byte[dataSize];
            Marshal.Copy((IntPtr)_decodeFrame->data[0], pcmData, 0, dataSize);
            
            ffmpeg.av_frame_unref(_decodeFrame);
            
            return pcmData;
        }

        public byte[] GenerateALawSilenceFrame(int sampleCount) {
            var output = new byte[sampleCount];
            Array.Fill(output, ALAW_SILENCE);
            return output;
        }

        public byte[] GenerateMuLawSilenceFrame(int sampleCount) {
            var output = new byte[sampleCount];
            Array.Fill(output, MULAW_SILENCE);
            return output;
        }

        private string GetErrorString(int error) {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"Unknown error: {error}";
        }

        public void Dispose() {
            if (_disposed) return;

            if (_encodeFrame != null) {
                fixed (AVFrame** frame = &_encodeFrame) {
                    ffmpeg.av_frame_free(frame);
                }
            }
            if (_decodeFrame != null) {
                fixed (AVFrame** frame = &_decodeFrame) {
                    ffmpeg.av_frame_free(frame);
                }
            }
            if (_encodePacket != null) {
                fixed (AVPacket** packet = &_encodePacket) {
                    ffmpeg.av_packet_free(packet);
                }
            }
            if (_decodePacket != null) {
                fixed (AVPacket** packet = &_decodePacket) {
                    ffmpeg.av_packet_free(packet);
                }
            }

            if (_alawEncoderContext != null) {
                fixed (AVCodecContext** ctx = &_alawEncoderContext) {
                    ffmpeg.avcodec_free_context(ctx);
                }
            }
            if (_mulawEncoderContext != null) {
                fixed (AVCodecContext** ctx = &_mulawEncoderContext) {
                    ffmpeg.avcodec_free_context(ctx);
                }
            }

            if (_alawDecoderContext != null) {
                fixed (AVCodecContext** ctx = &_alawDecoderContext) {
                    ffmpeg.avcodec_free_context(ctx);
                }
            }
            if (_mulawDecoderContext != null) {
                fixed (AVCodecContext** ctx = &_mulawDecoderContext) {
                    ffmpeg.avcodec_free_context(ctx);
                }
            }

            _disposed = true;
            _logger.LogDebug("G711Codec disposed");
        }
    }
}
