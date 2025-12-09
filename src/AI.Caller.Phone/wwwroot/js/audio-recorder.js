/**
 * AudioRecorder - 在线录音管理器
 * 用于场景录音的浏览器端录制功能
 * 
 * 功能：
 * - 使用 MediaRecorder API 录制音频
 * - 录音控制（开始、暂停、停止、重录）
 * - 实时波形显示
 * - 录音时长显示和限制
 * - 试听功能
 * - 上传到服务器
 */
class AudioRecorder {
    constructor() {
        // 状态管理
        this.state = 'idle'; // idle, recording, paused, stopped
        
        // MediaRecorder 相关
        this.mediaRecorder = null;
        this.audioChunks = [];
        this.stream = null;
        
        // 录音限制
        this.maxDuration = 300000; // 5分钟（毫秒）
        this.maxFileSize = 10 * 1024 * 1024; // 10MB
        
        // 计时器
        this.recordingStartTime = null;
        this.recordingDuration = 0;
        this.timerInterval = null;
        
        // 波形显示
        this.audioContext = null;
        this.analyser = null;
        this.dataArray = null;
        this.animationId = null;
        
        // 录音结果
        this.recordedBlob = null;
        this.recordedUrl = null;
        
        // DOM 元素（延迟初始化）
        this.elements = {};
        
        console.log('AudioRecorder 已创建');
    }
    
    /**
     * 初始化 - 绑定 DOM 元素
     */
    initialize() {
        this.elements = {
            startBtn: document.getElementById('startRecordBtn'),
            pauseBtn: document.getElementById('pauseRecordBtn'),
            stopBtn: document.getElementById('stopRecordBtn'),
            resetBtn: document.getElementById('resetRecordBtn'),
            uploadBtn: document.getElementById('uploadRecordBtn'),
            
            statusText: document.getElementById('recordStatus'),
            durationText: document.getElementById('recordDuration'),
            
            waveformCanvas: document.getElementById('waveformCanvas'),
            previewSection: document.getElementById('previewSection'),
            previewAudio: document.getElementById('previewAudio')
        };
        
        // 检查浏览器支持
        if (!this.checkBrowserSupport()) {
            this.showError('您的浏览器不支持在线录音功能，请使用 Chrome、Edge 或 Firefox 浏览器');
            if (this.elements.startBtn) {
                this.elements.startBtn.disabled = true;
            }
            return false;
        }
        
        console.log('AudioRecorder 初始化完成');
        return true;
    }
    
    /**
     * 检查浏览器支持
     */
    checkBrowserSupport() {
        return !!(navigator.mediaDevices && 
                  navigator.mediaDevices.getUserMedia && 
                  window.MediaRecorder);
    }
    
    /**
     * 开始录音
     */
    async startRecording() {
        try {
            if (this.state === 'paused') {
                // 恢复录音
                this.resumeRecording();
                return;
            }
            
            // 请求麦克风权限
            this.updateStatus('正在请求麦克风权限...', 'info');
            
            this.stream = await navigator.mediaDevices.getUserMedia({ 
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });
            
            // 创建 MediaRecorder
            const options = { mimeType: this.getSupportedMimeType() };
            this.mediaRecorder = new MediaRecorder(this.stream, options);
            
            // 重置数据
            this.audioChunks = [];
            this.recordingDuration = 0;
            
            // 监听数据
            this.mediaRecorder.ondataavailable = (event) => {
                if (event.data.size > 0) {
                    this.audioChunks.push(event.data);
                }
            };
            
            // 监听停止
            this.mediaRecorder.onstop = () => {
                this.handleRecordingStopped();
            };
            
            // 开始录音
            this.mediaRecorder.start(100); // 每100ms收集一次数据
            this.state = 'recording';
            this.recordingStartTime = Date.now();
            
            // 启动计时器
            this.startTimer();
            
            // 启动波形显示
            this.startWaveform();
            
            // 更新UI
            this.updateStatus('正在录音...', 'danger');
            this.updateUI();
            
            console.log('录音已开始');
            
        } catch (error) {
            console.error('开始录音失败:', error);
            
            if (error.name === 'NotAllowedError') {
                this.showError('麦克风权限被拒绝，请允许访问麦克风');
            } else if (error.name === 'NotFoundError') {
                this.showError('未找到麦克风设备');
            } else {
                this.showError('无法访问麦克风: ' + error.message);
            }
            
            this.cleanup();
        }
    }
    
