using Microsoft.Extensions.Logging;
using FFmpeg.AutoGen;

namespace AI.Caller.Core.Media {
    public unsafe class AudioResamplerCrossType<TInput, TOutput> : IDisposable {
        private readonly ILogger _logger;
        private readonly int _inputSampleRate;
        private readonly int _outputSampleRate;
        private readonly AVSampleFormat _inputSampleFormat;
        private readonly AVSampleFormat _outputSampleFormat;

        private SwrContext* _swrContext;
        private bool _disposed;

        public AudioResamplerCrossType(int inputSampleRate, int outputSampleRate, ILogger logger) {
            _logger = logger;
            _inputSampleRate = inputSampleRate;
            _outputSampleRate = outputSampleRate;

            if (typeof(TInput) == typeof(float))
                _inputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
            else if (typeof(TInput) == typeof(short))
                _inputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            else
                throw new InvalidOperationException($"Unsupported input type: {typeof(TInput)}");

            if (typeof(TOutput) == typeof(float))
                _outputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
            else if (typeof(TOutput) == typeof(short))
                _outputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            else if (typeof(TOutput) == typeof(byte))
                _outputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16; // byte作为S16处理
            else
                throw new InvalidOperationException($"Unsupported output type: {typeof(TOutput)}");

            if (_inputSampleRate != _outputSampleRate || _inputSampleFormat != _outputSampleFormat) {
                try {
                    InitializeSwrContext();
                    _logger.LogDebug($"AudioResamplerCrossType<{typeof(TInput).Name},{typeof(TOutput).Name}> initialized: {inputSampleRate}Hz -> {outputSampleRate}Hz");
                } catch (Exception ex) {
                    _logger.LogWarning(ex, $"Failed to initialize FFmpeg cross-type resampler");
                    _swrContext = null;
                }
            }
        }

        private void InitializeSwrContext() {
            SwrContext* ctx = ffmpeg.swr_alloc();
            if (ctx == null)
                throw new InvalidOperationException("Failed to allocate SWR context");

            AVChannelLayout inCh, outCh;
            ffmpeg.av_channel_layout_default(&inCh, 1);   // mono
            ffmpeg.av_channel_layout_default(&outCh, 1);  // mono

            SwrContext* configured = null;
            int ret = ffmpeg.swr_alloc_set_opts2(
                &configured,
                &outCh, _outputSampleFormat, _outputSampleRate,
                &inCh, _inputSampleFormat, _inputSampleRate,
                0, null
            );

            if (ret < 0 || configured == null)
                throw new InvalidOperationException($"Failed to set SWR options: {ret}");

            _swrContext = configured;

            ret = ffmpeg.swr_init(_swrContext);
            if (ret < 0)
                throw new InvalidOperationException($"Failed to initialize SWR context: {ret}");
        }

        public TOutput[] Resample(TInput[] input) {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioResamplerCrossType<TInput, TOutput>));
            
            if ((_inputSampleRate == _outputSampleRate && _inputSampleFormat == _outputSampleFormat) || _swrContext == null) {
                if (typeof(TInput) == typeof(TOutput)) {
                    return (TOutput[])(object)input;
                }
                return ConvertFormat(input);
            }

            try {
                int inputSamples = input.Length;
                int outputSamples = (int)ffmpeg.av_rescale_rnd(inputSamples, _outputSampleRate, _inputSampleRate, AVRounding.AV_ROUND_UP);

                TOutput[] output;
                if (typeof(TOutput) == typeof(byte)) {
                    output = new TOutput[outputSamples * 2]; // byte需要*2
                } else {
                    output = new TOutput[outputSamples];
                }

                fixed (TInput* inputPtr = input)
                fixed (TOutput* outputPtr = output) {
                    byte* inputData = (byte*)inputPtr;
                    byte* outputData = (byte*)outputPtr;
                    byte** inputDataPtr = &inputData;
                    byte** outputDataPtr = &outputData;

                    int converted = ffmpeg.swr_convert(_swrContext, outputDataPtr, outputSamples, inputDataPtr, inputSamples);
                    if (converted < 0)
                        throw new InvalidOperationException($"Cross-type resampling failed: {converted}");

                    if (typeof(TOutput) == typeof(byte)) {
                        if (converted * 2 != output.Length) {
                            Array.Resize(ref output, converted * 2);
                        }
                    } else {
                        if (converted != output.Length) {
                            Array.Resize(ref output, converted);
                        }
                    }
                }

                return output;
            } catch (Exception ex) {
                _logger.LogError(ex, "FFmpeg cross-type resampling failed, falling back to format conversion");
                return ConvertFormat(input);
            }
        }

        private TOutput[] ConvertFormat(TInput[] input) {
            if (typeof(TInput) == typeof(float) && typeof(TOutput) == typeof(byte)) {
                var floatInput = (float[])(object)input;
                var byteOutput = new byte[floatInput.Length * 2];
                
                for (int i = 0; i < floatInput.Length; i++) {
                    float sample = Math.Clamp(floatInput[i], -1f, 1f);
                    short shortSample = (short)(sample * 32767f);
                    
                    int byteIndex = i * 2;
                    byteOutput[byteIndex] = (byte)(shortSample & 0xFF);
                    byteOutput[byteIndex + 1] = (byte)((shortSample >> 8) & 0xFF);
                }
                
                return (TOutput[])(object)byteOutput;
            }
            
            throw new InvalidOperationException($"Unsupported format conversion: {typeof(TInput)} -> {typeof(TOutput)}");
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
                _logger.LogDebug("AudioResamplerCrossType disposed");
            }
        }
    }
}