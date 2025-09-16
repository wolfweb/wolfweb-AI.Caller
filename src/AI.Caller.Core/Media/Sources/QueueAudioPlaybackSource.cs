using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Sources {
    public sealed class QueueAudioPlaybackSource : IAudioPlaybackSource {
        private readonly ConcurrentQueue<short[]> _queue = new();
        private int _samplesPerFrame = 160; // 8k/20ms
        private volatile bool _started;

        public void Init(MediaProfile profile) {
            _samplesPerFrame = profile.SamplesPerFrame;
        }

        public Task StartAsync(CancellationToken ct) {
            _started = true;
            return Task.CompletedTask;
        }

        public short[] ReadNextPcmFrame() {
            if (!_started) return Array.Empty<short>();
            if (_queue.TryDequeue(out var frame)) {
                if (frame.Length == _samplesPerFrame) return frame;
                var buf = new short[_samplesPerFrame];
                int copy = Math.Min(frame.Length, _samplesPerFrame);
                Array.Copy(frame, 0, buf, 0, copy);
                return buf;
            }
            return new short[_samplesPerFrame];
        }

        public Task StopAsync() {
            _started = false;
            while (_queue.TryDequeue(out _)) { }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() {
            _started = false;
            while (_queue.TryDequeue(out _)) { }
            return ValueTask.CompletedTask;
        }

        // 供TTS/上层注入
        public void Enqueue(ReadOnlySpan<short> pcm) {
            if (!_started) return;
            var copy = new short[pcm.Length];
            pcm.CopyTo(copy);
            _queue.Enqueue(copy);
        }
    }
}