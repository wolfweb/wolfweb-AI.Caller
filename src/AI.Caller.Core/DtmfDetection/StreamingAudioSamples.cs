namespace AI.Caller.Core {
    using System;

    public class StreamingAudioSamples : ISamples {
        private readonly float[] _ringBuffer;
        private long _totalSamplesRead = 0;
        private int _writeIndex = 0;
        private int _readIndex = 0;
        private int _count = 0;

        private readonly int _capacity = 8192;
        private readonly object _lock = new object();

        public int Channels { get; }
        public int SampleRate { get; }

        public TimeSpan Position {
            get {
                lock (_lock) {
                    return TimeSpan.FromSeconds((double)_totalSamplesRead / Channels / SampleRate);
                }
            }
        }

        public StreamingAudioSamples(int sampleRate = 8000, int channels = 1) {
            SampleRate = sampleRate;
            Channels = channels;
            _ringBuffer = new float[_capacity];
        }

        public void Write(Span<float> newSamples) {
            lock (_lock) {
                foreach (var sample in newSamples) {
                    _ringBuffer[_writeIndex] = sample;
                    _writeIndex = (_writeIndex + 1) % _capacity;
                    if (_count < _capacity) _count++;
                    else _readIndex = (_readIndex + 1) % _capacity;
                }
            }
        }

        public int Read(float[] outputBuffer, int count) {
            lock (_lock) {
                int readCount = Math.Min(count, _count);
                for (int i = 0; i < readCount; i++) {
                    outputBuffer[i] = _ringBuffer[_readIndex];
                    _readIndex = (_readIndex + 1) % _capacity;
                }
                _count -= readCount;
                _totalSamplesRead += readCount;
                return readCount;
            }
        }

        public bool HasEnoughSamples(int requiredCount) {
            lock (_lock) {
                return _count >= requiredCount;
            }
        }
    }
}
