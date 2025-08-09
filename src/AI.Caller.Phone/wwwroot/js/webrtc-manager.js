/**
 * WebRTC连接管理器
 * 负责WebRTC连接的建立和媒体流处理
 */
class WebRTCManager {
    constructor(elements) {
        this.elements = elements;
        this.pc = null;
        this.localStream = null;
        this.iceServers = [{ urls: 'stun:stun.l.google.com:19302' }];
        this.iceTransportPolicy = 'all';
        this.preferredAudioDevices = null;
        
        // 添加SDP协商状态管理
        this.sdpNegotiationComplete = false;
        this.pendingIceCandidates = [];
    }

    async initialize() {
        // 检查WebRTC支持
        if (!this.checkWebRTCSupport()) {
            throw new Error('WebRTC not supported');
        }

        // 获取ICE服务器配置
        await this.fetchIceServers();

        // 检查网络连接
        if (!this.checkNetworkConnection()) {
            throw new Error('Network connection not available');
        }

        // 设置音频设备
        await this.setupAudioDevices();

        // 设置全局错误处理
        this.setupGlobalErrorHandling();

        // 获取用户媒体流
        await this.getUserMedia();

        console.log('WebRTC管理器初始化完成');
    }

    checkWebRTCSupport() {
        if (!window.RTCPeerConnection) {
            this.updateStatus('您的浏览器不支持WebRTC，请使用Chrome、Firefox、Edge或Safari的最新版本', 'danger');
            this.elements.callButton.disabled = true;
            return false;
        }
        
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            this.updateStatus('您的浏览器可能不完全支持音频功能，通话质量可能受影响', 'warning');
        }
        
