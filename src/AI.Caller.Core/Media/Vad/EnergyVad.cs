using System;

namespace AI.Caller.Core.Media.Vad {
    // 简易能量门限 VAD：进入说话/回到静音含去抖
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

        public VADResult Update(byte[] pcmBytes) {
            if (pcmBytes == null || pcmBytes.Length < 2)
                return new VADResult(_state, 0f);

            float rms = CalculateEnergyFromBytes(pcmBytes);

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

        private float CalculateEnergyFromBytes(byte[] pcmBytes) {
            if (pcmBytes.Length % 2 != 0) {
                return 0f;
            }

            double sumSq = 0;
            int sampleCount = pcmBytes.Length / 2;
            
            for (int i = 0; i < sampleCount; i++) {
                int sample = pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8);
                if (sample > 32767) sample -= 65536;
                
                float v = sample / 32768f;
                sumSq += v * v;
            }
            
            return (float)Math.Sqrt(sumSq / sampleCount);
        }
    }
}