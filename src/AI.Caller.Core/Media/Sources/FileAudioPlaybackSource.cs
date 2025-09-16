using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Sources {
    public sealed class FileAudioPlaybackSource : IAudioPlaybackSource {
        private FileStream? _fs;
        private BinaryReader? _br;
        private int _channels;
        private int _sampleRate;
        private short _bitsPerSample;
        private long _dataStart;
        private int _dataLength;
        private int _samplesPerFrame;
        private bool _started;

        public void Init(AI.Caller.Core.Media.MediaProfile profile) {
            _samplesPerFrame = profile.SamplesPerFrame;
        }

        private readonly string _wavPath;

        public FileAudioPlaybackSource(string wavPath) {
            _wavPath = wavPath;
        }

        public async Task StartAsync(CancellationToken ct) {
            _fs = new FileStream(_wavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _br = new BinaryReader(_fs);
            ParseWavHeader();
            _started = true;
            await Task.CompletedTask;
        }

        public short[] ReadNextPcmFrame() {
            if (!_started || _br == null) return Array.Empty<short>();
            var buf = new short[_samplesPerFrame];

            int bytesPerSample = _bitsPerSample / 8;
            int bytesToRead = _samplesPerFrame * bytesPerSample * _channels;

            Span<byte> frameBytes = stackalloc byte[bytesToRead > 8192 ? 8192 : bytesToRead];
            int totalRead = 0;
            while (totalRead < bytesToRead) {
                int chunk = Math.Min(frameBytes.Length, bytesToRead - totalRead);
                int n = _br.Read(frameBytes.Slice(0, chunk));
                if (n <= 0) break;
                totalRead += n;
            }
            if (totalRead == 0) return Array.Empty<short>();

            int framesRead = totalRead / (bytesPerSample * _channels);
            for (int i = 0; i < framesRead; i++) {
                int baseIndex = i * bytesPerSample * _channels;
                short sample = (short)(frameBytes[baseIndex] | (frameBytes[baseIndex + 1] << 8));
                buf[i] = sample;
            }

            if (framesRead < _samplesPerFrame) {
                for (int i = framesRead; i < _samplesPerFrame; i++) buf[i] = 0;
            }

            return buf;
        }

        public async Task StopAsync() {
            _started = false;
            _br?.Dispose();
            _fs?.Dispose();
            _br = null;
            _fs = null;
            await Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() {
            await StopAsync();
        }

        private void ParseWavHeader() {
            var br = _br!;
            string riff = new string(br.ReadChars(4));
            int fileSize = br.ReadInt32();
            string wave = new string(br.ReadChars(4));
            if (riff != "RIFF" || wave != "WAVE") throw new InvalidDataException("Not a WAV file");

            while (true) {
                string chunkId = new string(br.ReadChars(4));
                int chunkSize = br.ReadInt32();
                if (chunkId == "fmt ") {
                    short audioFormat = br.ReadInt16(); // PCM=1
                    _channels = br.ReadInt16();
                    _sampleRate = br.ReadInt32();
                    int byteRate = br.ReadInt32();
                    short blockAlign = br.ReadInt16();
                    _bitsPerSample = br.ReadInt16();
                    // skip remaining if any
                    int consumed = 16;
                    int remaining = chunkSize - consumed;
                    if (remaining > 0) br.ReadBytes(remaining);
                    if (audioFormat != 1 || _bitsPerSample != 16) throw new NotSupportedException("Only PCM16 supported");
                } else if (chunkId == "data") {
                    _dataStart = _fs!.Position;
                    _dataLength = chunkSize;
                    break;
                } else {
                    br.ReadBytes(chunkSize);
                }
            }
        }
    }
}