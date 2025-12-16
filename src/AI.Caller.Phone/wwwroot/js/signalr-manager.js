/**
 * SignalR连接管理器
 * 负责SignalR连接的建立、维护和事件处理
 */
class SignalRManager {
    constructor(elements, callStateManager, webRTCManager) {
        this.elements = elements;
        this.callStateManager = callStateManager;
        this.webRTCManager = webRTCManager;
        this.connection = null;
        this.isReconnecting = false;
        this.reconnectAttempts = 0;
        this.heartbeatInterval = null;
        this.heartbeatIntervalMs = 5000; // 5秒心跳间隔
        this.usingGlobalConnection = false; // 标记是否使用全局连接
    }

    async initialize() {
        // 使用全局SignalR连接
        await this.useGlobalConnection();
        this.setupPageSpecificHandlers();
    }

    /**
     * 使用全局SignalR连接
     */
    async useGlobalConnection() {
        // 等待全局SignalR管理器初始化
        await this.waitForGlobalSignalR();
        
        if (window.globalSignalRManager) {
            this.connection = window.globalSignalRManager.connection;
            console.log('Home页面已连接到全局SignalR');
            
            // 注册页面特定的事件处理器（只通过全局管理器注册）
            this.registerWithGlobalManager();
            
            // 标记使用全局连接，避免重复注册
            this.usingGlobalConnection = true;
        } else {
            console.error('全局SignalR管理器未找到，回退到独立连接');
            this.createIndependentConnection();
        }
    }

    /**
     * 等待全局SignalR管理器
     */
    async waitForGlobalSignalR() {
        return new Promise((resolve) => {
            let attempts = 0;
            const maxAttempts = 20; // 最多等待10秒
            
            const checkGlobalSignalR = () => {
                attempts++;
                
                if (window.globalSignalRManager && window.globalSignalRManager.isConnected) {
                    resolve();
                } else if (attempts >= maxAttempts) {
                    console.warn('等待全局SignalR超时');
                    resolve();
                } else {
                    setTimeout(checkGlobalSignalR, 500);
                }
            };
            
            checkGlobalSignalR();
        });
    }

