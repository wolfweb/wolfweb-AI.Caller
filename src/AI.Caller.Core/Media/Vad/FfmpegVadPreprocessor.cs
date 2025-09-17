using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace AI.Caller.Core.Media.Vad {
    public unsafe class FfmpegVadPreprocessor : IDisposable {
        private SwrContext* _swrContext;
        private AVFilterGraph* _filterGraph;
        private AVFilterContext* _bufferSrcCtx;
        private AVFilterContext* _bufferSinkCtx;
        private AVFrame* _inputFrame;
        private AVFrame* _outputFrame;
        private bool _initialized;
        private bool _disposed;

        private readonly int _inputSampleRate;
        private readonly int _outputSampleRate;
        private readonly float _hpCutoffHz;
        private readonly bool _enableDenoising;

        public FfmpegVadPreprocessor(int inputSampleRate = 16000, int outputSampleRate = 16000,
            float hpCutoffHz = 120f, bool enableDenoising = false) {
            _inputSampleRate = inputSampleRate;
            _outputSampleRate = outputSampleRate;
            _hpCutoffHz = hpCutoffHz;
            _enableDenoising = enableDenoising;
        }

        public void Initialize() {
            if (_initialized) return;

            try {
                InitializeResampler();
                InitializeFilterGraph();
                AllocateFrames();
                _initialized = true;
            } catch {
                Cleanup();
                throw;
            }
        }

        private void InitializeResampler() {
            if (_inputSampleRate == _outputSampleRate) return;

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

        private void InitializeFilterGraph() {
            _filterGraph = ffmpeg.avfilter_graph_alloc();
            if (_filterGraph == null)
                throw new InvalidOperationException("Failed to allocate filter graph");

            var abuffer = ffmpeg.avfilter_get_by_name("abuffer");
            var abuffersink = ffmpeg.avfilter_get_by_name("abuffersink");
            var highpass = ffmpeg.avfilter_get_by_name("highpass");

            if (abuffer == null || abuffersink == null || highpass == null)
                throw new InvalidOperationException("Required filters not available");

            int result;

            // Create buffer source
            var args = $"time_base=1/{_outputSampleRate}:sample_rate={_outputSampleRate}:sample_fmt=s16:channel_layout=mono";
            fixed (AVFilterContext** bufferSrcCtx = &_bufferSrcCtx) {
                result = ffmpeg.avfilter_graph_create_filter(bufferSrcCtx, abuffer, "in", args, null, _filterGraph);
                if (result < 0)
                    throw new InvalidOperationException($"Failed to create buffer source: {result}");
            }

            // Create buffer sink
            fixed (AVFilterContext** bufferSinkCtx = &_bufferSinkCtx) {
                result = ffmpeg.avfilter_graph_create_filter(bufferSinkCtx, abuffersink, "out", null, null, _filterGraph);
                if (result < 0)
                    throw new InvalidOperationException($"Failed to create buffer sink: {result}");
            }

            // Configure sink constraints
            ConfigureBufferSink();

            AVFilterContext* lastFilter = _bufferSrcCtx;

            // Add highpass filter
            AVFilterContext* hpCtx;
            var hpArgs = $"f={_hpCutoffHz}";
            result = ffmpeg.avfilter_graph_create_filter(&hpCtx, highpass, "hp", hpArgs, null, _filterGraph);
            if (result < 0)
                throw new InvalidOperationException($"Failed to create highpass filter: {result}");

            result = ffmpeg.avfilter_link(lastFilter, 0, hpCtx, 0);
            if (result < 0)
                throw new InvalidOperationException($"Failed to link to highpass: {result}");

            lastFilter = hpCtx;

            // Add optional denoising filter
            if (_enableDenoising) {
                var afftdn = ffmpeg.avfilter_get_by_name("afftdn");
                if (afftdn != null) {
                    AVFilterContext* denoiseCtx;
                    result = ffmpeg.avfilter_graph_create_filter(&denoiseCtx, afftdn, "denoise", "nr=12", null, _filterGraph);
                    if (result >= 0) {
                        result = ffmpeg.avfilter_link(lastFilter, 0, denoiseCtx, 0);
                        if (result >= 0)
                            lastFilter = denoiseCtx;
                    }
                }
            }

            // Link to sink
            result = ffmpeg.avfilter_link(lastFilter, 0, _bufferSinkCtx, 0);
            if (result < 0)
                throw new InvalidOperationException($"Failed to link to sink: {result}");

            // Configure graph
            result = ffmpeg.avfilter_graph_config(_filterGraph, null);
            if (result < 0)
                throw new InvalidOperationException($"Failed to configure filter graph: {result}");
        }

        private void ConfigureBufferSink() {
            // Set sample format constraint
            var sampleFmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            var sampleFmtPtr = stackalloc AVSampleFormat[1] { sampleFmt };
            ffmpeg.av_opt_set_bin(_bufferSinkCtx, "sample_fmts", (byte*)sampleFmtPtr, sizeof(AVSampleFormat), ffmpeg.AV_OPT_SEARCH_CHILDREN);

            // Set sample rate constraint
            var sampleRatePtr = stackalloc int[1] { _outputSampleRate };
            ffmpeg.av_opt_set_bin(_bufferSinkCtx, "sample_rates", (byte*)sampleRatePtr, sizeof(int), ffmpeg.AV_OPT_SEARCH_CHILDREN);

            // Set channel layout constraint (use legacy format for compatibility)
            var monoLayoutPtr = stackalloc ulong[1] { ffmpeg.AV_CH_LAYOUT_MONO };
            ffmpeg.av_opt_set_bin(_bufferSinkCtx, "channel_layouts", (byte*)monoLayoutPtr, sizeof(ulong), ffmpeg.AV_OPT_SEARCH_CHILDREN);
        }

        private void AllocateFrames() {
            _inputFrame = ffmpeg.av_frame_alloc();
            _outputFrame = ffmpeg.av_frame_alloc();

            if (_inputFrame == null || _outputFrame == null)
                throw new InvalidOperationException("Failed to allocate frames");
        }

        public short[] Process(short[] inputPcm) {
            if (!_initialized)
                Initialize();

            if (inputPcm == null || inputPcm.Length == 0)
                return Array.Empty<short>();

            try {
                short[] resampledPcm = inputPcm;

                // Resample if needed
                if (_swrContext != null) {
                    resampledPcm = Resample(inputPcm);
                }

                // Apply filter chain
                return ApplyFilters(resampledPcm);
            } catch (Exception ex) {
                throw new InvalidOperationException($"Error processing audio: {ex.Message}", ex);
            }
        }

        private short[] Resample(short[] input) {
            if (_swrContext == null) return input;

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
        }

        private short[] ApplyFilters(short[] input) {
            if (_filterGraph == null || _bufferSrcCtx == null || _bufferSinkCtx == null)
                return input;

            // Setup input frame
            _inputFrame->nb_samples = input.Length;
            _inputFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
            _inputFrame->sample_rate = _outputSampleRate;

            // Set channel layout using new API
            ffmpeg.av_channel_layout_default(&_inputFrame->ch_layout, 1);

            int ret = ffmpeg.av_frame_get_buffer(_inputFrame, 0);
            if (ret < 0)
                throw new InvalidOperationException($"Failed to allocate frame buffer: {ret}");

            // Copy input data
            fixed (short* inputPtr = input) {
                Buffer.MemoryCopy(inputPtr, _inputFrame->data[0], input.Length * sizeof(short), input.Length * sizeof(short));
            }

            // Send frame to filter
            ret = ffmpeg.av_buffersrc_add_frame_flags(_bufferSrcCtx, _inputFrame, 0);
            if (ret < 0)
                throw new InvalidOperationException($"Failed to add frame to buffer source: {ret}");

            // Get filtered frame
            ret = ffmpeg.av_buffersink_get_frame(_bufferSinkCtx, _outputFrame);
            if (ret < 0) {
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    return input;
                throw new InvalidOperationException($"Failed to get frame from buffer sink: {ret}");
            }

            var output = new short[_outputFrame->nb_samples];
            fixed (short* outputPtr = output) {
                Buffer.MemoryCopy(_outputFrame->data[0], outputPtr, output.Length * sizeof(short), output.Length * sizeof(short));
            }

            ffmpeg.av_frame_unref(_outputFrame);
            ffmpeg.av_frame_unref(_inputFrame);

            return output;
        }

        private void Cleanup() {
            if (_inputFrame != null) {
                fixed (AVFrame** inputFrame = &_inputFrame) {
                    ffmpeg.av_frame_free(inputFrame);
                    _inputFrame = null;
                }
            }

            if (_outputFrame != null) {
                fixed (AVFrame** outputFrame = &_outputFrame) {
                    ffmpeg.av_frame_free(outputFrame);
                    _outputFrame = null;
                }
            }

            if (_filterGraph != null) {
                fixed (AVFilterGraph** filterGraph = &_filterGraph) {
                    ffmpeg.avfilter_graph_free(filterGraph);
                    _filterGraph = null;
                }
            }

            if (_swrContext != null) {
                fixed (SwrContext** swrContext = &_swrContext) {
                    ffmpeg.swr_free(swrContext);
                    _swrContext = null;
                }
            }
        }

        public void Dispose() {
            if (!_disposed) {
                Cleanup();
                _disposed = true;
            }
        }
    }
}