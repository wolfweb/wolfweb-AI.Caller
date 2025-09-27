using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Sources {
    public sealed class QueueAudioPlaybackSource : IAudioPlaybackSource, IPlaybackMeter, IPausablePlayback {
        private readonly ConcurrentQueue<byte[]> _queue = new();
        private int _samplesPerFrame = 160; // 8k/20ms
        private volatile bool _started;
        private volatile bool _paused;
        private float _playbackRms; // 简易播放电平计
        private readonly object _gateLock = new();

        public bool IsPaused => _paused;
        public float PlaybackRms => _playbackRms;

        public void Init(MediaProfile profile) {
            _samplesPerFrame = profile.SamplesPerFrame;
        }

        public Task StartAsync(CancellationToken ct) {
            _started = true;
            _paused = false;
            _playbackRms = 0f;
            return Task.CompletedTask;
        }

        public byte[] ReadNextPcmFrame() {
            if (!_started) return Array.Empty<byte>();
            int frameBytes = _samplesPerFrame * 2;
            if (_paused) return new byte[frameBytes];

            if (_queue.TryDequeue(out var frame)) {
                UpdatePlaybackRms(frame);

                if (frame.Length == frameBytes) return frame;
                var buf = new byte[frameBytes];
                int copy = Math.Min(frame.Length, frameBytes);
                Array.Copy(frame, 0, buf, 0, copy);
                return buf;
            }
            return new byte[frameBytes];
        }

        public Task StopAsync() {
            _started = false;
            _paused = false;
            while (_queue.TryDequeue(out _)) { }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() {
            _started = false;
            _paused = false;
            while (_queue.TryDequeue(out _)) { }
            return ValueTask.CompletedTask;
        }

        public void Enqueue(ReadOnlySpan<byte> pcm) {
            if (!_started) return;
            var copy = new byte[pcm.Length];
            pcm.CopyTo(copy);
            _queue.Enqueue(copy);
        }

        public void Pause() {
            lock (_gateLock) { _paused = true; }
        }

        public void Resume() {
            lock (_gateLock) { _paused = false; }
        }

        private void UpdatePlaybackRms(byte[] frame) {
            if (frame == null || frame.Length < 2) { _playbackRms = 0f; return; }
            
            if (frame.Length % 2 != 0) { _playbackRms = 0f; return; }
            
            double sumSq = 0;
            int sampleCount = frame.Length / 2;
            
            for (int i = 0; i < sampleCount; i++) {
                int sample = frame[i * 2] | (frame[i * 2 + 1] << 8);
                if (sample > 32767) sample -= 65536;
                
                float v = sample / 32768f;
                sumSq += v * v;
            }
            _playbackRms = (float)Math.Sqrt(sumSq / sampleCount);
        }
    }
}