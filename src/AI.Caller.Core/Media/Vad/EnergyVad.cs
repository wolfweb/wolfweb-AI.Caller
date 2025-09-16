using System;

namespace AI.Caller.Core.Media.Vad {
    // 简易能量门限 VAD：进入说话/回到静音含去抖，满足最小可运行
    public sealed class EnergyVad : IVoiceActivityDetector {
        private float _threshold = 0.02f;
        private int _enterMs = 200;
        private int _resumeMs = 600;
        private int _sampleRate = 8000;
        private int _frameMs = 20;

        private int _speakingCountNeeded;
        private int _silenceCountNeeded;
        private int _consecSpeaking;
        private int _consecSilence;
        private VADState _state = VADState.Silence;

        public void Configure(float energyThreshold, int enterSpeakingMs, int resumeSilenceMs, int sampleRate, int frameMs) {
            _threshold = energyThreshold;
            _enterMs = enterSpeakingMs;
            _resumeMs = resumeSilenceMs;
            _sampleRate = sampleRate;
            _frameMs = frameMs;

            _speakingCountNeeded = Math.Max(1, _enterMs / _frameMs);
            _silenceCountNeeded = Math.Max(1, _resumeMs / _frameMs);
            _consecSpeaking = 0;
            _consecSilence = 0;
            _state = VADState.Silence;
        }

        public VADResult Update(short[] pcm) {
            if (pcm == null || pcm.Length == 0)
                return new VADResult(_state, 0f);

            double sumSq = 0;
            for (int i = 0; i < pcm.Length; i++) {
                float v = pcm[i] / 32768f;
                sumSq += v * v;
            }
            float rms = (float)Math.Sqrt(sumSq / pcm.Length);

            bool speakingNow = rms >= _threshold;

            if (speakingNow) {
                _consecSpeaking++;
                _consecSilence = 0;
                if (_state == VADState.Silence && _consecSpeaking >= _speakingCountNeeded) {
                    _state = VADState.Speaking;
                }
            } else {
                _consecSilence++;
                _consecSpeaking = 0;
                if (_state == VADState.Speaking && _consecSilence >= _silenceCountNeeded) {
                    _state = VADState.Silence;
                }
            }

            return new VADResult(_state, rms);
        }
    }
}