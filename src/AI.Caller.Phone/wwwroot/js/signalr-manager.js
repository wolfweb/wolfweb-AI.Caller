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
    }

    async initialize() {
        this.createConnection();
        this.setupConnectionEvents();
        this.setupCallEvents();
        this.setupRecordingEvents();
        
        await this.startConnection();
    }

    createConnection() {
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
            this.startManualReconnect();
        });
    }

    setupCallEvents() {
        // 来电事件
        this.connection.on("inCalling", (callData) => {
            this.handleIncomingCall(callData);
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

    handleCallAnswered() {
        this.updateStatus('接听成功', 'success');
        this.callStateManager.setState(CallState.CONNECTED);
        this.stopCallTimer();
        this.startCallTimer();
        this.showCallInfo(true);
        this.checkSecureConnection();
    }

    handleAnswered() {
        this.updateStatus('通话已接听', 'success');
        this.callStateManager.setState(CallState.CONNECTED);
        this.showCallInfo(false);
        this.stopCallTimer();
        this.startCallTimer();
        this.showCallInfo(true);
        this.checkSecureConnection();
    }

    handleCallTimeout() {
        this.updateStatus('拨打超时', 'warning');
        this.callStateManager.resetToIdle();
        this.showCallInfo(false);
        this.stopCallTimer();
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
            const isSecure = await this.connection.invoke("GetSecureContextState");
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