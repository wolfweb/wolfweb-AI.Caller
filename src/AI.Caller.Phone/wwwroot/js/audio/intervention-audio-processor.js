/**
 * AudioWorklet Processor for Intervention Audio
 * 用于人工接入时的麦克风音频处理
 * 替代已废弃的 ScriptProcessor
 */
class InterventionAudioProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.bufferSize = 1024;
        this.buffer = new Float32Array(this.bufferSize);
        this.bufferIndex = 0;
    }

    process(inputs, outputs, parameters) {
        const input = inputs[0];
        
        if (!input || !input[0]) {
            return true;
        }

        const inputChannel = input[0]; // 单声道
        
        for (let i = 0; i < inputChannel.length; i++) {
            this.buffer[this.bufferIndex++] = inputChannel[i];
            
            // 当缓冲区满时，发送数据
            if (this.bufferIndex >= this.bufferSize) {
                // 计算 RMS 音量
                let sumSquares = 0;
                for (let j = 0; j < this.bufferSize; j++) {
                    sumSquares += this.buffer[j] * this.buffer[j];
                }
                const rms = Math.sqrt(sumSquares / this.bufferSize);
                
                // Float32 → Int16 PCM 转换
                const int16Data = new Int16Array(this.bufferSize);
                for (let j = 0; j < this.bufferSize; j++) {
                    int16Data[j] = Math.max(-32768, Math.min(32767, this.buffer[j] * 32767));
                }
                
                // 发送音频数据和音量到主线程
                this.port.postMessage({
                    audioData: int16Data,
                    volume: rms
                });
                
                // 重置缓冲区
                this.bufferIndex = 0;
            }
        }
        
        return true; // 保持 processor 活跃
    }
}

registerProcessor('intervention-audio-processor', InterventionAudioProcessor);
