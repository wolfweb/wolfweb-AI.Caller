using System;
using System.IO;

namespace AI.Caller.Core.Tests.Media.Tts {
    internal static class TtsTestUtils {
        public static (int sampleRate, short channels, short bitsPerSample, int dataLen) ReadWavHeader(string path) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);
            string riff = new string(br.ReadChars(4));
            int chunkSize = br.ReadInt32();
            string wave = new string(br.ReadChars(4));
            if (riff != "RIFF" || wave != "WAVE") throw new InvalidDataException("Not a WAV");

            short audioFmt = 0;
            short channels = 0;
            int sampleRate = 0;
            short bits = 0;
            int dataLen = 0;

            while (fs.Position < fs.Length) {
                string id = new string(br.ReadChars(4));
                int size = br.ReadInt32();
                if (id == "fmt ") {
                    audioFmt = br.ReadInt16();
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    int byteRate = br.ReadInt32();
                    short blockAlign = br.ReadInt16();
                    bits = br.ReadInt16();
                    int remain = size - 16;
                    if (remain > 0) br.ReadBytes(remain);
                } else if (id == "data") {
                    dataLen = size;
                    break;
                } else {
                    br.ReadBytes(size);
                }
            }

            if (audioFmt != 1 || bits != 16) throw new NotSupportedException("Expect PCM16");
            return (sampleRate, channels, bits, dataLen);
        }

        public static double ComputeRms(string path, int takeSamples = 32000) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);
            // skip headers
            Span<byte> hdr = stackalloc byte[44];
            br.Read(hdr);
            int samples = 0;
            double sumSq = 0;
            for (int i = 0; i < takeSamples; i++) {
                if (fs.Position + 2 > fs.Length) break;
                short s = br.ReadInt16();
                float v = s / 32768f;
                sumSq += v * v;
                samples++;
            }
            if (samples == 0) return 0;
            return Math.Sqrt(sumSq / samples);
        }
    }
}