    /**
     * 暂停录音
     */
    pauseRecording() {
        if (this.state !== 'recording') return;
        
        if (this.mediaRecorder && this.mediaRecorder.state === 'recording') {
            this.mediaRecorder.pause();
            this.state = 'paused';
            
            // 停止计时器
            this.stopTimer();
            
            // 停止波形
            this.stopWaveform();
            
            // 更新UI
            this.updateStatus('录音已暂停', 'warning');
            this.updateUI();
            
            console.log('录音已暂停');
        }
    }
    
    /**
     * 恢复录音
     */
    resumeRecording() {
        if (this.state !== 'paused') return;
        
        if (this.mediaRecorder && this.mediaRecorder.state === 'paused') {
            this.mediaRecorder.resume();
            this.state = 'recording';
            
            // 恢复计时器
            this.startTimer();
            
            // 恢复波形
            this.startWaveform();
            
            // 更新UI
            this.updateStatus('正在录音...', 'danger');
            this.updateUI();
            
            console.log('录音已恢复');
        }
    }
    
    /**
     * 停止录音
     */
    stopRecording() {
        if (this.state !== 'recording' && this.state !== 'paused') return;
        
        if (this.mediaRecorder) {
            this.mediaRecorder.stop();
            // handleRecordingStopped 会在 onstop 事件中被调用
        }
        
        // 停止计时器
        this.stopTimer();
        
        // 停止波形
        this.stopWaveform();
        
        console.log('录音已停止');
    }
    
    /**
     * 处理录音停止
     */
    handleRecordingStopped() {
        // 创建 Blob
        const mimeType = this.getSupportedMimeType();
        this.recordedBlob = new Blob(this.audioChunks, { type: mimeType });
        
        // 检查文件大小
        if (this.recordedBlob.size > this.maxFileSize) {
            this.showError('录音文件过大（超过10MB），请重新录制');
            this.resetRecording();
            return;
        }
        
        // 创建预览 URL
        this.recordedUrl = URL.createObjectURL(this.recordedBlob);
        
        // 显示试听
        if (this.elements.previewAudio) {
            this.elements.previewAudio.src = this.recordedUrl;
            this.elements.previewSection.style.display = 'block';
        }
        
        // 更新状态
        this.state = 'stopped';
        this.updateStatus('录音完成，可以试听或保存', 'success');
        this.updateUI();
        
        // 释放媒体流
        this.releaseStream();
        
        console.log('录音处理完成，大小:', this.formatFileSize(this.recordedBlob.size));
    }
    
    /**
     * 重新录制
     */
    resetRecording() {
        // 清理现有录音
        this.cleanup();
        
        // 重置状态
        this.state = 'idle';
        this.audioChunks = [];
        this.recordedBlob = null;
        this.recordingDuration = 0;
        
        // 清除预览
        if (this.recordedUrl) {
            URL.revokeObjectURL(this.recordedUrl);
            this.recordedUrl = null;
        }
        
        if (this.elements.previewAudio) {
            this.elements.previewAudio.src = '';
            this.elements.previewSection.style.display = 'none';
        }
        
        // 更新UI
        this.updateStatus('就绪', 'secondary');
        this.updateDuration(0);
        this.updateUI();
        
        // 清空波形
        this.clearWaveform();
        
        console.log('已重置，可以重新录制');
    }
    
