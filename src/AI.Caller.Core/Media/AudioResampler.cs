using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using FFmpeg.AutoGen;

namespace AI.Caller.Core.Media {
    public unsafe class AudioResampler : IDisposable {
        private readonly ILogger _logger;
        private readonly int _inputSampleRate;
        private readonly int _outputSampleRate;
        private SwrContext* _swrContext;
        private bool _disposed;

        public AudioResampler(int inputSampleRate, int outputSampleRate, ILogger logger) {
            _inputSampleRate = inputSampleRate;
            _outputSampleRate = outputSampleRate;
            _logger = logger;
            
            if (_inputSampleRate != _outputSampleRate) {
                try {
                    InitializeSwrContext();
                    _logger.LogDebug($"AudioResampler initialized with FFmpeg.AutoGen: {inputSampleRate}Hz -> {outputSampleRate}Hz");
                } catch (Exception ex) {
                    _logger.LogWarning(ex, $"Failed to initialize FFmpeg resampler, will use passthrough");
                    _swrContext = null;
                }
            }
        }

        private void InitializeSwrContext() {
            _swrContext = ffmpeg.swr_alloc();
            if (_swrContext == null)
                throw new InvalidOperationException("Failed to allocate SWR context");

            ffmpeg.av_opt_set_int(_swrContext, "in_channel_layout", (long)ffmpeg.AV_CH_LAYOUT_MONO, 0);
            ffmpeg.av_opt_set_int(_swrContext, "out_channel_layout", (long)ffmpeg.AV_CH_LAYOUT_MONO, 0);
            ffmpeg.av_opt_set_int(_swrContext, "in_sample_rate", _inputSampleRate, 0);
            ffmpeg.av_opt_set_int(_swrContext, "out_sample_rate", _outputSampleRate, 0);
            ffmpeg.av_opt_set_sample_fmt(_swrContext, "in_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
            ffmpeg.av_opt_set_sample_fmt(_swrContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);

            int ret = ffmpeg.swr_init(_swrContext);
            if (ret < 0)
                throw new InvalidOperationException($"Failed to initialize SWR context: {ret}");
        }

        public short[] Resample(short[] input) {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioResampler));
            
            if (_inputSampleRate == _outputSampleRate || _swrContext == null) {
                return input;
            }

            try {
                int inputSamples = input.Length;
                int outputSamples = (int)ffmpeg.av_rescale_rnd(inputSamples, _outputSampleRate, _inputSampleRate, AVRounding.AV_ROUND_UP);

                var output = new short[outputSamples];

                fixed (short* inputPtr = input)
                fixed (short* outputPtr = output) {
                    byte* inputData = (byte*)inputPtr;
                    byte* outputData = (byte*)outputPtr;
                    byte** inputDataPtr = &inputData;
                    byte** outputDataPtr = &outputData;

                    int converted = ffmpeg.swr_convert(_swrContext, outputDataPtr, outputSamples, inputDataPtr, inputSamples);
                    if (converted < 0)
                        throw new InvalidOperationException($"Resampling failed: {converted}");

                    if (converted != outputSamples) {
                        Array.Resize(ref output, converted);
                    }
                }

                return output;
            } catch (Exception ex) {
                _logger.LogError(ex, "FFmpeg resampling failed, returning original audio");
                return input;
            }
        }



        public void Dispose() {
            if (!_disposed) {
                if (_swrContext != null) {
                    fixed (SwrContext** swrContext = &_swrContext) {
                        ffmpeg.swr_free(swrContext);
                        _swrContext = null;
                    }
                }
                _disposed = true;
                _logger.LogDebug("AudioResampler disposed");
            }
        }
    }
}