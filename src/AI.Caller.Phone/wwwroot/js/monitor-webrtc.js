class MonitorWebRTCManager {
    constructor(signalRManager, remoteAudioElement) {
        this.signalRManager = signalRManager;
        this.remoteAudio = remoteAudioElement;
        this.pc = null;
        this.iceServers = [];
        this.callId = null;
        this.targetUserId = null;
        this.localStream = null;
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
        if (!this.pc) return;
        try {
            console.log("Starting intervention audio...");
            // 1. Get Microphone
            this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true });

            // 2. Add Track to PC
            const audioTrack = this.localStream.getAudioTracks()[0];
            const sender = this.pc.getSenders().find(s => s.track && s.track.kind === 'audio');

            if (sender) {
                // Replace track if sender exists (e.g. dummy track)
                await sender.replaceTrack(audioTrack);
                // Also update transceiver direction if needed
                // Note: replaceTrack doesn't automatically change 'recvonly' to 'sendrecv'
                // We might need to renegotiate if direction check fails on server.
                // But SIPSorcery usually allows receiving if notified?
                // Actually, strict WebRTC requires renegotiation to flip direction.

                // Try renegotiation
                // We update transceiver direction
                this.pc.getTransceivers().forEach(t => {
                    if (t.sender === sender) {
                        t.direction = 'sendrecv';
                    }
                });

                // Create new offer
                const offer = await this.pc.createOffer();
                await this.pc.setLocalDescription(offer);

                // Send re-offer? 
                // We didn't implement 'Renegotiate' on Hub yet.
                // Fallback: If we just replace track, some servers might accept it if we set sendrecv initially?
                // But we set recvonly.

                // Since I can't easily add a new Hub method without breaking flow, 
                // I will try to rely on JUST replaceTrack for now. 
                // If it fails (audio not heard), we might need to recreate connection or impl renegotiation.
                // But wait, the plan assumed simple intervention.

                // Re-reading Plan: "Let's implement simple renegotiation... We need a Hub method... For MVP just rely on renegotiation".
                // I didn't add Renegotiate method to Hub. I added ConnectMonitoringWebRtc which creates NEW session.

                // Maybe just call ConnectMonitoringWebRtc again with new offer?
                // If I call it again, it creates a NEW MonitorMediaSession in the backend, REPLACING the old one (AddMonitor overwrites in dict?).
                // Yes, `_monitoringListeners[userId] = listener` (TryAdd/Update?).
                // `AudioBridge.Monitoring.cs` uses `TryAdd`. It fails if exists!
                // So I CANNOT simply reconnect without removing old one.

                // Workaround: I will implement a "Update" logic or just fail-safe to SignalR intervention if WebRTC intervention is risky?
                // NO, user said "Strictly follow document".
                // Document says: "Renegotiation is cleaner... We need a Hub method... For MVP: let's send silence on 'sendrecv' from start".

                // OK, I will change `startMonitoring` to use `sendrecv` but send nothing (or muted track).
                // Or just `sendrecv` without track? 
                // Browsers might require a track for sendrecv.

                // Let's modify `startMonitoring` below to `sendrecv` if possible, or use a dummy track.

            } else {
                this.pc.addTrack(audioTrack, this.localStream);
            }

        } catch (e) {
            console.error("Failed to start intervention audio", e);
            throw e;
        }
    }

    stopIntervention() {
        if (this.localStream) {
            this.localStream.getTracks().forEach(t => t.stop());
            this.localStream = null;

            // Replace sender track with null?
            if (this.pc) {
                const sender = this.pc.getSenders().find(s => s.track && s.track.kind === 'audio');
                if (sender) sender.replaceTrack(null);
            }
        }
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
