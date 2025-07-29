/**
 * 录音管理器
 * 负责录音功能的控制和状态管理
 */
class RecordingManager {
    constructor(elements, signalRManager, callStateManager) {
        this.elements = elements;
        this.signalRManager = signalRManager;
        this.callStateManager = callStateManager;
        this.isRecording = false;
        this.isAutoRecording = false;
        this.recordingStartTime = null;
        this.recordingTimerInterval = null;
    }

    initialize() {
        // 初始化全局录音状态
        window.isRecording = false;
        window.isAutoRecording = false;
        
        console.log('录音管理器已初始化');
    }

    async startRecording() {
        try {
            console.log('手动开始录音');
            this.updateRecordingStatus('正在开始录音...', 'warning');
            
            // 检查SignalR连接状态
            if (!this.checkSignalRConnection()) {
                return;
            }
            
            const calleeNumber = this.getCalleeNumber();
            console.log('调用StartRecordingAsync，参数:', calleeNumber);
            
            const result = await this.signalRManager.connection.invoke("StartRecordingAsync", calleeNumber);
            console.log('StartRecordingAsync返回结果:', result);
            
            this.handleRecordingResult(result, '录音开始');
            
        } catch (error) {
            console.error('开始录音时发生错误:', error);
            this.handleRecordingError(error, '录音开始失败');
        }
    }

    async stopRecording() {
        try {
            console.log('手动停止录音');
            this.updateRecordingStatus('正在停止录音...', 'warning');
            
            // 检查SignalR连接状态
            if (!this.checkSignalRConnection()) {
                return;
            }
            
            console.log('调用StopRecordingAsync');
            const result = await this.signalRManager.connection.invoke("StopRecordingAsync");
            console.log('StopRecordingAsync返回结果:', result);
            
            this.handleRecordingResult(result, '录音停止');
            
        } catch (error) {
            console.error('停止录音时发生错误:', error);
            this.handleRecordingError(error, '录音停止失败');
        }
    }

    checkSignalRConnection() {
        if (this.signalRManager.connection.state !== signalR.HubConnectionState.Connected) {
            console.error('SignalR连接未建立，当前状态:', this.signalRManager.connection.state);
            this.updateRecordingStatus('连接未建立，无法操作录音', 'danger');
            return false;
        }
        return true;
    }

    getCalleeNumber() {
        return this.elements.destinationInput.value || 
               this.elements.callerNumber.textContent || 
               '未知';
    }

    handleRecordingResult(result, operation) {
        if (result && result.success) {
            console.log(`${operation}成功:`, result);
            this.updateRecordingStatus(`${operation}成功`, 'success');
        } else {
            console.error(`${operation}失败:`, result);
            this.updateRecordingStatus(`${operation}失败: ` + (result?.message || '未知错误'), 'danger');
        }
    }

    handleRecordingError(error, operation) {
        console.error('错误详情:', {
            name: error.name,
            message: error.message,
            stack: error.stack
        });
        this.updateRecordingStatus(`${operation}: ` + error.message, 'danger');
    }

    updateRecordingStatus(message, type = 'info') {
        this.elements.recordingStatus.textContent = message;
        this.elements.recordingStatusAlert.className = `alert alert-${type} mb-4`;
        
        if (type === 'danger') {
            this.elements.recordingStatusAlert.classList.remove('d-none');
            setTimeout(() => {
                this.elements.recordingStatusAlert.classList.add('d-none');
            }, 5000);
        }
    }

    startRecordingTimer() {
        this.recordingStartTime = new Date();
        this.elements.recordingTimer.textContent = "00:00";
        this.elements.recordingIcon.className = "bi bi-record-circle text-danger me-2";

        this.recordingTimerInterval = setInterval(() => {
            const now = new Date();
            const diff = new Date(now - this.recordingStartTime);
            const minutes = diff.getUTCMinutes().toString().padStart(2, '0');
            const seconds = diff.getUTCSeconds().toString().padStart(2, '0');
            this.elements.recordingTimer.textContent = `${minutes}:${seconds}`;
        }, 1000);
    }

    stopRecordingTimer() {
        if (this.recordingTimerInterval) {
            clearInterval(this.recordingTimerInterval);
            this.recordingTimerInterval = null;
        }
        this.elements.recordingIcon.className = "bi bi-stop-circle text-secondary me-2";
    }

    // 处理来自SignalR的录音事件
    handleRecordingStarted(data) {
        console.log("录音已开始:", data);
        this.isRecording = true;
        this.isAutoRecording = data.isAuto || false;
        
        // 更新全局状态
        window.isRecording = true;
        window.isAutoRecording = this.isAutoRecording;
        
        this.updateRecordingStatus(
            this.isAutoRecording ? '自动录音已开始' : '手动录音已开始', 
            'success'
        );
        this.elements.recordingStatusAlert.classList.remove('d-none');
        
        this.startRecordingTimer();
        this.callStateManager.updateButtonVisibility();
    }

    handleRecordingStopped(data) {
        console.log("录音已停止:", data);
        this.isRecording = false;
        
        // 更新全局状态
        window.isRecording = false;
        
        this.updateRecordingStatus('录音已停止并保存', 'info');
        setTimeout(() => {
            this.elements.recordingStatusAlert.classList.add('d-none');
        }, 3000);
        
        this.stopRecordingTimer();
        this.callStateManager.updateButtonVisibility();
    }

    handleRecordingError(data) {
        console.error("录音错误:", data);
        this.isRecording = false;
        
        // 更新全局状态
        window.isRecording = false;
        
        this.updateRecordingStatus('录音错误: ' + data.message, 'danger');
        this.stopRecordingTimer();
        this.callStateManager.updateButtonVisibility();
    }

    handleRecordingStatusUpdate(data) {
        console.log("录音状态更新:", data);
        if (data.status) {
            const statusText = this.getRecordingStatusText(data.status);
            this.updateRecordingStatus(statusText, data.status === 'Failed' ? 'danger' : 'info');
        }
    }

    getRecordingStatusText(status) {
        const statusMap = {
            'Recording': '录音中',
            'Completed': '录音完成',
            'Failed': '录音失败'
        };
        return statusMap[status] || status;
    }

    // 在通话状态变化时的处理
    onCallStateChanged(newState) {
        // 当通话结束时，自动停止录音计时器显示
        if (newState === CallState.IDLE && this.isRecording) {
            // 不改变录音状态，只是隐藏录音状态显示
            setTimeout(() => {
                if (!this.isRecording) {
                    this.elements.recordingStatusAlert.classList.add('d-none');
                }
            }, 5000);
        }
    }

    getRecordingState() {
        return {
            isRecording: this.isRecording,
            isAutoRecording: this.isAutoRecording,
            recordingStartTime: this.recordingStartTime
        };
    }

    cleanup() {
        this.stopRecordingTimer();
        this.isRecording = false;
        this.isAutoRecording = false;
        window.isRecording = false;
        window.isAutoRecording = false;
        
        console.log('录音管理器已清理');
    }
}