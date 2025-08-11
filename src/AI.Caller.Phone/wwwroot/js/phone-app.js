/**
 * 电话应用主模块
 * 负责协调各个子模块的工作
 */
class PhoneApp {
    constructor() {
        this.elements = this.initializeElements();
        this.callStateManager = null;
        this.signalRManager = null;
        this.webRTCManager = null;
        this.recordingManager = null;
        this.uiManager = null;
        this.hangupHandler = null;
        
        this.isInitialized = false;
    }

    initializeElements() {
        return {
            destinationInput: document.getElementById('destination'),
            callButton: document.getElementById('callButton'),
            answerButton: document.getElementById('answerButton'),
            hangupButton: document.getElementById('hangupButton'),
            startRecordingButton: document.getElementById('startRecordingButton'),
            stopRecordingButton: document.getElementById('stopRecordingButton'),
            statusDiv: document.getElementById('status'),
            statusAlert: document.getElementById('statusAlert'),
            recordingStatusAlert: document.getElementById('recordingStatusAlert'),
            recordingStatus: document.getElementById('recordingStatus'),
            recordingTimer: document.getElementById('recordingTimer'),
            recordingIcon: document.getElementById('recordingIcon'),
            callerName: document.getElementById('callerName'),
            callerNumber: document.getElementById('callerNumber'),
            callInfo: document.getElementById('callInfo'),
            callTimer: document.getElementById('callTimer'),
            remoteAudio: document.getElementById('remoteAudio')
        };
    }

    async initialize() {
        try {
            this.uiManager = new UIManager(this.elements);
            this.uiManager.updateStatus('正在初始化...', 'info');

            // 初始化状态管理器
            this.callStateManager = new CallStateManager(this.elements);
            
            // 初始化WebRTC管理器
            this.webRTCManager = new WebRTCManager(this.elements);
            await this.webRTCManager.initialize();

            // 初始化SignalR管理器
            this.signalRManager = new SignalRManager(this.elements, this.callStateManager, this.webRTCManager);
            await this.signalRManager.initialize();

            // 初始化录音管理器
            this.recordingManager = new SimpleRecordingManager(this.elements, this.signalRManager, this.callStateManager);
            this.recordingManager.initialize();

            // 初始化挂断处理器
            this.hangupHandler = new HangupHandler(
                this.signalRManager.connection, 
                {
                    ...this.elements,
                    updateStatus: (message, type) => this.uiManager.updateStatus(message, type),
                    clearCallUI: () => this.clearCallUI()
                }, 
                this.callStateManager
            );

            // 设置事件监听器
            this.setupEventListeners();

            this.isInitialized = true;
            this.uiManager.updateStatus('就绪', 'success');
            console.log('电话应用初始化完成');

        } catch (error) {
            console.error('初始化失败:', error);
            this.uiManager.updateStatus('初始化失败: ' + error.message, 'danger');
            throw error;
        }
    }

    setupEventListeners() {
        // 拨号盘事件
        this.setupDialpadEvents();
        
        // 联系人选择事件
        this.setupContactEvents();
        
        // 通话控制事件
        this.setupCallControlEvents();
        
        // 录音控制事件
        this.setupRecordingEvents();
        
        // 全局事件
        this.setupGlobalEvents();
    }

    setupDialpadEvents() {
        document.querySelectorAll('.dialpad-btn').forEach(button => {
            button.addEventListener('click', () => {
                const key = button.getAttribute('data-key');
                this.elements.destinationInput.value += key;
                this.elements.destinationInput.focus();
            });
        });
    }

    setupContactEvents() {
        document.querySelectorAll('.contact-item').forEach(item => {
            item.addEventListener('click', () => {
                const name = item.getAttribute('data-name');
                const phone = item.getAttribute('data-phone');
                
                this.elements.destinationInput.value = phone;
                this.elements.callerName.textContent = name;
                this.elements.callerNumber.textContent = phone;
            });
        });
    }

    setupCallControlEvents() {
        this.elements.callButton.addEventListener('click', () => this.handleCall());
        this.elements.answerButton.addEventListener('click', () => this.handleAnswer());
        this.elements.hangupButton.addEventListener('click', () => this.handleHangup());
    }

    setupRecordingEvents() {
        this.elements.startRecordingButton.addEventListener('click', () => {
            this.recordingManager.startRecording();
        });
        
        this.elements.stopRecordingButton.addEventListener('click', () => {
            this.recordingManager.stopRecording();
        });
    }

    setupGlobalEvents() {
        // 网络状态监听
        window.addEventListener('online', () => {
            this.uiManager.updateStatus('网络已恢复连接', 'success');
            setTimeout(() => this.uiManager.updateStatus('就绪', 'success'), 3000);
        });
        
        window.addEventListener('offline', () => {
            this.uiManager.updateStatus('网络已断开，通话可能受到影响', 'danger');
            if (this.callStateManager) {
                this.callStateManager.resetToIdle();
            }
        });

        // 通话事件监听
        document.addEventListener('callEnded', (event) => {
            console.log('收到通话结束事件:', event.detail);
            this.uiManager.showCallInfo(false);
            this.uiManager.stopCallTimer();
        });
        
        document.addEventListener('remoteHangup', (event) => {
            console.log('收到对方挂断事件:', event.detail);
            this.uiManager.showCallInfo(false);
            this.uiManager.stopCallTimer();
        });
    }