        return true;
    }

    checkNetworkConnection() {
        if (!navigator.onLine) {
            this.updateStatus('网络连接不可用，请检查您的网络设置', 'danger');
            return false;
        }
        return true;
    }

    async fetchIceServers() {
        try {
            const response = await fetch('/api/WebRTC/ice-servers');
            if (response.ok) {
                const config = await response.json();
                console.log('Fetched ICE server configuration:', config);
                
                if (config.iceServers && config.iceServers.length > 0) {
                    this.iceServers = config.iceServers;
                    console.log(`Using ${this.iceServers.length} ICE servers from server configuration`);
                }
                
                if (config.iceTransportPolicy) {
                    this.iceTransportPolicy = config.iceTransportPolicy;
                    console.log(`Using ICE transport policy: ${this.iceTransportPolicy}`);
                }
                
                return true;
            } else {
                console.error('Failed to fetch ICE server configuration:', response.status, response.statusText);
                return false;
            }
        } catch (error) {
            console.error('Error fetching ICE server configuration:', error);
            return false;
        }
    }

    async setupAudioDevices() {
        try {
            const hasPermission = await this.checkMicrophonePermission();
            if (!hasPermission) {
                this.updateStatus('未获得麦克风权限，通话功能可能受限', 'warning');
            } else {
                const deviceInfo = await this.selectOptimalAudioDevices();
                if (deviceInfo) {
                    this.preferredAudioDevices = deviceInfo;
                }
            }
        } catch (error) {
            this.updateStatus('无法检查麦克风权限，通话功能可能受限', 'warning');
        }
    }

    async checkMicrophonePermission() {
        try {
            console.log('检查麦克风权限');
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            stream.getTracks().forEach(track => track.stop());
            console.log('已获得麦克风权限');
            return true;
        } catch (error) {
            console.error('获取麦克风权限失败:', error);
            this.updateStatus('无法访问麦克风，请在浏览器设置中允许访问', 'danger');
            alert('通话需要麦克风权限。请在浏览器设置中允许访问麦克风，然后重试。');
            return false;
        }
    }

    async selectOptimalAudioDevices() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) {
            return false;
        }
        
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            
            const audioInputDevices = devices.filter(device => device.kind === 'audioinput');
            console.log(`找到 ${audioInputDevices.length} 个麦克风设备`);
            
            const audioOutputDevices = devices.filter(device => device.kind === 'audiooutput');
            console.log(`找到 ${audioOutputDevices.length} 个扬声器设备`);
            
            const preferredMicrophoneId = this.selectPreferredDevice(audioInputDevices, '麦克风');
            const preferredSpeakerId = this.selectPreferredDevice(audioOutputDevices, '扬声器');
            
            // 设置音频输出设备
            if (preferredSpeakerId && typeof this.elements.remoteAudio.setSinkId === 'function') {
                try {
                    await this.elements.remoteAudio.setSinkId(preferredSpeakerId);
                    console.log('已设置音频输出设备');
                } catch (sinkIdError) {
                    console.warn('设置音频输出设备失败:', sinkIdError);
                }
            }
            
            return {
                preferredMicrophoneId,
                preferredSpeakerId,
                audioInputDevices,
                audioOutputDevices
            };
        } catch (error) {
            console.error('枚举音频设备失败:', error);
            return false;
        }
    }

    selectPreferredDevice(devices, deviceType) {
        if (devices.length === 0) return '';
        
        if (devices.length === 1) {
            console.log(`使用唯一可用${deviceType}:`, devices[0].label || '未命名设备');
            return devices[0].deviceId;
        }
        
        // 优先选择非默认设备
        const nonDefaultDevice = devices.find(device => 
            !device.deviceId.includes('default') && 
            !device.label.toLowerCase().includes('default'));
        
        if (nonDefaultDevice) {
            console.log(`选择非默认${deviceType}:`, nonDefaultDevice.label);
            return nonDefaultDevice.deviceId;
        }
        
        return devices[0].deviceId;
    }

    setupGlobalErrorHandling() {
        window.addEventListener('error', (event) => {
            console.error('未捕获的错误:', event.error || event.message);
            event.preventDefault();
            return true;
        });
        
        window.addEventListener('unhandledrejection', (event) => {
            console.error('未处理的Promise拒绝:', event.reason);
            event.preventDefault();
            return true;
        });
        
        if (navigator.mediaDevices && navigator.mediaDevices.addEventListener) {
            navigator.mediaDevices.addEventListener('devicechange', () => this.handleDeviceChange());
            console.log('设备变化监听已设置');
        }
        
        console.log('全局错误处理已设置');
    }

    async handleDeviceChange() {
        console.log('检测到媒体设备变化');
        
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            const audioInputDevices = devices.filter(device => device.kind === 'audioinput');
            const audioOutputDevices = devices.filter(device => device.kind === 'audiooutput');
            
            console.log('可用音频输入设备:', audioInputDevices.length);
            console.log('可用音频输出设备:', audioOutputDevices.length);
        } catch (error) {
            console.error('获取媒体设备信息时出错:', error);
        }
    }

    async getUserMedia() {
        try {
            this.localStream = await navigator.mediaDevices.getUserMedia({ 
                video: false, 
                audio: true 
            });
            console.log('已获取用户媒体流');
        } catch (error) {
            console.error('获取用户媒体流失败:', error);
            throw error;
        }
    }

    async createPeerConnection(isCaller, remoteSdp) {
        const rtcConfig = {
            iceServers: this.iceServers,
            iceTransportPolicy: this.iceTransportPolicy
        };
        
        console.log('Creating RTCPeerConnection with config:', rtcConfig);
        this.pc = new RTCPeerConnection(rtcConfig);
        
        // 重置状态
        this.sdpNegotiationComplete = false;
        this.pendingIceCandidates = [];
        
        // 添加本地媒体流
        this.localStream.getTracks().forEach(track => {
            this.pc.addTrack(track, this.localStream);
        });

        // 设置事件处理器
        this.setupPeerConnectionEvents();

        if (isCaller) {
            const offer = await this.pc.createOffer();
            await this.pc.setLocalDescription(offer);
            
            // 标记SDP协商完成并发送缓存的ICE候选者
            this.sdpNegotiationComplete = true;
            await this.sendPendingIceCandidates();
            
            return this.pc.localDescription;
        } else {
            await this.pc.setRemoteDescription(remoteSdp);
            const answer = await this.pc.createAnswer();
            await this.pc.setLocalDescription(answer);
            
            // 标记SDP协商完成并发送缓存的ICE候选者
            this.sdpNegotiationComplete = true;
            await this.sendPendingIceCandidates();
            
            return this.pc.localDescription;
        }
    }

    setupPeerConnectionEvents() {
        // 远程媒体流处理
        this.pc.ontrack = evt => {
            this.elements.remoteAudio.srcObject = evt.streams[0];
        };

        // ICE候选者处理 - 修复时序问题
        this.pc.onicecandidate = async evt => {
            if (evt.candidate) {
                console.log("新ICE候选者:", evt.candidate);
                if (this.sdpNegotiationComplete) {
                    // SDP协商完成后立即发送
                    await this.sendIceCandidate(evt.candidate);
                } else {
                    // SDP协商未完成，暂存候选者
                    console.log("SDP协商未完成，暂存ICE候选者");
                    this.pendingIceCandidates.push(evt.candidate);
                }
            }
        };

        // 连接状态监听
        this.pc.onicegatheringstatechange = () => {
            console.log("ICE gathering state: " + this.pc.iceGatheringState);
        };
        
        this.pc.oniceconnectionstatechange = () => {
            console.log("ICE connection state: " + this.pc.iceConnectionState);
            if (this.pc.iceConnectionState === 'disconnected' || this.pc.iceConnectionState === 'failed') {
                console.warn('WebRTC connection disrupted, triggering reconnect');
                this.handleWebRTCReconnect();
            } else if (this.pc.iceConnectionState === 'connected') {
                console.log('WebRTC connection established');
                this.updateStatus('WebRTC连接已建立', 'success');
            }
        };
        
        this.pc.onsignalingstatechange = () => {
            console.log("Signaling state: " + this.pc.signalingState);
        };
        
        this.pc.onconnectionstatechange = () => {
            console.log("Connection state: " + this.pc.connectionState);
            if (this.pc.connectionState === 'failed') {
                console.warn('WebRTC connection failed, triggering reconnect');
                this.handleWebRTCReconnect();
            }
        };
    }

    async handleWebRTCReconnect() {
        if (!window.phoneApp || !window.phoneApp.signalRManager || window.phoneApp.signalRManager.connection.state !== 'Connected') {
            console.warn('SignalR not connected, waiting for reconnect before WebRTC recovery');
            return;
        }
    
        try {
            this.updateStatus('WebRTC连接断开，正在重新连接...', 'warning');
            this.closePeerConnection(); 
        
            await window.phoneApp.signalRManager.connection.invoke("ReconnectWebRTCAsync");
    
            this.updateStatus('WebRTC重新连接中，等待服务器响应...', 'info');
        } catch (error) {
            console.error('WebRTC reconnect failed:', error);
            this.updateStatus('WebRTC重新连接失败: ' + error.message, 'danger');
            window.phoneApp.handleCallError(error);
        }
    }

    async sendPendingIceCandidates() {
        console.log(`发送 ${this.pendingIceCandidates.length} 个缓存的ICE候选者`);
        for (const candidate of this.pendingIceCandidates) {
            await this.sendIceCandidate(candidate);
        }
        this.pendingIceCandidates = [];
    }

    async sendIceCandidate(candidate) {
        try {
            await window.phoneApp.signalRManager.connection.invoke("SendIceCandidateAsync", candidate);
            console.log("ICE候选者发送成功:", candidate.candidate);
        } catch (error) {
            console.error("发送ICE候选者失败:", error);
        }
    }

    closePeerConnection() {
        if (this.pc) {
            console.log('关闭WebRTC连接');
            this.pc.close();
            this.pc = null;
        }
        
        // 重置状态
        this.sdpNegotiationComplete = false;
        this.pendingIceCandidates = [];
    }

    stopLocalStream() {
        if (this.localStream) {
            console.log('停止本地媒体流');
            this.localStream.getTracks().forEach(track => {
                track.stop();
            });
            this.localStream = null;
        }
    }

    cleanup() {
        this.closePeerConnection();
        this.stopLocalStream();
        
        // 清理远程音频
        if (this.elements.remoteAudio.srcObject) {
            const tracks = this.elements.remoteAudio.srcObject.getTracks();
            tracks.forEach(track => track.stop());
            this.elements.remoteAudio.srcObject = null;
        }
        
        console.log('WebRTC资源已清理');
    }

    updateStatus(text, type) {
        this.elements.statusDiv.textContent = text;
        this.elements.statusAlert.className = `alert mb-4 alert-${type}`;
    }

    getConnectionState() {
        return this.pc ? this.pc.connectionState : 'Not connected';
    }
}