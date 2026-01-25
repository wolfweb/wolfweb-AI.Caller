class MonitorWebRTCManager {
    constructor(signalRManager, remoteAudioElement) {
        this.signalRManager = signalRManager;
        this.remoteAudio = remoteAudioElement;
        this.pc = null;
        this.iceServers = [];
        this.callId = null;
        this.targetUserId = null;
        this.localStream = null;
        this.isInterventionActive = false; // 添加介入状态跟踪
    }

    async initialize() {
        // Fetch ICE servers
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

        // 1. Create PeerConnection
        this.pc = new RTCPeerConnection({ iceServers: this.iceServers });

        // 2. Setup Events
        this.pc.ontrack = (event) => {
            console.log("Remote track received", event.streams[0]);
            if (this.remoteAudio.srcObject !== event.streams[0]) {
                this.remoteAudio.srcObject = event.streams[0];
                this.remoteAudio.play().catch(e => console.error("Auto-play failed", e));
            }
        };

        this.pc.onicecandidate = (event) => {
            if (event.candidate) {
                this.signalRManager.invoke("SendMonitorIceCandidate",
                    targetUserId,
                    JSON.stringify(event.candidate)
                );
            }
        };

        this.pc.onconnectionstatechange = () => {
            console.log("Monitor Connection State:", this.pc.connectionState);
        };

        const transceiver = this.pc.addTransceiver('audio', { direction: 'sendrecv' });
        this.audioSender = transceiver.sender;

        const offer = await this.pc.createOffer();
        await this.pc.setLocalDescription(offer);

        const result = await this.signalRManager.invoke("ConnectMonitoringWebRtc",
            targetUserId,
            callId,
            JSON.stringify(offer)
        );

        if (result.success && result.answer) {
            await this.pc.setRemoteDescription({ type: 'answer', sdp: result.answer });
        } else {
            throw new Error(result.message || "Connection failed");
        }

        return result;
    }

    // Call this when SignalR receives an ICE candidate from server
    async addIceCandidate(candidate) {
        if (this.pc) {
            try {
                // If candidate is string, parse it.
                const candidateObj = typeof candidate === 'string' ? JSON.parse(candidate) : candidate;
                // Add
                await this.pc.addIceCandidate(candidateObj);
                console.log("Added remote ICE candidate");
            } catch (e) {
                // console.error("Error adding ICE candidate", e);
            }
        }
    }

    async startIntervention() {
        if (!this.pc) {
            throw new Error("WebRTC connection not established");
        }

        if (this.isInterventionActive) {
            console.log("Intervention already active");
            return;
        }

        try {
            console.log("Starting intervention audio...");
            this.isInterventionActive = true;
            
            this.localStream = await navigator.mediaDevices.getUserMedia({ 
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });

            const audioTrack = this.localStream.getAudioTracks()[0];
            const sender = this.pc.getSenders().find(s => s.track && s.track.kind === 'audio');

            if (sender) {
                await sender.replaceTrack(audioTrack);
                
                this.pc.getTransceivers().forEach(t => {
                    if (t.sender === sender) {
                        t.direction = 'sendrecv';
                    }
                });

                const offer = await this.pc.createOffer();
                await this.pc.setLocalDescription(offer);

                console.log("Sending WebRTC renegotiation request...");
                const result = await this.signalRManager.invoke("RenegotiateMonitoringWebRtc",
                    this.targetUserId,
                    this.callId,
                    JSON.stringify(offer)
                );

                if (result.success && result.answer) {
                    await this.pc.setRemoteDescription({ type: 'answer', sdp: result.answer });
                    console.log("WebRTC renegotiation completed successfully");
                } else {
                    throw new Error(result.message || "Renegotiation failed");
                }

            } else {
                this.pc.addTrack(audioTrack, this.localStream);
                console.log("Added new audio track to WebRTC connection");
            }

        } catch (e) {
            console.error("Failed to start intervention audio", e);
            this.isInterventionActive = false;
            if (this.localStream) {
                this.localStream.getTracks().forEach(t => t.stop());
                this.localStream = null;
            }
            throw e;
        }
    }

    stopIntervention() {
        if (this.localStream) {
            this.localStream.getTracks().forEach(t => t.stop());
            this.localStream = null;
            console.log("Stopped intervention audio stream");

            // Replace sender track with null
            if (this.pc) {
                const sender = this.pc.getSenders().find(s => s.track && s.track.kind === 'audio');
                if (sender) {
                    sender.replaceTrack(null);
                    console.log("Removed audio track from WebRTC connection");
                }
            }
        }

        this.isInterventionActive = false;
        console.log("Intervention stopped");
    }

    stop() {
        this.stopIntervention();
        if (this.pc) {
            this.pc.close();
            this.pc = null;
        }
        this.remoteAudio.srcObject = null;
    }
}
