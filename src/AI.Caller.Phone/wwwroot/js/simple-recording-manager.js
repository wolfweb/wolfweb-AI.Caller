/**
 * 简化的录音管理器
 * 负责基本的录音功能控制
 */
class SimpleRecordingManager {
    constructor(elements, signalRManager, callStateManager) {
        this.elements = elements;
        this.signalRManager = signalRManager;
        this.callStateManager = callStateManager;
        this.isRecording = false;
        this.recordingStartTime = null;
        this.recordingTimerInterval = null;
    }

    initialize() {
        // 初始化全局录音状态
        window.isRecording = false;
        window.isAutoRecording = false;
        
        console.log('简化录音管理器已初始化');
    }

    async startRecording() {
        try {
            console.log('开始录音');
            this.updateRecordingStatus('正在开始录音...', 'warning');
            
            if (!this.checkSignalRConnection()) {
                return;
            }
            
            const result = await this.signalRManager.connection.invoke("StartRecordingAsync");
            
            if (result && result.success) {
                this.updateRecordingStatus('录音已开始', 'success');
            } else {
                this.updateRecordingStatus('录音开始失败', 'danger');
            }
            
        } catch (error) {
            console.error('开始录音失败:', error);
            this.updateRecordingStatus('录音开始失败', 'danger');
        }
    }

    async stopRecording() {
        try {
            console.log('停止录音');
            this.updateRecordingStatus('正在停止录音...', 'warning');
            
            if (!this.checkSignalRConnection()) {
                return;
            }
            
            const result = await this.signalRManager.connection.invoke("StopRecordingAsync");
            
            if (result && result.success) {
                this.updateRecordingStatus('录音已停止', 'success');
            } else {
                this.updateRecordingStatus('录音停止失败', 'danger');
            }
            
        } catch (error) {
            console.error('停止录音失败:', error);
            this.updateRecordingStatus('录音停止失败', 'danger');
        }
    }

    checkSignalRConnection() {
        if (this.signalRManager.connection.state !== signalR.HubConnectionState.Connected) {
            console.error('SignalR连接未建立');
            this.updateRecordingStatus('连接未建立，无法操作录音', 'danger');
            return false;
        }
        return true;
    }

    updateRecordingStatus(message, type = 'info') {
        if (this.elements.recordingStatus) {
            this.elements.recordingStatus.textContent = message;
        }
        if (this.elements.recordingStatusAlert) {
            this.elements.recordingStatusAlert.className = `alert alert-${type} mb-4`;
            
            if (type === 'danger') {
                this.elements.recordingStatusAlert.classList.remove('d-none');
                setTimeout(() => {
                    this.elements.recordingStatusAlert.classList.add('d-none');
                }, 5000);
            }
        }
    }

    startRecordingTimer() {
        this.recordingStartTime = new Date();
        if (this.elements.recordingTimer) {
            this.elements.recordingTimer.textContent = "00:00";
        }
        if (this.elements.recordingIcon) {
            this.elements.recordingIcon.className = "bi bi-record-circle text-danger me-2";
        }

        this.recordingTimerInterval = setInterval(() => {
            const now = new Date();
            const diff = new Date(now - this.recordingStartTime);
            const minutes = diff.getUTCMinutes().toString().padStart(2, '0');
            const seconds = diff.getUTCSeconds().toString().padStart(2, '0');
            if (this.elements.recordingTimer) {
                this.elements.recordingTimer.textContent = `${minutes}:${seconds}`;
            }
        }, 1000);
    }

    stopRecordingTimer() {
        if (this.recordingTimerInterval) {
            clearInterval(this.recordingTimerInterval);
            this.recordingTimerInterval = null;
        }
        if (this.elements.recordingIcon) {
            this.elements.recordingIcon.className = "bi bi-stop-circle text-secondary me-2";
        }
    }

    // 处理来自SignalR的录音事件
    handleRecordingStarted(data) {
        console.log("录音已开始:", data);
        this.isRecording = true;
        window.isRecording = true;
        
        this.updateRecordingStatus('录音已开始', 'success');
        this.startRecordingTimer();
        
        if (this.callStateManager) {
            this.callStateManager.updateButtonVisibility();
        }
    }

    handleRecordingStopped(data) {
        console.log("录音已停止:", data);
        this.isRecording = false;
        window.isRecording = false;
        
        this.updateRecordingStatus('录音已停止', 'info');
        this.stopRecordingTimer();
        
        if (this.callStateManager) {
            this.callStateManager.updateButtonVisibility();
        }
    }

    handleRecordingError(data) {
        console.error("录音错误:", data);
        this.isRecording = false;
        window.isRecording = false;
        
        this.updateRecordingStatus('录音错误: ' + (data.message || '未知错误'), 'danger');
        this.stopRecordingTimer();
        
        if (this.callStateManager) {
            this.callStateManager.updateButtonVisibility();
        }
    }

    cleanup() {
        this.stopRecordingTimer();
        this.isRecording = false;
        window.isRecording = false;
        
        if (this.elements.recordingStatusAlert) {
            this.elements.recordingStatusAlert.classList.add('d-none');
        }
        
        console.log('简化录音管理器已清理');
    }
}