using AI.Caller.Core;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using FFmpeg.AutoGen;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Caller.Phone.Controllers {
    [AllowAnonymous]
    public class TTSTestController : Controller {
        private readonly ILogger _logger;
        private readonly ITTSEngine _ttsEngine;
        private readonly G711Codec _g711Codec;

        public TTSTestController(ILogger<TTSTestController> logger, ITTSEngine ttsEngine) {
            _logger = logger;
            _ttsEngine = ttsEngine;
            _g711Codec = new G711Codec();
        }

        public IActionResult Index() {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> OriginalAudio() {
            var text = "您好，欢迎致电我们公司，我是AI客服小助手。";

            try {
                _logger.LogInformation("生成原始音频流");
                var allAudioData = new List<float>();
                int sampleRate = 16000;

                await foreach (var audioData in _ttsEngine.SynthesizeStreamAsync(text, 0, 1.0f)) {
                    if (audioData.FloatData?.Length > 0) {
                        sampleRate = audioData.SampleRate;
                        allAudioData.AddRange(audioData.FloatData);
                    }
                }

                _logger.LogInformation($"原始音频: {allAudioData.Count} 采样点 @ {sampleRate}Hz");
                var wavData = ConvertToWav(allAudioData.ToArray(), sampleRate);

                Response.Headers.Add("Content-Disposition", $"inline; filename=\"original_{sampleRate}Hz.wav\"");
                return File(wavData, "audio/wav");
            } catch (Exception ex) {
                _logger.LogError(ex, "生成原始音频失败");
                return BadRequest($"生成原始音频失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 输出重采样后的音频流 (8000Hz)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ResampledAudio() {
            var text = "您好，欢迎致电我们公司，我是AI客服小助手。";

            try {
                _logger.LogInformation("生成重采样音频流");
                var aiProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
                var allResampledData = new List<byte>();
                int originalSampleRate = 22050;
                int totalOriginalSamples = 0;

                await foreach (var audioData in _ttsEngine.SynthesizeStreamAsync(text, 0, 1.0f)) {
                    if (audioData.FloatData?.Length > 0) {
                        originalSampleRate = audioData.SampleRate;
                        totalOriginalSamples += audioData.FloatData.Length;

                        float[] processedFloat = audioData.FloatData;
                        if (originalSampleRate != aiProfile.SampleRate) {
                            using var resampler = new AudioResampler<float>(originalSampleRate, aiProfile.SampleRate, _logger);
                            processedFloat = resampler.Resample(audioData.FloatData);
                            _logger.LogDebug($"重采样: {audioData.FloatData.Length} bytes -> {processedFloat.Length} bytes");
                        }

                        var shortSamples = new short[processedFloat.Length];
                        for (int i = 0; i < processedFloat.Length; i++) {
                            float sample = processedFloat[i];
                            if (float.IsNaN(sample) || float.IsInfinity(sample)) {
                                sample = 0f;
                            } else {
                                sample = Math.Clamp(sample, -1f, 1f);
                            }

                            int intSample = (int)MathF.Round(sample * 32767f);
                            shortSamples[i] = (short)Math.Clamp(intSample, short.MinValue, short.MaxValue);
                        }

                        var byteArray = new byte[shortSamples.Length * 2];
                        for (int i = 0; i < shortSamples.Length; i++) {
                            short sample = shortSamples[i];
                            byteArray[i * 2] = (byte)(sample & 0xFF);        // 低字节
                            byteArray[i * 2 + 1] = (byte)((sample >> 8) & 0xFF); // 高字节
                        }

                        allResampledData.AddRange(byteArray);
                    }
                }

                var resampledSamples = allResampledData.Count / 2;
                var expectedSamples = (int)(totalOriginalSamples * (double)aiProfile.SampleRate / originalSampleRate);

                _logger.LogInformation($"重采样结果: {totalOriginalSamples}@{originalSampleRate}Hz -> {resampledSamples}@{aiProfile.SampleRate}Hz (预期: {expectedSamples})");

                if (allResampledData.Count == 0) {
                    _logger.LogError("重采样失败：没有输出数据");
                    return BadRequest("❌ 重采样失败：没有输出数据！这就是AI TTS没声音的原因。");
                }

                if (Math.Abs(resampledSamples - expectedSamples) > expectedSamples * 0.1) {
                    _logger.LogWarning($"重采样异常：得到{resampledSamples}采样，预期{expectedSamples}采样");
                }

                var resampledFloat = ConvertPcm16ToFloat(allResampledData.ToArray());
                var wavData = ConvertToWav(resampledFloat, aiProfile.SampleRate);

                Response.Headers.Add("Content-Disposition", $"inline; filename=\"resampled_{aiProfile.SampleRate}Hz.wav\"");
                return File(wavData, "audio/wav");
            } catch (Exception ex) {
                _logger.LogError(ex, "生成重采样音频失败");
                return BadRequest($"生成重采样音频失败: {ex.Message}");
            }
        }

        private byte[] ConvertFloatToPcm16(float[] floatData) {
            var pcmData = new byte[floatData.Length * 2];
            for (int i = 0; i < floatData.Length; i++) {
                var sample = (short)(floatData[i] * 32767f);
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return pcmData;
        }

        private float[] ConvertPcm16ToFloat(byte[] pcmData) {
            var floatData = new float[pcmData.Length / 2];
            for (int i = 0; i < floatData.Length; i++) {
                var sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                floatData[i] = sample / 32767f;
            }
            return floatData;
        }

        private byte[] ConvertToWav(float[] audioData, int sampleRate) {
            var pcmData = ConvertFloatToPcm16(audioData);
            var wavHeader = CreateWavHeader(pcmData.Length, sampleRate);

            var wavData = new byte[wavHeader.Length + pcmData.Length];
            Array.Copy(wavHeader, 0, wavData, 0, wavHeader.Length);
            Array.Copy(pcmData, 0, wavData, wavHeader.Length, pcmData.Length);

            return wavData;
        }

        /// <summary>
        /// 完全模拟AI TTS的处理流程 (包括分帧)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> AIProcessedAudio() {
            var text = "您好，欢迎致电我们公司，我是AI客服小助手。";
            
            try {
                _logger.LogInformation("生成AI处理流程音频流");
                var aiProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
                var allProcessedData = new List<byte>();
                int originalSampleRate = 22050;
                int totalOriginalSamples = 0;

                await foreach (var audioData in _ttsEngine.SynthesizeStreamAsync(text, 0, 1.0f)) {
                    if (audioData.FloatData?.Length > 0) {
                        originalSampleRate = audioData.SampleRate;
                        totalOriginalSamples += audioData.FloatData.Length;
                        
                        var processedChunk = SimulateAIEnqueueFloatPcm(audioData.FloatData, audioData.SampleRate, aiProfile);
                        allProcessedData.AddRange(processedChunk);
                    }
                }

                var processedSamples = allProcessedData.Count / 2;
                var expectedSamples = (int)(totalOriginalSamples * (double)aiProfile.SampleRate / originalSampleRate);
                
                _logger.LogInformation($"AI处理结果: {totalOriginalSamples}@{originalSampleRate}Hz -> {processedSamples}@{aiProfile.SampleRate}Hz (预期: {expectedSamples})");

                if (allProcessedData.Count == 0) {
                    _logger.LogError("AI处理失败：没有输出数据");
                    return BadRequest("❌ AI处理失败：没有输出数据！");
                }

                var processedFloat = ConvertPcm16ToFloat(allProcessedData.ToArray());
                var wavData = ConvertToWav(processedFloat, aiProfile.SampleRate);
                
                Response.Headers.Add("Content-Disposition", $"inline; filename=\"ai_processed_{aiProfile.SampleRate}Hz.wav\"");
                return File(wavData, "audio/wav");
            } catch (Exception ex) {
                _logger.LogError(ex, "生成AI处理音频失败");
                return BadRequest($"生成AI处理音频失败: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> G711ProcessedAudio() {
            var text = "您好，欢迎致电我们公司，我是AI客服小助手。";

            try {
                _logger.LogInformation("生成G.711处理流程音频流");
                var aiProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
                var allProcessedData = new List<byte>();
                int originalSampleRate = 22050;
                int totalOriginalSamples = 0;

                await foreach (var audioData in _ttsEngine.SynthesizeStreamAsync(text, 0, 1.0f)) {
                    if (audioData.FloatData?.Length > 0) {
                        originalSampleRate = audioData.SampleRate;
                        totalOriginalSamples += audioData.FloatData.Length;

                        var pcmChunk = SimulateAIEnqueueFloatPcm(audioData.FloatData, audioData.SampleRate, aiProfile);
                        var g711Chunk = _g711Codec.EncodeMuLaw(pcmChunk.AsSpan());
                        var decodedPcmChunk = _g711Codec.DecodeG711MuLaw(g711Chunk.AsSpan());
                        allProcessedData.AddRange(decodedPcmChunk);
                    }
                }

                var processedSamples = allProcessedData.Count / 2;
                var expectedSamples = (int)(totalOriginalSamples * (double)aiProfile.SampleRate / originalSampleRate);

                _logger.LogInformation($"G.711处理结果: {totalOriginalSamples}@{originalSampleRate}Hz -> {processedSamples}@{aiProfile.SampleRate}Hz (预期: {expectedSamples})");

                if (allProcessedData.Count == 0) {
                    _logger.LogError("G.711处理失败：没有输出数据");
                    return BadRequest("❌ G.711处理失败：没有输出数据！");
                }

                var processedFloat = ConvertPcm16ToFloat(allProcessedData.ToArray());
                var wavData = ConvertToWav(processedFloat, aiProfile.SampleRate);

                Response.Headers.Add("Content-Disposition", $"inline; filename=\"g711_processed_{aiProfile.SampleRate}Hz.wav\"");
                return File(wavData, "audio/wav");
            } catch (Exception ex) {
                _logger.LogError(ex, "生成G.711处理音频失败");
                return BadRequest($"生成G.711处理音频失败: {ex.Message}");
            }
        }

        private byte[] SimulateAIEnqueueFloatPcm(float[] src, int ttsSampleRate, MediaProfile profile) {
            _logger.LogDebug($"模拟AI处理: {src.Length} samples from {ttsSampleRate}Hz to {profile.SampleRate}Hz");
            
            // 步骤1: 计算帧大小 (完全按照AI代码)
            int frame = profile.SamplesPerFrame * 2;
            _logger.LogDebug($"AI帧大小: {frame} bytes (SamplesPerFrame: {profile.SamplesPerFrame})");

            float[] processedFloat = src;
            if (src.Length > 0 && ttsSampleRate != 8000) {
                using var resampler = new AudioResampler<float>(
                    ttsSampleRate,
                    8000,
                    _logger);
                processedFloat = resampler.Resample(src);
                _logger.LogDebug($"Resampled audio from {ttsSampleRate}Hz to {8000}Hz");
            }

            var shortSamples = new short[processedFloat.Length];
            for (int i = 0; i < processedFloat.Length; i++) {
                float sample = processedFloat[i];
                if (float.IsNaN(sample) || float.IsInfinity(sample)) {
                    sample = 0f;
                } else {
                    sample = Math.Clamp(sample, -1f, 1f);
                }

                int intSample = (int)MathF.Round(sample * 32767f);
                shortSamples[i] = (short)Math.Clamp(intSample, short.MinValue, short.MaxValue);
            }

            var byteArray = new byte[shortSamples.Length * 2];
            for (int i = 0; i < shortSamples.Length; i++) {
                short sample = shortSamples[i];
                byteArray[i * 2] = (byte)(sample & 0xFF);        // 低字节
                byteArray[i * 2 + 1] = (byte)((sample >> 8) & 0xFF); // 高字节
            }

            // 步骤4: 分帧处理 (完全按照AI代码) - 这是关键差异！
            var finalProcessed = new List<byte>();
            var k = 0;
            while (k < byteArray.Length) {
                int len = Math.Min(frame, byteArray.Length - k);
                var frameData = new byte[len];
                Array.Copy(byteArray, k, frameData, 0, len);
                finalProcessed.AddRange(frameData);
                k += len;
                _logger.LogTrace($"AI分帧: {len} bytes (帧大小: {frame})");
            }

            _logger.LogDebug($"AI处理完成: {finalProcessed.Count} bytes");
            return finalProcessed.ToArray();
        }

        private byte[] CreateWavHeader(int dataLength, int sampleRate) {
            var header = new byte[44];
            var fileSize = dataLength + 36;

            // RIFF header
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
            BitConverter.GetBytes(fileSize).CopyTo(header, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);

            // fmt chunk
            System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
            BitConverter.GetBytes(16).CopyTo(header, 16); // fmt chunk size
            BitConverter.GetBytes((short)1).CopyTo(header, 20); // PCM format
            BitConverter.GetBytes((short)1).CopyTo(header, 22); // mono
            BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
            BitConverter.GetBytes(sampleRate * 2).CopyTo(header, 28); // byte rate
            BitConverter.GetBytes((short)2).CopyTo(header, 32); // block align
            BitConverter.GetBytes((short)16).CopyTo(header, 34); // bits per sample

            // data chunk
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
            BitConverter.GetBytes(dataLength).CopyTo(header, 40);

            return header;
        }
    }
}