using SIPSorceryMedia.Abstractions;
using IAudioEncoder = SIPSorceryMedia.Abstractions.IAudioEncoder;

namespace AI.Caller.Core {
    public class MixerAudioSource : IAudioSource {
        private readonly IAudioEncoder _audioEncoder;
        private readonly MediaFormatManager<AudioFormat> _formatManager;

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;

        public MixerAudioSource(IAudioEncoder audioEncoder) {
            _audioEncoder = audioEncoder;
            _formatManager = new MediaFormatManager<AudioFormat>(_audioEncoder.SupportedFormats);
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) {
            if (!_isStarted || _isPaused || _isClosed) return;

            OnAudioSourceRawSample?.Invoke(samplingRate, durationMilliseconds, sample);

            if (OnAudioSourceEncodedSample == null) return;

            var selectedFormat = _formatManager.SelectedFormat;
            if (selectedFormat.IsEmpty()) return;

            short[] pcmToEncode = sample;
            int inputRateInt = (int)samplingRate;

            if (inputRateInt != selectedFormat.ClockRate) {
                pcmToEncode = Resample(sample, inputRateInt, selectedFormat.ClockRate);
            }

            byte[] encodedData = _audioEncoder.EncodeAudio(pcmToEncode, selectedFormat);

            int durationRtpUnits = pcmToEncode.Length;

            OnAudioSourceEncodedSample.Invoke((uint)durationRtpUnits, encodedData);

            OnAudioSourceEncodedFrameReady?.Invoke(new EncodedAudioFrame(durationRtpUnits, selectedFormat, durationMilliseconds, encodedData));
        }

        public void SendAudio(short[] pcm, int sampleRate) {            
            AudioSamplingRatesEnum rateEnum = sampleRate switch {
                8000 => AudioSamplingRatesEnum.Rate8KHz,
                16000 => AudioSamplingRatesEnum.Rate16KHz,
                48000 => AudioSamplingRatesEnum.Rate48kHz,
                _ => AudioSamplingRatesEnum.Rate8KHz // 默认兜底
            };

            uint durationMs = (uint)(pcm.Length / (sampleRate / 1000));

            ExternalAudioSourceRawSample(rateEnum, durationMs, pcm);
        }

        public Task StartAudio() {
            _isStarted = true;
            return Task.CompletedTask;
        }

        public Task CloseAudio() {
            _isClosed = true;
            return Task.CompletedTask;
        }

        public Task PauseAudio() {
            _isPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeAudio() {
            _isPaused = false;
            return Task.CompletedTask;
        }

        public List<AudioFormat> GetAudioSourceFormats() {
            return _formatManager.GetSourceFormats();
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat) {
            _formatManager.SetSelectedFormat(audioFormat);
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter) {
            _formatManager.RestrictFormats(filter);
        }

        public bool HasEncodedAudioSubscribers() {
            return OnAudioSourceEncodedSample != null;
        }

        public bool IsAudioSourcePaused() {
            return _isPaused;
        }

        private static short[] Resample(short[] pcm, int inRate, int outRate) {
            if (inRate == outRate) {
                return pcm;
            } else if (inRate == 8000 && outRate == 16000) {
                // Crude up-sample to 16Khz by doubling each sample.
                return pcm.SelectMany(x => new short[] { x, x }).ToArray();
            } else if (inRate == 8000 && outRate == 48000) {
                // Crude up-sample to 48Khz by 6x each sample. This sounds bad, use for testing only.
                return pcm.SelectMany(x => new short[] { x, x, x, x, x, x }).ToArray();
            } else if (inRate == 16000 && outRate == 8000) {
                // Crude down-sample to 8Khz by skipping every second sample.
                return pcm.Where((x, i) => i % 2 == 0).ToArray();
            } else if (inRate == 16000 && outRate == 48000) {
                // Crude up-sample to 48Khz by 3x each sample. This sounds bad, use for testing only.
                return pcm.SelectMany(x => new short[] { x, x, x }).ToArray();
            } else {
                throw new ApplicationException($"Sorry don't know how to re-sample PCM from {inRate} to {outRate}. Pull requests welcome!");
            }
        }
    }
}
