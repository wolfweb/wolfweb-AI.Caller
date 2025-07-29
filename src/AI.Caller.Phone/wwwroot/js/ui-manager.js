/**
 * UI管理器
 * 负责用户界面的更新和控制
 */
class UIManager {
    constructor(elements) {
        this.elements = elements;
        this.callTimerInterval = null;
        this.callStartTime = null;
    }

    updateStatus(text, type) {
        this.elements.statusDiv.textContent = text;
        this.elements.statusAlert.className = `alert mb-4 alert-${type}`;
    }

    showCallInfo(show) {
        if (show) {
            this.elements.callInfo.classList.remove('d-none');
        } else {
            this.elements.callInfo.classList.add('d-none');
        }
    }

    startCallTimer() {
        this.callStartTime = new Date();
        this.elements.callTimer.textContent = "00:00";

        this.callTimerInterval = setInterval(() => {
            const now = new Date();
            const diff = new Date(now - this.callStartTime);
            const minutes = diff.getUTCMinutes().toString().padStart(2, '0');
            const seconds = diff.getUTCSeconds().toString().padStart(2, '0');
            this.elements.callTimer.textContent = `${minutes}:${seconds}`;
        }, 1000);
    }

    stopCallTimer() {
        if (this.callTimerInterval) {
            clearInterval(this.callTimerInterval);
            this.callTimerInterval = null;
        }
    }

    toggleControls(inCall) {
        // 控制拨号按钮
        this.elements.callButton.disabled = inCall;
        if (!inCall) {
            this.elements.callButton.classList.remove('d-none');
        }
        
        // 控制挂断按钮
        this.elements.hangupButton.disabled = !inCall;
        
        // 控制输入框
        this.elements.destinationInput.disabled = inCall;

        // 控制拨号盘按钮
        document.querySelectorAll('.dialpad-btn').forEach(btn => {
            btn.disabled = inCall;
        });

        // 控制联系人按钮
        document.querySelectorAll('.contact-item').forEach(item => {
            item.disabled = inCall;
        });
    }

    clearCallUI() {
        console.log('清理通话UI和资源');

        // 停止通话计时器
        this.stopCallTimer();

        // 清理远程音频
        this.clearRemoteAudio();

        // 重置UI状态
        this.resetUIState();

        // 延迟更新状态，确保其他清理操作完成
        setTimeout(() => {
            this.updateStatus('就绪', 'success');
        }, 100);
        
        console.log('UI已重置');
    }

    clearRemoteAudio() {
        if (this.elements.remoteAudio.srcObject) {
            try {
                const tracks = this.elements.remoteAudio.srcObject.getTracks();
                tracks.forEach(track => {
                    track.stop();
                    console.log('远程音频轨道已停止');
                });
            } catch (error) {
                console.warn('停止远程音频轨道时出错:', error);
            } finally {
                this.elements.remoteAudio.srcObject = null;
                console.log('远程音频源已清除');
            }
        }

        try {
            this.elements.remoteAudio.pause();
            this.elements.remoteAudio.currentTime = 0;
            this.elements.remoteAudio.volume = 1.0; // 重置音量
            console.log('音频元素已重置');
        } catch (audioResetError) {
            console.warn('重置音频元素时出错:', audioResetError);
        }
    }

    resetUIState() {
        // 隐藏通话信息
        this.elements.callInfo.classList.add('d-none');
        
        // 启用输入框
        this.elements.destinationInput.disabled = false;
        
        // 重置通话计时器显示
        this.elements.callTimer.textContent = '00:00';
        
        // 启用拨号盘按钮
        document.querySelectorAll('.dialpad-btn').forEach(btn => {
            btn.disabled = false;
        });
        
        // 启用联系人按钮
        document.querySelectorAll('.contact-item').forEach(item => {
            item.disabled = false;
        });
    }

    setCallerInfo(name, number) {
        this.elements.callerName.textContent = name || '未知联系人';
        this.elements.callerNumber.textContent = number || '';
    }

    // 录音状态UI更新
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

    showRecordingStatus(show) {
        if (show) {
            this.elements.recordingStatusAlert.classList.remove('d-none');
        } else {
            this.elements.recordingStatusAlert.classList.add('d-none');
        }
    }

    // 按钮状态控制
    enableButton(button) {
        if (button) {
            button.disabled = false;
            button.classList.remove('d-none');
        }
    }

    disableButton(button) {
        if (button) {
            button.disabled = true;
            button.classList.add('d-none');
        }
    }

    // 输入框控制
    clearDestinationInput() {
        this.elements.destinationInput.value = '';
    }

    setDestinationInput(value) {
        this.elements.destinationInput.value = value;
    }

    getDestinationInput() {
        return this.elements.destinationInput.value;
    }

    // 焦点控制
    focusDestinationInput() {
        this.elements.destinationInput.focus();
    }

    // 状态指示器
    showLoadingStatus(message) {
        this.updateStatus(message, 'info');
    }

    showSuccessStatus(message) {
        this.updateStatus(message, 'success');
    }

    showWarningStatus(message) {
        this.updateStatus(message, 'warning');
    }

    showErrorStatus(message) {
        this.updateStatus(message, 'danger');
    }

    // 通话信息显示
    updateCallInfo(callerName, callerNumber, showTimer = false) {
        this.setCallerInfo(callerName, callerNumber);
        
        if (showTimer) {
            this.startCallTimer();
        }
        
        this.showCallInfo(true);
    }

    hideCallInfo() {
        this.showCallInfo(false);
        this.stopCallTimer();
    }

    // 获取当前UI状态
    getUIState() {
        return {
            callInfoVisible: !this.elements.callInfo.classList.contains('d-none'),
            recordingStatusVisible: !this.elements.recordingStatusAlert.classList.contains('d-none'),
            destinationValue: this.elements.destinationInput.value,
            callTimerRunning: this.callTimerInterval !== null,
            currentStatus: this.elements.statusDiv.textContent
        };
    }

    // 清理资源
    cleanup() {
        this.stopCallTimer();
        this.clearRemoteAudio();
        console.log('UI管理器已清理');
    }
}