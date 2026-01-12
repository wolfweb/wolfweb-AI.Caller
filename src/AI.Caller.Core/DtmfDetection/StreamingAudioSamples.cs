namespace AI.Caller.Core {
    using System;

    public class StreamingAudioSamples : ISamples {
        private readonly Queue<float> _buffer = new Queue<float>();
        private readonly object _lock = new object();
        private long _totalSamplesRead = 0;

        public int Channels { get; }
        public int SampleRate { get; }

        public TimeSpan Position {
            get {
                lock (_lock) {
                    return TimeSpan.FromSeconds((double)_totalSamplesRead / Channels / SampleRate);
                }
            }
        }

        public StreamingAudioSamples(int sampleRate, int channels = 1) {
            SampleRate = sampleRate;
            Channels = channels;
        }

        public void Write(float[] newSamples) {
            lock (_lock) {
                foreach (var sample in newSamples) {
                    _buffer.Enqueue(sample);
                }
            }
        }

        public int Read(float[] outputBuffer, int count) {
            lock (_lock) {
                int readCount = 0;

                while (readCount < count && _buffer.Count > 0) {
                    outputBuffer[readCount] = _buffer.Dequeue();
                    readCount++;
                }

                _totalSamplesRead += readCount;
                return readCount;
            }
        }

        public bool HasEnoughSamples(int requiredCount) {
            lock (_lock) {
                return _buffer.Count >= requiredCount;
            }
        }
    }
}
