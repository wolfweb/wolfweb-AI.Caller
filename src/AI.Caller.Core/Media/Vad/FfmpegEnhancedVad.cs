using System;

namespace AI.Caller.Core.Media.Vad {
    // 使用 FFmpeg 预处理后的信号进行判决：自适应噪声底线 + 双阈值 + 去抖
    public sealed class FfmpegEnhancedVad : IVoiceActivityDetector, IDisposable {
        private readonly FfmpegVadPreprocessor _pre;
        private int _sampleRate;
        private int _frameMs;

        private int _enterMs = 200;
        private int _resumeMs = 600;
        private int _speakingNeeded;
        private int _silenceNeeded;

        private float _noiseFloor = 0.01f;
        private float _emaAlpha = 0.05f;
        private float _enterMarginDb = 6f;
        private float _resumeMarginDb = 3f;

        private int _consecSpeaking;
        private int _consecSilence;
        private VADState _state = VADState.Silence;

        public FfmpegEnhancedVad(int inputSampleRate, int targetSampleRate = 16000, int hpCutoffHz = 120) {
            _sampleRate = targetSampleRate;
            _frameMs = 20;
            _pre = new FfmpegVadPreprocessor(inputSampleRate, targetSampleRate, hpCutoffHz);
            RecalcCounts();
        }

        public void Configure(float energyThreshold, int enterSpeakingMs, int resumeSilenceMs, int sampleRate, int frameMs) {
            _noiseFloor = Math.Max(1e-4f, energyThreshold);
            _enterMs = enterSpeakingMs;
            _resumeMs = resumeSilenceMs;
            _sampleRate = sampleRate;
            _frameMs = frameMs;
            RecalcCounts();
        }

        private void RecalcCounts() {
            _speakingNeeded = Math.Max(1, _enterMs / _frameMs);
            _silenceNeeded = Math.Max(1, _resumeMs / _frameMs);
            _consecSpeaking = 0;
            _consecSilence = 0;
            _state = VADState.Silence;
        }

        public VADResult Update(byte[] pcmBytes) {
            if (pcmBytes == null || pcmBytes.Length < 2) return new VADResult(_state, 0f);

            if (pcmBytes.Length % 2 != 0) return new VADResult(_state, 0f);
            
            int sampleCount = pcmBytes.Length / 2;
            var samples = new short[sampleCount];
            
            for (int i = 0; i < sampleCount; i++) {
                int sample = pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8);
                if (sample > 32767) sample -= 65536;
                samples[i] = (short)sample;
            }

            var proc = _pre.Process(samples);
            if (proc.Length == 0) return new VADResult(_state, 0f);

            double sumSq = 0;
            for (int i = 0; i < proc.Length; i++) {
                float v = proc[i] / 32768f;
                sumSq += v * v;
            }
            float rms = (float)Math.Sqrt(sumSq / proc.Length);

            float enterThresh = _noiseFloor * DbToLin(_enterMarginDb);
            float resumeThresh = _noiseFloor * DbToLin(_resumeMarginDb);

            bool speakingNow = _state == VADState.Silence ? rms >= enterThresh : rms >= resumeThresh;

            if (!speakingNow) {
                _noiseFloor = (1 - _emaAlpha) * _noiseFloor + _emaAlpha * rms;
                const float minSensibleNoise = 1e-3f;
                const float maxSensibleNoise = 1e-2f;
                _noiseFloor = Math.Clamp(_noiseFloor, minSensibleNoise, maxSensibleNoise);
            }

            if (speakingNow) {
                _consecSpeaking++;
                _consecSilence = 0;
                if (_state == VADState.Silence && _consecSpeaking >= _speakingNeeded) {
                    _state = VADState.Speaking;
                }
            } else {
                _consecSilence++;
                _consecSpeaking = 0;
                if (_state == VADState.Speaking && _consecSilence >= _silenceNeeded) {
                    _state = VADState.Silence;
                }
            }

            return new VADResult(_state, rms);
        }

        private static float DbToLin(float db) => (float)Math.Pow(10.0, db / 20.0);

        public void Dispose() {
            _pre.Dispose();
        }

        // 可调参数
        public void SetAdaptive(float emaAlpha, float enterMarginDb, float resumeMarginDb) {
            _emaAlpha = Math.Clamp(emaAlpha, 0.001f, 0.5f);
            _enterMarginDb = enterMarginDb;
            _resumeMarginDb = resumeMarginDb;
        }
    }
}