    /**
     * 上传录音到服务器
     */
    async uploadRecording(fileName = null) {
        if (!this.recordedBlob) {
            this.showError('没有可上传的录音');
            return null;
        }
        
        try {
            // 生成文件名
            if (!fileName) {
                const timestamp = new Date().getTime();
                const extension = this.getFileExtension();
                fileName = `recording_${timestamp}.${extension}`;
            }
            
            // 创建 FormData
            const formData = new FormData();
            formData.append('file', this.recordedBlob, fileName);
            formData.append('fileName', fileName);
            
            // 更新状态
            this.updateStatus('正在上传...', 'info');
            if (this.elements.uploadBtn) {
                this.elements.uploadBtn.disabled = true;
                this.elements.uploadBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>上传中...';
            }
            
            // 上传
            const response = await fetch('/ScenarioRecording/UploadOnlineRecording', {
                method: 'POST',
                body: formData
            });
            
            const result = await response.json();
            
            if (result.success) {
                this.updateStatus('上传成功！', 'success');
                console.log('录音上传成功:', result.filePath);
                return result;
            } else {
                this.showError('上传失败: ' + result.message);
                return null;
            }
            
        } catch (error) {
            console.error('上传录音失败:', error);
            this.showError('上传失败: ' + error.message);
            return null;
            
        } finally {
            // 恢复按钮
            if (this.elements.uploadBtn) {
                this.elements.uploadBtn.disabled = false;
                this.elements.uploadBtn.innerHTML = '<i class="bi bi-cloud-upload-fill"></i> 保存到服务器';
            }
        }
    }
    
    /**
     * 启动计时器
     */
    startTimer() {
        this.timerInterval = setInterval(() => {
            this.recordingDuration = Date.now() - this.recordingStartTime;
            this.updateDuration(this.recordingDuration);
            
            // 检查是否超时
            if (this.recordingDuration >= this.maxDuration) {
                this.showError('已达到最大录音时长（5分钟），自动停止');
                this.stopRecording();
            }
        }, 100);
    }
    
    /**
     * 停止计时器
     */
    stopTimer() {
        if (this.timerInterval) {
            clearInterval(this.timerInterval);
            this.timerInterval = null;
        }
    }
    
    /**
     * 启动波形显示
     */
    startWaveform() {
        if (!this.elements.waveformCanvas || !this.stream) return;
        
        try {
            // 创建音频上下文
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            const source = this.audioContext.createMediaStreamSource(this.stream);
            
            // 创建分析器
            this.analyser = this.audioContext.createAnalyser();
            this.analyser.fftSize = 2048;
            source.connect(this.analyser);
            
            const bufferLength = this.analyser.frequencyBinCount;
            this.dataArray = new Uint8Array(bufferLength);
            
            // 开始绘制
            this.drawWaveform();
            
        } catch (error) {
            console.error('启动波形显示失败:', error);
        }
    }
    
    /**
     * 绘制波形
     */
    drawWaveform() {
        if (!this.elements.waveformCanvas || !this.analyser) return;
        
        const canvas = this.elements.waveformCanvas;
        const canvasCtx = canvas.getContext('2d');
        const width = canvas.width;
        const height = canvas.height;
        
        const draw = () => {
            this.animationId = requestAnimationFrame(draw);
            
            this.analyser.getByteTimeDomainData(this.dataArray);
            
            // 清空画布
            canvasCtx.fillStyle = 'rgb(255, 255, 255)';
            canvasCtx.fillRect(0, 0, width, height);
            
            // 绘制波形
            canvasCtx.lineWidth = 2;
            canvasCtx.strokeStyle = 'rgb(13, 110, 253)'; // Bootstrap primary color
            canvasCtx.beginPath();
            
            const sliceWidth = width / this.dataArray.length;
            let x = 0;
            
            for (let i = 0; i < this.dataArray.length; i++) {
                const v = this.dataArray[i] / 128.0;
                const y = v * height / 2;
                
                if (i === 0) {
                    canvasCtx.moveTo(x, y);
                } else {
                    canvasCtx.lineTo(x, y);
                }
                
                x += sliceWidth;
            }
            
            canvasCtx.lineTo(width, height / 2);
            canvasCtx.stroke();
        };
        
        draw();
    }
    
