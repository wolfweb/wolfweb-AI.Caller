using System;
using System.IO;
using System.Text;

namespace AI.Caller.Core.Tests.Media.Tts {
    public static class TtsTestUtils {
        // 读取最基本WAV头信息：采样率、声道、位深、数据长度
        public static (int sampleRate, int channels, int bitsPerSample, int dataLen) ReadWavHeader(string path) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            string riff = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (riff != "RIFF") throw new InvalidDataException("Not RIFF");

            int riffSize = br.ReadInt32();
            string wave = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (wave != "WAVE") throw new InvalidDataException("Not WAVE");

            int sampleRate = 0, channels = 0, bits = 0, dataLen = 0;

            while (fs.Position < fs.Length) {
                string chunkId = Encoding.ASCII.GetString(br.ReadBytes(4));
                int chunkSize = br.ReadInt32();

                if (chunkId == "fmt ") {
                    short audioFormat = br.ReadInt16();
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    int byteRate = br.ReadInt32();
                    short blockAlign = br.ReadInt16();
                    bits = br.ReadInt16();
                    // 跳过扩展
                    int remaining = chunkSize - 16;
                    if (remaining > 0) br.ReadBytes(remaining);
                } else if (chunkId == "data") {
                    dataLen = chunkSize;
                    // 读指针跳到末尾即可
                    fs.Position += chunkSize;
                } else {
                    // 跳过未知块
                    fs.Position += chunkSize;
                }
            }

            return (sampleRate, channels, bits, dataLen);
        }
    }
}