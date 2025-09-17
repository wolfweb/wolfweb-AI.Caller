using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Sources {
    public sealed class QueueAudioPlaybackSource : IAudioPlaybackSource, IPlaybackMeter, IPausablePlayback {
        private readonly ConcurrentQueue<short[]> _queue = new();
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

        public short[] ReadNextPcmFrame() {
            if (!_started) return Array.Empty<short>();
            if (_paused) return new short[_samplesPerFrame];

            if (_queue.TryDequeue(out var frame)) {
                // 更新播放电平计
                UpdatePlaybackRms(frame);

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

        // 供TTS/上层注入
        public void Enqueue(ReadOnlySpan<short> pcm) {
            if (!_started) return;
            var copy = new short[pcm.Length];
            pcm.CopyTo(copy);
            _queue.Enqueue(copy);
        }

        public void Pause() {
            lock (_gateLock) { _paused = true; }
        }

        public void Resume() {
            lock (_gateLock) { _paused = false; }
        }

        private void UpdatePlaybackRms(short[] frame) {
            if (frame == null || frame.Length == 0) { _playbackRms = 0f; return; }
            double sumSq = 0;
            for (int i = 0; i < frame.Length; i++) {
                float v = frame[i] / 32768f;
                sumSq += v * v;
            }
            _playbackRms = (float)Math.Sqrt(sumSq / frame.Length);
        }
    }
}