    /**
     * 停止波形显示
     */
    stopWaveform() {
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }
    }
    
    /**
     * 清空波形
     */
    clearWaveform() {
        if (!this.elements.waveformCanvas) return;
        
        const canvas = this.elements.waveformCanvas;
        const canvasCtx = canvas.getContext('2d');
        canvasCtx.fillStyle = 'rgb(255, 255, 255)';
        canvasCtx.fillRect(0, 0, canvas.width, canvas.height);
    }
    
    /**
     * 更新UI状态
     */
    updateUI() {
        if (!this.elements.startBtn) return;
        
        const { startBtn, pauseBtn, stopBtn, resetBtn, uploadBtn } = this.elements;
        
        switch (this.state) {
            case 'idle':
                startBtn.disabled = false;
                startBtn.innerHTML = '<i class="bi bi-mic-fill"></i> 开始录音';
                pauseBtn.disabled = true;
                stopBtn.disabled = true;
                resetBtn.disabled = true;
                uploadBtn.disabled = true;
                break;
                
            case 'recording':
                startBtn.disabled = true;
                pauseBtn.disabled = false;
                stopBtn.disabled = false;
                resetBtn.disabled = true;
                uploadBtn.disabled = true;
                break;
                
            case 'paused':
                startBtn.disabled = false;
                startBtn.innerHTML = '<i class="bi bi-play-fill"></i> 继续录音';
                pauseBtn.disabled = true;
                stopBtn.disabled = false;
                resetBtn.disabled = true;
                uploadBtn.disabled = true;
                break;
                
            case 'stopped':
                startBtn.disabled = true;
                pauseBtn.disabled = true;
                stopBtn.disabled = true;
                resetBtn.disabled = false;
                uploadBtn.disabled = false;
                break;
        }
    }
    
    /**
     * 更新状态文本
     */
    updateStatus(message, type = 'info') {
        if (this.elements.statusText) {
            this.elements.statusText.textContent = message;
        }
        
        // 更新父级 alert 的样式
        const alertDiv = this.elements.statusText?.closest('.alert');
        if (alertDiv) {
            alertDiv.className = `alert alert-${type} mb-3`;
        }
    }
    
    /**
     * 更新时长显示
     */
    updateDuration(milliseconds) {
        if (!this.elements.durationText) return;
        
        const totalSeconds = Math.floor(milliseconds / 1000);
        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        
        this.elements.durationText.textContent = 
            `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
    }
    
    /**
     * 显示错误
     */
    showError(message) {
        console.error('AudioRecorder Error:', message);
        alert(message);
    }
    
    /**
     * 释放媒体流
     */
    releaseStream() {
        if (this.stream) {
            this.stream.getTracks().forEach(track => track.stop());
            this.stream = null;
        }
    }
    
    /**
     * 清理资源
     */
    cleanup() {
        // 停止录音
        if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
            this.mediaRecorder.stop();
        }
        
        // 停止计时器
        this.stopTimer();
        
        // 停止波形
        this.stopWaveform();
        
        // 释放媒体流
        this.releaseStream();
        
        // 关闭音频上下文
        if (this.audioContext) {
            this.audioContext.close();
            this.audioContext = null;
        }
        
        this.mediaRecorder = null;
        this.analyser = null;
        this.dataArray = null;
    }
    
    /**
     * 获取支持的 MIME 类型
     */
    getSupportedMimeType() {
        const types = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
            'audio/ogg',
            'audio/mp4'
        ];
        
        for (const type of types) {
            if (MediaRecorder.isTypeSupported(type)) {
                return type;
            }
        }
        
        return 'audio/webm'; // 默认
    }
    
    /**
     * 获取文件扩展名
     */
    getFileExtension() {
        const mimeType = this.getSupportedMimeType();
        if (mimeType.includes('webm')) return 'webm';
        if (mimeType.includes('ogg')) return 'ogg';
        if (mimeType.includes('mp4')) return 'mp4';
        return 'webm';
    }
    
    /**
     * 格式化文件大小
     */
    formatFileSize(bytes) {
        if (bytes === 0) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
    }
}

// 导出到 PhoneApp 命名空间
window.PhoneApp = window.PhoneApp || {};
window.PhoneApp.AudioRecorder = AudioRecorder;

console.log('AudioRecorder 类已加载到 PhoneApp 命名空间');
