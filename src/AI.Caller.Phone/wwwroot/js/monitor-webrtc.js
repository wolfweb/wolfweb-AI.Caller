class MonitorWebRTCManager {
    constructor(signalRManager, remoteAudioElement) {
        this.signalRManager = signalRManager;
        this.remoteAudio = remoteAudioElement;
        this.pc = null;
        this.iceServers = [];
        this.callId = null;
        this.targetUserId = null;
        this.localStream = null;
        this.isInterventionActive = false;
    }

    async initialize() {
        try {
            const response = await fetch('/api/WebRTC/ice-servers');
            if (response.ok) {
                const config = await response.json();
                if (config.iceServers) this.iceServers = config.iceServers;
            }
        } catch (e) {
            console.warn("Failed to fetch ICE servers, using default", e);
            this.iceServers = [{ urls: 'stun:stun.l.google.com:19302' }];
        }
    }

    async startMonitoring(targetUserId, callId) {
        this.targetUserId = targetUserId;
        this.callId = callId;

        this.pc = new RTCPeerConnection({ iceServers: this.iceServers });

        try {
            console.log("Requesting microphone access for standby...");
            this.localStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });

            this.localStream.getAudioTracks().forEach(track => {
                track.enabled = false; // 默认静音
                this.pc.addTrack(track, this.localStream);
            });

        } catch (e) {
            console.warn("Microphone access denied or failed. Intervention will not work.", e);
            this.pc.addTransceiver('audio', { direction: 'recvonly' });
        }

        this.pc.ontrack = (event) => {
            console.log("Remote track received", event.streams[0]);
            if (this.remoteAudio.srcObject !== event.streams[0]) {
                this.remoteAudio.srcObject = event.streams[0];
                this.remoteAudio.play().catch(e => console.error("Auto-play failed", e));
            }
        };

        this.pc.onicecandidate = (event) => {
            if (event.candidate) {
                this.signalRManager.invoke("SendMonitorIceCandidate", targetUserId, JSON.stringify(event.candidate));
            }
        };

        let offer = await this.pc.createOffer();
        await this.pc.setLocalDescription(offer);

        const result = await this.signalRManager.invoke("ConnectMonitoringWebRtc",
            targetUserId,
            callId,
            JSON.stringify(offer)
        );

        if (result.success && result.answer) {
            await this.pc.setRemoteDescription({ type: 'answer', sdp: result.answer });
        } else {
            this.stop();
            throw new Error(result.message || "Connection failed");
        }

        return result;
    }

    async addIceCandidate(candidate) {
        if (this.pc) {
            try {
                const candidateObj = typeof candidate === 'string' ? JSON.parse(candidate) : candidate;
                await this.pc.addIceCandidate(candidateObj);
            } catch (e) { }
        }
    }

    async startIntervention() {
        if (!this.pc) throw new Error("WebRTC connection not established");
        if (this.isInterventionActive) return;

        console.log("Unmuting microphone for intervention...");

        if (this.localStream) {
            this.localStream.getAudioTracks().forEach(track => {
                track.enabled = true; // 取消静音，声音立即发送
            });
            this.isInterventionActive = true;
            console.log("Intervention active (Mic Unmuted)");
        } else {
            throw new Error("Microphone stream not available");
        }
    }

    stopIntervention() {
        if (this.localStream) {
            this.localStream.getAudioTracks().forEach(track => {
                track.enabled = false; // 静音
            });
            console.log("Intervention stopped (Mic Muted)");
        }
        this.isInterventionActive = false;
    }

    stop() {
        this.stopIntervention();
        if (this.localStream) {
            this.localStream.getTracks().forEach(t => t.stop());
            this.localStream = null;
        }
        if (this.pc) {
            this.pc.close();
            this.pc = null;
        }
        this.remoteAudio.srcObject = null;
    }
}