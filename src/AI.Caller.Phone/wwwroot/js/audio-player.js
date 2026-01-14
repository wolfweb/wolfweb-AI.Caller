class AudioPlayer {
    constructor(audioContext, options = {}) {
        this.ctx = audioContext;

        this.sampleRate = options.sampleRate || 8000;
        this.panValue = options.pan ?? 0;

        this.bufferThreshold = options.threshold ?? 15;

        this.lookahead = options.lookahead ?? 0.15;

        this.queue = [];
        this.nextTime = 0;
        this.timerId = null;
        this.isBuffering = true;
        this.isRunning = false;

        this.panner = this.ctx.createStereoPanner();
        this.panner.pan.value = this.panValue;
        this.panner.connect(this.ctx.destination);
    }

    push(pcmData) {
        if (!pcmData || pcmData.length === 0) return;

        if (!(pcmData instanceof Int16Array)) {
            console.warn("AudioPlayer: push 期望 Int16Array");
            return;
        }

        this.queue.push(pcmData);

        if (!this.isRunning) {
            this.startScheduler();
        }
    }

    startScheduler() {
        this.isRunning = true;
        this.isBuffering = true;
        this.schedule();
    }

    schedule() {
        if (!this.isRunning) return;

        const now = this.ctx.currentTime;

        if (this.isBuffering) {
            if (this.queue.length >= this.bufferThreshold) {
                this.isBuffering = false;
                this.nextTime = now + 0.02;
                console.log(`缓冲结束，开始播放 (积压${this.queue.length}帧)`);
            } else {
                this.timerId = setTimeout(() => this.schedule(), 20);
                return;
            }
        }

        if (this.nextTime < now - 0.08) {
            this.nextTime = now;
        }

        while (this.queue.length > 0 && this.nextTime < now + this.lookahead) {
            const pcm = this.queue.shift();
            this.playChunk(pcm);
        }

        if (this.queue.length === 0 && this.nextTime < now + 0.01) {
            console.log("缓冲耗尽 (Underrun)，重新缓冲...");
            this.isBuffering = true;
        }

        this.timerId = setTimeout(() => this.schedule(), 20);
    }

    playChunk(pcmData) {
        try {
            const samples = pcmData.length;
            const buffer = this.ctx.createBuffer(1, samples, this.sampleRate);
            const float32 = buffer.getChannelData(0);

            for (let i = 0; i < samples; i++) {
                float32[i] = pcmData[i] / 32768.0;
            }

            const source = this.ctx.createBufferSource();
            source.buffer = buffer;
            source.connect(this.panner);

            source.start(this.nextTime);

            this.nextTime += samples / this.sampleRate;

        } catch (e) {
            console.error("播放失败", e);
        }
    }

    reset() {
        this.isRunning = false;
        this.isBuffering = true;
        this.queue = [];
        this.nextTime = 0;

        if (this.timerId) {
            clearTimeout(this.timerId);
            this.timerId = null;
        }
    }
}