    /**
     * 向全局管理器注册事件处理器
     */
    registerWithGlobalManager() {
        const handlerId = 'home-page';
        
        // 注册来电处理器
        window.globalSignalRManager.registerEventHandler(handlerId, 'inCalling', (callData) => {
            this.handleIncomingCall(callData);
        });
        
        // 注册其他事件处理器
        window.globalSignalRManager.registerEventHandler(handlerId, 'callTrying', (data) => {
            this.handleCallTrying(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'callRinging', (data) => {
            this.handleCallRinging(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'callAnswered', (data) => {
            this.handleCallAnswered(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'answered', (data) => {
            this.handleAnswered(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'callTimeout', (data) => {
            this.handleCallTimeout(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'sdpAnswered', (answerDesc) => {
            this.handleSdpAnswered(answerDesc);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'receiveIceCandidate', (candidate) => {
            this.handleIceCandidate(candidate);
        });
        
        // 注册录音事件处理器
        window.globalSignalRManager.registerEventHandler(handlerId, 'recordingStarted', (data) => {
            if (window.phoneApp && window.phoneApp.recordingManager) {
                window.phoneApp.recordingManager.handleRecordingStarted(data);
            }
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'recordingStopped', (data) => {
            if (window.phoneApp && window.phoneApp.recordingManager) {
                window.phoneApp.recordingManager.handleRecordingStopped(data);
            }
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'recordingError', (data) => {
            if (window.phoneApp && window.phoneApp.recordingManager) {
                window.phoneApp.recordingManager.handleRecordingError(data);
            }
        });
        
        console.log('Home页面事件处理器已注册到全局SignalR管理器');
    }

    /**
     * 创建独立连接（备用方案）
     */
    createIndependentConnection() {
        console.warn('全局SignalR不可用，创建独立连接');
        
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/webrtc")
            .configureLogging(signalR.LogLevel.Debug)
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: retryContext => {
                    const attempt = retryContext.previousRetryCount;
                    const delay = Math.min(1000 * Math.pow(2, attempt), 30000);
                    console.log(`SignalR重连尝试 #${attempt + 1}，等待 ${delay}ms`);
                    this.updateStatus(`SignalR重连尝试 #${attempt + 1}，等待 ${delay}ms`);
                    return delay;
                }
            })
            .build();
            
        // 标记使用独立连接
        this.usingGlobalConnection = false;
            
        // 只有在独立连接时才直接注册事件
        this.setupConnectionEvents();
        this.setupCallEvents();
        this.setupRecordingEvents();
        this.startConnection();
        this.startHeartbeat();
    }

    /**
     * 设置页面特定的处理器
     */
    setupPageSpecificHandlers() {
        // 页面卸载时注销事件处理器
        window.addEventListener('beforeunload', () => {
            if (window.globalSignalRManager) {
                window.globalSignalRManager.unregisterEventHandler('home-page');
            }
        });
    }

    setupConnectionEvents() {
        this.connection.onreconnecting((error) => {
            this.isReconnecting = true;
            this.reconnectAttempts++;
            console.log(`SignalR连接断开，开始第 ${this.reconnectAttempts} 次重连尝试:`, error);
            this.updateStatus(`连接已断开，正在重连... (尝试 ${this.reconnectAttempts})`, 'warning');
        });

        this.connection.onreconnected((connectionId) => {
            this.isReconnecting = false;
            console.log(`SignalR重连成功，连接ID: ${connectionId}`);
            this.updateStatus('连接已恢复', 'success');
            this.reconnectAttempts = 0;

            this.startHeartbeat();

            if(!this.webRTCManager.pc) 
                this.webRTCManager.initialize();

            if (this.webRTCManager.pc && 
                (this.webRTCManager.pc.iceConnectionState === 'disconnected' || 
                 this.webRTCManager.pc.iceConnectionState === 'failed' || 
                 this.webRTCManager.pc.connectionState === 'failed')) {
                console.log('WebRTC connection disrupted after SignalR reconnect, initiating WebRTC reconnect');
                this.webRTCManager.handleWebRTCReconnect();
            } else if (this.callStateManager.getCurrentState() === CallState.CONNECTED) {
                // 如果通话仍在进行，确保 WebRTC 状态正常
                console.log('Verifying WebRTC connection after SignalR reconnect');
                this.webRTCManager.checkSecureConnection();
            }
        });

        this.connection.onclose((error) => {
            this.isReconnecting = false;
            console.error('SignalR连接已关闭:', error);
            this.updateStatus('连接已断开，正在尝试重连...', 'danger');
            
            this.stopHeartbeat();
            
            this.startManualReconnect();
        });
    }

    setupCallEvents() {
        // 来电事件
        this.connection.on("inCalling", (callData) => {
            this.handleIncomingCall(callData);
        });

        // 呼叫中事件（Trying）
        this.connection.on("callTrying", (data) => {
            this.handleCallTrying(data);
        });

        // 对方振铃事件（Ringing）- 播放回铃音
        this.connection.on("callRinging", (data) => {
            this.handleCallRinging(data);
        });

        // 通话接听事件
        this.connection.on("callAnswered", async () => {
            this.handleCallAnswered();
        });

        this.connection.on("answered", () => {
            this.handleAnswered();
        });

        // 通话超时事件
        this.connection.on("callTimeout", () => {
            this.handleCallTimeout();
        });

        // SDP应答事件
        this.connection.on("sdpAnswered", (answerDesc) => {
            this.handleSdpAnswered(answerDesc);
        });

        // ICE候选者事件
        this.connection.on("receiveIceCandidate", (candidate) => {
            this.handleIceCandidate(candidate);
        });
    }

    setupRecordingEvents() {
        this.connection.on("recordingStarted", (data) => {
            if (window.phoneApp && window.phoneApp.recordingManager) {
                window.phoneApp.recordingManager.handleRecordingStarted(data);
            }
        });

        this.connection.on("recordingStopped", (data) => {
            if (window.phoneApp && window.phoneApp.recordingManager) {
                window.phoneApp.recordingManager.handleRecordingStopped(data);
            }
        });

        this.connection.on("recordingError", (data) => {
            if (window.phoneApp && window.phoneApp.recordingManager) {
                window.phoneApp.recordingManager.handleRecordingError(data);
            }
        });

        this.connection.on("recordingStatusUpdate", (data) => {
            console.log("录音状态更新:", data);
            // 简化处理，只记录日志
        });
    }

    handleIncomingCall(callData) {
        console.log("=== 来电数据详细分析 ===");
        console.log("完整callData:", JSON.stringify(callData, null, 2));
        console.log("callData.caller:", callData.caller);
        console.log("callData.caller.sipUsername:", callData.caller?.sipUsername);
        console.log("callData.caller.userId:", callData.caller?.userId);
        console.log("callData.callee:", callData.callee);
        console.log("callData.isExternal:", callData.isExternal);
        console.log("=== 来电数据分析结束 ===");
        
        // ===== 第一步：播放来电铃音 =====
        if (window.ringtoneManager) {
            console.log('触发来电铃音播放');
            window.ringtoneManager.play();
        } else {
            console.warn('铃音管理器未初始化');
        }
        // ================================
        
        try {
            if (!callData || !callData.caller || !callData.callee) {
                console.error('来电数据结构不完整:', callData);
                this.updateStatus('来电数据异常', 'danger');
                if (window.ErrorRecovery) {
                    window.ErrorRecovery.handleError(new Error('来电数据结构不完整'), 'handleIncomingCall', window.ErrorTypes.BUSINESS);
                }
                return;
            }
            
            this.callStateManager.setState(CallState.INCOMING);
            this.callStateManager.setCallContext({
                callId: callData.callId,
                caller: {
                    userId: callData.caller.userId,
                    sipUsername: callData.caller.sipUsername
                },
                callee: {
                    userId: callData.callee.userId,
                    sipUsername: callData.callee.sipUsername
                },
                isExternal: callData.isExternal || false,
                timestamp: callData.timestamp || new Date().toISOString()
            });
            
            let callerDisplay = '未知来电';
            let callerNumber = '';
            
            if (callData.caller.sipUsername) {
                callerDisplay = callData.caller.sipUsername;
                callerNumber = callData.caller.sipUsername;
            } else if (callData.caller.userId) {
                callerDisplay = `用户 ${callData.caller.userId}`;
                callerNumber = callData.caller.userId;
            }
            
            if (callData.isExternal) {
                callerNumber = `外部来电: ${callerNumber}`;
            }
            
            console.log('设置来电显示:', { callerDisplay, callerNumber });
            
            this.elements.callerName.innerHTML = callerDisplay;
            this.elements.callerNumber.innerHTML = callerNumber;
            
            const offerObj = this.parseOfferSdp(callData.offerSdp);
            this.elements.answerButton.setAttribute('data-offer', JSON.stringify(offerObj));
            console.log("Offer SDP processed:", offerObj);
            
            // 显示接听和挂断按钮
            this.elements.answerButton.classList.remove('d-none');
            this.elements.hangupButton.classList.remove('d-none');
            this.elements.callButton.classList.add('d-none');
            console.log('已显示接听和挂断按钮');
            
            this.showCallInfo(true);
            this.startCallTimer();
            this.updateStatus('来电中...', 'info');
            
        } catch (parseError) {
            console.error("处理来电时发生错误:", parseError);
            this.updateStatus('来电处理失败', 'danger');
            
            if (window.ErrorRecovery) {
                window.ErrorRecovery.handleError(parseError, 'handleIncomingCall', window.ErrorTypes.BUSINESS);
            } else {
                this.callStateManager.resetToIdle();
            }
        }
    }

    handleCallTrying(data) {
        console.log('收到 callTrying 事件:', data);
        this.updateStatus('正在呼叫...', 'info');
    }

    handleCallRinging(data) {
        console.log('收到 callRinging 事件:', data);
        
        // 回铃音由后端通过 SIP/RTP 发送，前端不再播放
        console.log('对方振铃中（回铃音由后端 SIP/RTP 发送）');
        
        this.updateStatus('对方振铃中...', 'info');
    }

    handleCallAnswered() {
        // 回铃音由后端控制，前端无需停止
        
        this.updateStatus('接听成功', 'success');
        this.callStateManager.setState(CallState.CONNECTED);
        this.stopCallTimer();
        this.startCallTimer();
        this.showCallInfo(true);
        this.checkSecureConnection();
    }

    handleAnswered() {
        // 回铃音由后端控制，前端无需停止
        
        this.updateStatus('通话已接听', 'success');
        this.callStateManager.setState(CallState.CONNECTED);
        this.showCallInfo(false);
        this.stopCallTimer();
        this.startCallTimer();
        this.showCallInfo(true);
        this.checkSecureConnection();
    }

    handleCallTimeout() {
        console.log('收到来电超时通知');
        
        // ===== 停止铃音 =====
        if (window.ringtoneManager) {
            console.log('来电超时，停止铃音');
            window.ringtoneManager.stop();
        }
        // ====================
        
        this.updateStatus('来电超时未接听', 'warning');
        this.callStateManager.resetToIdle();
        this.showCallInfo(false);
        this.stopCallTimer();
        
        // 3秒后恢复就绪状态
        setTimeout(() => {
            this.updateStatus('就绪', 'success');
        }, 3000);
    }

    handleSdpAnswered(answerDesc) {
        console.log("SDP Answer received from server:", answerDesc, "Type:", typeof answerDesc);
        if (this.webRTCManager.pc) {
            try {
                const sdpObj = this.parseSdpAnswer(answerDesc);
                console.log("Processing SDP answer:", sdpObj);
                
                this.webRTCManager.pc.setRemoteDescription(sdpObj).then(() => {
                    console.log("Remote description set successfully");
                }).catch(error => {
                    console.error("Error setting remote description:", error);
                });
            } catch (parseError) {
                console.error("Error parsing SDP answer:", parseError);
            }
        }
    }

    handleIceCandidate(candidate) {
        if (this.webRTCManager.pc && candidate) {
            console.log("Adding ICE candidate from server:", candidate);
            const candidateObj = typeof candidate === "string" ? JSON.parse(candidate) : candidate;
            
            this.webRTCManager.pc.addIceCandidate(candidateObj).then(() => {
                console.log("ICE candidate added successfully");
            }).catch(error => {
                console.error("Error adding ICE candidate:", error);
            });
        }
    }

    async startConnection() {
        try {
            await this.connection.start();
            console.log('SignalR连接已建立');
        } catch (error) {
            console.error('SignalR连接失败:', error);
            this.startManualReconnect();
        }
    }

    startHeartbeat() {
        // 如果使用全局连接，不需要启动独立的心跳（全局管理器已经处理）
        if (this.usingGlobalConnection) {
            console.log('使用全局SignalR连接，心跳由全局管理器处理');
            return;
        }
        
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
        }

        this.heartbeatInterval = setInterval(async () => {
            if (this.connection && 
                this.connection.state === signalR.HubConnectionState.Connected) {
                try {
                    await this.connection.invoke("Heartbeat");
                    console.log('独立连接心跳发送成功');
                } catch (error) {
                    console.warn('独立连接心跳发送失败:', error);
                }
            }
        }, this.heartbeatIntervalMs);

        console.log(`独立连接心跳定时器已启动，间隔: ${this.heartbeatIntervalMs}ms`);
    }

    stopHeartbeat() {
        // 如果使用全局连接，不需要停止心跳（由全局管理器处理）
        if (this.usingGlobalConnection) {
            console.log('使用全局SignalR连接，心跳由全局管理器处理');
            return;
        }
        
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
            console.log('独立连接心跳定时器已停止');
        }
    }

    async startManualReconnect() {
        let retryCount = 0;
        
        const attemptReconnect = async () => {
            if (this.connection.state === signalR.HubConnectionState.Connected) {
                console.log('SignalR连接已恢复');
                this.updateStatus('连接已恢复', 'success');
                setTimeout(() => this.updateStatus('就绪', 'success'), 3000);
                return;
            }
            
            try {
                retryCount++;
                console.log(`手动重连尝试 #${retryCount}`);
                this.updateStatus(`连接断开，重连中... (尝试 ${retryCount})`, 'warning');
                
                await this.connection.start();
                console.log('手动重连成功');
                this.updateStatus('连接已恢复', 'success');
            } catch (error) {
                console.error(`手动重连失败 #${retryCount}:`, error);
                
                const delay = Math.min(1000 * Math.pow(2, Math.min(retryCount - 1, 5)), 30000);
                console.log(`${delay}ms 后进行下次重连尝试`);
                
                setTimeout(attemptReconnect, delay);
            }
        };
        
        attemptReconnect();
    }

    // 辅助方法
    parseOfferSdp(offerSdp) {
        if (typeof offerSdp === 'string') {
            return JSON.parse(offerSdp);
        } else if (typeof offerSdp === 'object') {
            return offerSdp;
        } else {
            throw new Error("Invalid offer SDP type: " + typeof offerSdp);
        }
    }

    parseSdpAnswer(answerDesc) {
        if (typeof answerDesc === 'string') {
            return JSON.parse(answerDesc);
        } else if (typeof answerDesc === 'object') {
            return answerDesc;
        } else {
            throw new Error("Invalid SDP answer type: " + typeof answerDesc);
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

    async checkSecureConnection() {
        try {
            const callContext = this.callStateManager.getCallContext();
            
            // 使用适当的连接调用方法
            let isSecure;
            if (this.usingGlobalConnection && window.globalSignalRManager) {
                isSecure = await window.globalSignalRManager.invoke("GetSecureContextState", callContext.callId);
            } else {
                isSecure = await this.connection.invoke("GetSecureContextState", callContext.callId);
            }
            
            if (!isSecure) {
                this.updateStatus('当前连接不安全，通话质量可能受影响', 'warning');
            } else {
                this.updateStatus('安全链接通话中...', 'info');
            }
        } catch (error) {
            console.warn('检查安全状态失败:', error);
        }
    }

    // UI辅助方法
    updateStatus(text, type) {
        this.elements.statusDiv.textContent = text;
        this.elements.statusAlert.className = `alert mb-4 alert-${type}`;
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

    showCallInfo(show) {
        if (show) {
            this.elements.callInfo.classList.remove('d-none');
        } else {
            this.elements.callInfo.classList.add('d-none');
        }
    }

    startCallTimer() {
        window.callStartTime = new Date();
        this.elements.callTimer.textContent = "00:00";

        window.callTimerInterval = setInterval(() => {
            const now = new Date();
            const diff = new Date(now - window.callStartTime);
            const minutes = diff.getUTCMinutes().toString().padStart(2, '0');
            const seconds = diff.getUTCSeconds().toString().padStart(2, '0');
            this.elements.callTimer.textContent = `${minutes}:${seconds}`;
        }, 1000);
    }

    stopCallTimer() {
        if (window.callTimerInterval) {
            clearInterval(window.callTimerInterval);
            window.callTimerInterval = null;
        }
    }

    startRecordingTimer() {
        window.recordingStartTime = new Date();
        this.elements.recordingTimer.textContent = "00:00";
        this.elements.recordingIcon.className = "bi bi-record-circle text-danger me-2";

        window.recordingTimerInterval = setInterval(() => {
            const now = new Date();
            const diff = new Date(now - window.recordingStartTime);
            const minutes = diff.getUTCMinutes().toString().padStart(2, '0');
            const seconds = diff.getUTCSeconds().toString().padStart(2, '0');
            this.elements.recordingTimer.textContent = `${minutes}:${seconds}`;
        }, 1000);
    }

    stopRecordingTimer() {
        if (window.recordingTimerInterval) {
            clearInterval(window.recordingTimerInterval);
            window.recordingTimerInterval = null;
        }
        this.elements.recordingIcon.className = "bi bi-stop-circle text-secondary me-2";
    }

    getConnectionState() {
        return this.connection ? this.connection.state : 'Not initialized';
    }
}