using Microsoft.Extensions.Logging;
using FFmpeg.AutoGen;

namespace AI.Caller.Core.Media {
    public unsafe class AudioResampler<T> : IDisposable {
        private readonly ILogger _logger;
        private readonly int _inputSampleRate;
        private readonly int _outputSampleRate;
        private readonly AVSampleFormat _sampleFormat;

        private T[] _internalBuffer = Array.Empty<T>();
        private SwrContext* _swrContext;
        private bool _disposed;

        public AudioResampler(int inputSampleRate, int outputSampleRate, ILogger logger) {
            _logger = logger;
            _inputSampleRate = inputSampleRate;
            _outputSampleRate = outputSampleRate;

            if (typeof(T) == typeof(float))
                _sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
            else if (typeof(T) == typeof(short))
                _sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            else if (typeof(T) == typeof(byte))
                _sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            else
                throw new InvalidOperationException($"Unsupported type: {typeof(T)}. Supported types are float, short, byte.");

            if (_inputSampleRate != _outputSampleRate) {
                try {
                    InitializeSwrContext();
                    _logger.LogDebug($"AudioResampler<{typeof(T).Name}> initialized with FFmpeg.AutoGen: {inputSampleRate}Hz -> {outputSampleRate}Hz");
                } catch (Exception ex) {
                    _logger.LogWarning(ex, $"Failed to initialize FFmpeg resampler, will use passthrough");
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
                &outCh, _sampleFormat, _outputSampleRate,
                &inCh, _sampleFormat, _inputSampleRate,
                0, null
            );

            if (ret < 0 || configured == null)
                throw new InvalidOperationException($"Failed to set SWR options: {ret}");

            _swrContext = configured;

            ret = ffmpeg.swr_init(_swrContext);
            if (ret < 0)
                throw new InvalidOperationException($"Failed to initialize SWR context: {ret}");
        }


        public ArraySegment<T> Resample(T[] input) {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioResampler<T>));
            
            if (_inputSampleRate == _outputSampleRate || _swrContext == null) {
                return new ArraySegment<T>(input);
            }

            try {
                int inputSamples;
                int outputSamples;
                
                if (typeof(T) == typeof(byte)) {
                    inputSamples = input.Length / 2;
                    outputSamples = (int)ffmpeg.av_rescale_rnd(inputSamples, _outputSampleRate, _inputSampleRate, AVRounding.AV_ROUND_UP);
                } else {
                    inputSamples = input.Length;
                    outputSamples = (int)ffmpeg.av_rescale_rnd(inputSamples, _outputSampleRate, _inputSampleRate, AVRounding.AV_ROUND_UP);
                }

                int requiredSize;
                if (typeof(T) == typeof(byte)) {
                    requiredSize = outputSamples * 2;
                } else {
                    requiredSize = outputSamples;
                }

                if (_internalBuffer.Length < requiredSize) {
                    _internalBuffer = new T[requiredSize]; // Initial or Resize alloc
                }

                fixed (T* inputPtr = input)
                fixed (T* outputPtr = _internalBuffer) {
                    byte* inputData = (byte*)inputPtr;
                    byte* outputData = (byte*)outputPtr;
                    byte** inputDataPtr = &inputData;
                    byte** outputDataPtr = &outputData;

                    int converted = ffmpeg.swr_convert(_swrContext, outputDataPtr, outputSamples, inputDataPtr, inputSamples);
                    if (converted < 0)
                        throw new InvalidOperationException($"Resampling failed: {converted}");

                    int finalLength;
                    if (typeof(T) == typeof(byte)) {
                        finalLength = converted * 2;
                    } else {
                        finalLength = converted;
                    }
                    
                    return new ArraySegment<T>(_internalBuffer, 0, finalLength);
                }

            } catch (Exception ex) {
                _logger.LogError(ex, "FFmpeg resampling failed, returning original audio");
                return new ArraySegment<T>(input);
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