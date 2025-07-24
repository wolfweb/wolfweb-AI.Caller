using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Net;

namespace AI.Caller.Phone.Services
{
    public class WebRTCHostedService : IHostedService {
        private readonly ILogger<WebRTCHostedService> _logger;

        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();

        public WebRTCHostedService(ILogger<WebRTCHostedService> logger) {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("WebRTCHostedService StartAsync.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }

        public async Task<RTCSessionDescriptionInit> GetOffer(string id) {
            if (string.IsNullOrWhiteSpace(id)) {
                throw new ArgumentNullException("id", "A unique ID parameter must be supplied when creating a new peer connection.");
            } else if (_peerConnections.ContainsKey(id)) {
                throw new ArgumentNullException("id", "The specified peer connection ID is already in use.");
            }
            var peerConnection = new RTCPeerConnection(null);

            MediaStreamTrack audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, 
                false,
                [
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                ]
            );
            peerConnection.addTrack(audioTrack);

            peerConnection.OnRtpPacketReceived += PeerConnection_OnRtpPacketReceived;
            peerConnection.OnReceiveReport += PeerConnection_OnReceiveReport; ;
            peerConnection.OnSendReport += PeerConnection_OnSendReport; ;

            peerConnection.OnTimeout += (mediaType) => _logger.LogWarning($"Timeout for {mediaType}.");            
            peerConnection.onconnectionstatechange += (state) => {
                _logger.LogDebug($"Peer connection {id} state changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed) {
                    _peerConnections.TryRemove(id, out _);
                } else if (state == RTCPeerConnectionState.connected) {
                    _logger.LogDebug("Peer connection connected.");
                }
            };

            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            _peerConnections.TryAdd(id, peerConnection);

            return offerSdp;
        }

        private void PeerConnection_OnSendReport(SDPMediaTypesEnum arg1, RTCPCompoundPacket arg2) {
            
        }

        private void PeerConnection_OnReceiveReport(IPEndPoint arg1, SDPMediaTypesEnum arg2, RTCPCompoundPacket arg3) {
            
        }

        private void PeerConnection_OnRtpPacketReceived(IPEndPoint arg1, SDPMediaTypesEnum arg2, RTPPacket arg3) {
            
        }

        public void SetRemoteDescription(string id, RTCSessionDescriptionInit description) {
            if (!_peerConnections.TryGetValue(id, out var pc)) {
                throw new ApplicationException("No peer connection is available for the specified id.");
            } else {
                _logger.LogDebug("Answer SDP: " + description.sdp);
                pc.setRemoteDescription(description);
            }
        }

        public void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate) {
            if (!_peerConnections.TryGetValue(id, out var pc)) {
                throw new ApplicationException("No peer connection is available for the specified id.");
            } else {
                _logger.LogDebug("ICE Candidate: " + iceCandidate.candidate);
                pc.addIceCandidate(iceCandidate);
            }
        }
    }
}