    async handleCall() {
        const destination = this.elements.destinationInput.value;
        if (!destination) {
            alert('请输入电话号码或选择联系人');
            return;
        }

        try {
            console.log('开始呼叫:', destination);
            this.callStateManager.setState(CallState.OUTGOING);
            this.uiManager.updateStatus('正在呼叫...', 'warning');
            
            const sdpOffer = await this.webRTCManager.createPeerConnection(true, null);

            const response = await fetch('/api/phone/Call', {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({Destination: destination, Offer: sdpOffer })
            });

            this.elements.callerName.innerHTML = destination;
            
        } catch (error) {
            console.error('呼叫失败:', error);
            this.handleCallError(error);
        }
    }

    async handleAnswer() {
        this.uiManager.updateStatus('正在接听...', 'warning');
        try {
            const answerSdp = await this.webRTCManager.createPeerConnection(
                false, 
                this.elements.answerButton.attributes['data-offer']
            );
            
            console.log('接听SDP:', answerSdp);

            var extral = this.elements.callerNumber.innerHTML;
            var number = this.elements.callerName.innerText;
            if(extral && extral.indexOf('→')){
                number = extral.split('→')[1].trim();
            }

            await this.signalRManager.connection.invoke("AnswerAsync", {
                caller: number, 
                answerSdp: JSON.stringify(answerSdp)
            });

            this.uiManager.updateStatus('接听成功', 'success');
            this.callStateManager.setState(CallState.CONNECTED);
            this.uiManager.stopCallTimer();
            this.uiManager.startCallTimer();
            this.uiManager.showCallInfo(true);

            // 检查安全状态
            this.checkSecureConnection();
            
        } catch (error) {
            this.uiManager.updateStatus(`接听失败: ${error.message}`, 'danger');
            this.callStateManager.resetToIdle();
        }
    }

    async handleHangup() {
        if (this.hangupHandler) {
            await this.hangupHandler.initiateHangup('用户主动挂断');
        } else {
            console.log('开始挂断通话');
            this.callStateManager.setState(CallState.ENDING);
            this.uiManager.updateStatus('正在挂断...', 'warning');
            
            await fetch('/api/phone/Hangup', {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: null
            });
        }
        
        this.callStateManager.resetToIdle();
        this.uiManager.showCallInfo(false);
        this.uiManager.updateStatus('已挂断...', 'success');
    }

    handleCallError(error) {
        let errorMessage = error.message;
        
        if (errorMessage.includes('JSON')) {
            errorMessage = '服务器响应格式错误，请联系管理员';
        } else if (errorMessage.includes('WebRTC')) {
            errorMessage = 'WebRTC连接失败，请检查网络连接或浏览器设置';
        } else if (errorMessage.includes('服务器错误')) {
            errorMessage = '服务器处理请求失败，请稍后再试';
        }
        
        this.uiManager.updateStatus(`呼叫失败: ${errorMessage}`, 'danger');
        this.callStateManager.resetToIdle();
        this.uiManager.showCallInfo(false);
    }

    async checkSecureConnection() {
        try {
            const isSecure = await this.signalRManager.connection.invoke("GetSecureContextState");
            if (!isSecure) {
                this.uiManager.updateStatus('当前连接不安全，通话质量可能受影响', 'warning');
            } else {
                this.uiManager.updateStatus('安全链接通话中...', 'info');
            }
        } catch (error) {
            console.warn('检查安全状态失败:', error);
        }
    }

    clearCallUI() {
        console.log('PhoneApp: 清理通话UI');
        
        // 使用UIManager清理UI
        this.uiManager.clearCallUI();
        
        // 重置状态管理器
        if (this.callStateManager) {
            this.callStateManager.resetToIdle();
        }
        
        // 清理WebRTC连接
        if (this.webRTCManager && this.webRTCManager.pc) {
            try {
                this.webRTCManager.pc.close();
                this.webRTCManager.pc = null;
                console.log('WebRTC连接已关闭');
            } catch (error) {
                console.warn('关闭WebRTC连接时出错:', error);
            }
        }
        
        // 停止录音（如果正在录音）
        if (this.recordingManager && window.isRecording) {
            this.recordingManager.stopRecording();
        }
        
        console.log('PhoneApp: UI清理完成');
    }

    // 调试方法
    getDebugInfo() {
        return {
            isInitialized: this.isInitialized,
            callState: this.callStateManager?.getCurrentState(),
            signalRState: this.signalRManager?.getConnectionState(),
            webRTCState: this.webRTCManager?.getConnectionState(),
            recordingState: this.recordingManager?.getRecordingState()
        };
    }
}

// 全局调试函数
window.debugCallState = function() {
    if (window.phoneApp) {
        console.log('=== 通话状态调试信息 ===');
        console.log(window.phoneApp.getDebugInfo());
        console.log('=== 调试信息结束 ===');
    } else {
        console.log('PhoneApp未初始化');
    }
};