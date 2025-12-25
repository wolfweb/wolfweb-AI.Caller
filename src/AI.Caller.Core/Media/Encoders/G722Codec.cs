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
        
        private AVFrame* _encodeFrame;
        private AVPacket* _encodePacket;
        private AVFrame* _decodeFrame;
        private AVPacket* _decodePacket;
        
        private bool _disposed = false;
        private readonly object _codecLock = new object();
        
        // G.722 constants - for RTP compatibility
        private const int G722_SAMPLE_RATE = 16000;
        private const int G722_FRAME_SIZE = 320; // 20ms at 16kHz
        private const int G722_ENCODED_FRAME_SIZE = 160; // G.722 compresses 2:1
        
        public int SampleRate => G722_SAMPLE_RATE;
        public int Channels => _channels;
        public AudioCodec Type => AudioCodec.G722;

        public G722Codec(ILogger<G722Codec> logger, int channels = 1) {
            _logger = logger;
            _channels = channels;
            
            try {
                InitializeCodecs();
                _logger.LogInformation("G722Codec initialized successfully: {SampleRate}Hz, {Channels} channel(s)", 
                    G722_SAMPLE_RATE, channels);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to initialize G722Codec");
                Dispose();
                throw;
            }
        }

        private void InitializeCodecs() {
            try {
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
                
                InitializeEncoder();
                InitializeDecoder();
                
                _encodeFrame = ffmpeg.av_frame_alloc();
                _encodePacket = ffmpeg.av_packet_alloc();
                _decodeFrame = ffmpeg.av_frame_alloc();
                _decodePacket = ffmpeg.av_packet_alloc();
                
                if (_encodeFrame == null || _encodePacket == null || _decodeFrame == null || _decodePacket == null) {
                    throw new Exception("Failed to allocate AVFrame or AVPacket for G722");
                }
                
                _logger.LogDebug("G722 codec frames and packets allocated successfully");
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to initialize G722 codecs");
                throw;
            }
        }

        private void InitializeEncoder() {
            var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_ADPCM_G722);
            if (codec == null) {
                throw new Exception("G.722 encoder not found - ensure FFmpeg is properly installed with G.722 support");
            }

            _encoderContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_encoderContext == null) {
                throw new Exception("Failed to allocate G722 encoder context");
            }

            // 🔧 IMPROVED: Enhanced G.722 encoder configuration for better audio quality
            _encoderContext->sample_rate = G722_SAMPLE_RATE;
            _encoderContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            _encoderContext->bit_rate = 64000; // Standard G.722 bit rate
            
            // 🔧 QUALITY IMPROVEMENT: Enhanced channel layout setup
            _encoderContext->ch_layout.order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE;
            _encoderContext->ch_layout.nb_channels = _channels;
            if (_channels == 1) {
                _encoderContext->ch_layout.u.mask = ffmpeg.AV_CH_LAYOUT_MONO;
            } else {
                _encoderContext->ch_layout.u.mask = ffmpeg.AV_CH_LAYOUT_STEREO;
            }
                        
            int ret = ffmpeg.avcodec_open2(_encoderContext, codec, null);
            if (ret < 0) {
                throw new Exception($"Failed to open G722 encoder: {GetErrorString(ret)}");
            }
            
            _logger.LogDebug("🔧 G722 enhanced encoder initialized - frame_size: {FrameSize}, bit_rate: {BitRate}, compression_level: {CompressionLevel}", 
                _encoderContext->frame_size, _encoderContext->bit_rate, _encoderContext->compression_level);
        }

        private void InitializeDecoder() {
            var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_ADPCM_G722);
            if (codec == null) {
                throw new Exception("G.722 decoder not found - ensure FFmpeg is properly installed with G.722 support");
            }

            _decoderContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_decoderContext == null) {
                throw new Exception("Failed to allocate G722 decoder context");
            }

            // 🔧 IMPROVED: Enhanced G.722 decoder configuration for better audio quality
            _decoderContext->sample_rate = G722_SAMPLE_RATE;
            _decoderContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            
            // 🔧 QUALITY IMPROVEMENT: Enhanced channel layout setup
            _decoderContext->ch_layout.order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE;
            _decoderContext->ch_layout.nb_channels = _channels;
            if (_channels == 1) {
                _decoderContext->ch_layout.u.mask = ffmpeg.AV_CH_LAYOUT_MONO;
            } else {
                _decoderContext->ch_layout.u.mask = ffmpeg.AV_CH_LAYOUT_STEREO;
            }

            int ret = ffmpeg.avcodec_open2(_decoderContext, codec, null);
            if (ret < 0) {
                throw new Exception($"Failed to open G722 decoder: {GetErrorString(ret)}");
            }
            
            _logger.LogDebug("🔧 G722 enhanced decoder initialized - frame_size: {FrameSize}, error_concealment: {ErrorConcealment}", _decoderContext->frame_size, _decoderContext->error_concealment);
        }

        public byte[] Encode(ReadOnlySpan<byte> pcm16) {
            if (_disposed) throw new ObjectDisposedException(nameof(G722Codec));

            if (pcm16.Length == 0) {
                return [];
            }

            if (pcm16.Length % 2 != 0) {
                throw new ArgumentException("PCM byte array length must be even for 16-bit audio");
            }

            lock (_codecLock) {
                try {
                    int numSamples = pcm16.Length / (2 * _channels); // 16-bit samples
                    
                    int expectedFrameSize = _encoderContext->frame_size > 0 ? _encoderContext->frame_size : G722_FRAME_SIZE;
                    
                    _logger.LogTrace("🎤 G722 Encode Input: {Bytes} bytes, {Samples} samples, FFmpeg frame_size: {FrameSize}", 
                        pcm16.Length, numSamples, expectedFrameSize);
                    
                    if (numSamples > expectedFrameSize) {
                        // Split into multiple frames
                        var results = new List<byte[]>();
                        int offset = 0;
                        
                        while (offset < pcm16.Length) {
                            int frameBytes = Math.Min(expectedFrameSize * 2 * _channels, pcm16.Length - offset);
                            var frameData = pcm16.Slice(offset, frameBytes);
                            var encoded = EncodeFrame(frameData);
                            if (encoded.Length > 0) {
                                results.Add(encoded);
                            }
                            offset += frameBytes;
                        }
                        
                        // Concatenate all encoded frames
                        if (results.Count > 0) {
                            var totalLength = results.Sum(r => r.Length);
                            var combined = new byte[totalLength];
                            int pos = 0;
                            foreach (var result in results) {
                                Array.Copy(result, 0, combined, pos, result.Length);
                                pos += result.Length;
                            }
                            return combined;
                        }
                        return [];
                    } else {
                        return EncodeFrame(pcm16);
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "💥 Exception during G722 encoding");
                    return [];
                }
            }
        }

        private byte[] EncodeFrame(ReadOnlySpan<byte> pcm16) {
            int numSamples = pcm16.Length / (2 * _channels);
            
            _encodeFrame->nb_samples = numSamples;
            _encodeFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
            _encodeFrame->sample_rate = SampleRate;
            _encodeFrame->ch_layout = _encoderContext->ch_layout;

            int ret = ffmpeg.av_frame_make_writable(_encodeFrame);

            if (ret < 0) {
                ffmpeg.av_frame_unref(_encodeFrame);
                _encodeFrame->nb_samples = numSamples;
                _encodeFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
                _encodeFrame->sample_rate = SampleRate;
                _encodeFrame->ch_layout = _encoderContext->ch_layout;

                ret = ffmpeg.av_frame_get_buffer(_encodeFrame, 0);
                if (ret < 0) {
                    _logger.LogError("❌ Failed to allocate G722 encode frame buffer: {Error}", GetErrorString(ret));
                    return [];
                }
                ret = ffmpeg.av_frame_make_writable(_encodeFrame);
                if (ret < 0) {
                    _logger.LogError("❌ Failed to make G722 encode frame writable: {Error}", GetErrorString(ret));
                    return [];
                }
            }

            fixed (byte* pcmPtr = pcm16) {
                var frameDataSize = Math.Min(pcm16.Length, _encodeFrame->linesize[0]);
                Buffer.MemoryCopy(pcmPtr, _encodeFrame->data[0], frameDataSize, frameDataSize);
            }

            ret = ffmpeg.avcodec_send_frame(_encoderContext, _encodeFrame);
            if (ret < 0) {
                _logger.LogError("❌ G722 Encode: Failed to send frame: {Error}", GetErrorString(ret));
                return [];
            }

            ffmpeg.av_packet_unref(_encodePacket);

            ret = ffmpeg.avcodec_receive_packet(_encoderContext, _encodePacket);
            if (ret < 0) {
                if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF) {
                    _logger.LogError("❌ G722 Encode: Failed to receive packet: {Error}", GetErrorString(ret));
                }
                return [];
            }

            if (_encodePacket->size <= 0) return [];

            byte[] encodedData = new byte[_encodePacket->size];
            Marshal.Copy((IntPtr)_encodePacket->data, encodedData, 0, _encodePacket->size);

            return encodedData;
        }

        public byte[] Decode(ReadOnlySpan<byte> encoded) {
            if (_disposed) throw new ObjectDisposedException(nameof(G722Codec));
            
            if (encoded.Length == 0) {
                return [];
            }

            lock (_codecLock) {
                try {
                    if (_decodePacket->data == null || _decodePacket->size < encoded.Length) {
                        ffmpeg.av_packet_unref(_decodePacket);
                        int allocRet = ffmpeg.av_new_packet(_decodePacket, encoded.Length);
                        if (allocRet < 0) {
                            _logger.LogError("❌ Failed to allocate packet for decoding");
                            return [];
                        }
                    }
                                        
                    fixed (byte* payloadPtr = encoded) {
                        Buffer.MemoryCopy(payloadPtr, _decodePacket->data, encoded.Length, encoded.Length);
                    }

                    _decodePacket->size = encoded.Length;

                    int ret = ffmpeg.avcodec_send_packet(_decoderContext, _decodePacket);
                    if (ret < 0) {
                        _logger.LogError("❌ Failed to send packet to decoder: {Error}", GetErrorString(ret));
                        return [];
                    }

                    ret = ffmpeg.avcodec_receive_frame(_decoderContext, _decodeFrame);
                    if (ret < 0) {
                        if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF) {
                            _logger.LogError("❌ Failed to receive frame: {Error}", GetErrorString(ret));
                        }
                        return [];
                    }

                    if (_decodeFrame->nb_samples <= 0) return [];

                    int dataSize = ffmpeg.av_samples_get_buffer_size(null, _decodeFrame->ch_layout.nb_channels, _decodeFrame->nb_samples, (AVSampleFormat)_decodeFrame->format, 1);

                    if (dataSize <= 0) return [];

                    byte[] pcmData = new byte[dataSize];
                    Marshal.Copy((IntPtr)_decodeFrame->data[0], pcmData, 0, dataSize);

                    return pcmData;
                } catch (Exception ex) {
                    _logger.LogError(ex, "💥 Exception during G722 enhanced decoding");
                    return [];
                }
            }
        }

        public byte[] GenerateSilenceFrame(int durationMs) {
            if (_disposed) throw new ObjectDisposedException(nameof(G722Codec));
            
            try {
                int samples = (SampleRate * durationMs) / 1000;
                var silencePcm = new byte[samples * 2 * _channels]; // 16-bit samples
                Array.Fill(silencePcm, (byte)0);
                
                var encoded = Encode(silencePcm);
                _logger.LogTrace("Generated G722 silence frame: {Duration}ms -> {Samples} samples -> {EncodedBytes} bytes", 
                    durationMs, samples, encoded.Length);
                
                return encoded;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to generate G722 silence frame");
                return new byte[G722_ENCODED_FRAME_SIZE]; // Return standard size silence frame
            }
        }

        private string GetErrorString(int error) {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"Unknown error: {error}";
        }

        public void Dispose() {
            if (_disposed) return;

            lock (_codecLock) {
                if (_disposed) return;

                try {
                    // Free frames
                    if (_encodeFrame != null) {
                        fixed (AVFrame** frame = &_encodeFrame) {
                            ffmpeg.av_frame_free(frame);
                        }
                        _encodeFrame = null;
                    }
                    
                    if (_decodeFrame != null) {
                        fixed (AVFrame** frame = &_decodeFrame) {
                            ffmpeg.av_frame_free(frame);
                        }
                        _decodeFrame = null;
                    }

                    // Free packets
                    if (_encodePacket != null) {
                        fixed (AVPacket** packet = &_encodePacket) {
                            ffmpeg.av_packet_free(packet);
                        }
                        _encodePacket = null;
                    }
                    
                    if (_decodePacket != null) {
                        fixed (AVPacket** packet = &_decodePacket) {
                            ffmpeg.av_packet_free(packet);
                        }
                        _decodePacket = null;
                    }

                    // Free codec contexts
                    if (_encoderContext != null) {
                        fixed (AVCodecContext** ctx = &_encoderContext) {
                            ffmpeg.avcodec_free_context(ctx);
                        }
                        _encoderContext = null;
                    }
                    
                    if (_decoderContext != null) {
                        fixed (AVCodecContext** ctx = &_decoderContext) {
                            ffmpeg.avcodec_free_context(ctx);
                        }
                        _decoderContext = null;
                    }

                    _logger.LogDebug("G722Codec disposed successfully");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error during G722Codec disposal");
                } finally {
                    _disposed = true;
                }
            }
        }
    }
}