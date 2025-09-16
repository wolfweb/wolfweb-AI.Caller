﻿using AI.Caller.Phone.Hubs;
using Microsoft.AspNetCore.SignalR;
using Org.BouncyCastle.Asn1.Ocsp;
using SIPSorcery.Net;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.CallRouting.Handlers {
    public class InboundCallHandler : ICallHandler {
        private readonly ILogger<InboundCallHandler> _logger;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public InboundCallHandler(
            ILogger<InboundCallHandler> logger,
            IHubContext<WebRtcHub> hubContext,
            IServiceScopeFactory serviceScopeFactory) {
            _logger = logger;
            _hubContext = hubContext;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<bool> HandleCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult) {
            try {
                _logger.LogDebug($"开始处理新呼入 - CallId: {sipRequest.Header.CallId}, Strategy: {routingResult.Strategy}");

                if (!routingResult.Success || routingResult.TargetClient == null) {
                    _logger.LogError($"路由结果无效 - CallId: {sipRequest.Header.CallId}");
                    return false;
                }

                switch (routingResult.Strategy) {
                    case CallHandlingStrategy.WebToWeb:
                        return await HandleWebToWebCall(sipRequest, routingResult);
                    case CallHandlingStrategy.NonWebToWeb:
                        return await HandleNonWebToWebCall(sipRequest, routingResult);                    
                    case CallHandlingStrategy.Fallback:
                        return await HandleFallbackCall(sipRequest, routingResult);
                    default:
                        _logger.LogWarning($"未知的处理策略: {routingResult.Strategy}");
                        return false;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理新呼入时发生错误 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        private async Task<bool> HandleWebToWebCall(SIPRequest sipRequest, CallRoutingResult routingResult) {
            try {
                var sipClient = routingResult.TargetClient!;
                var targetUser = routingResult.TargetUser!;

                _logger.LogDebug($"处理Web到Web通话 - CallId: {sipRequest.Header.CallId}, User: {targetUser.SipAccount?.SipUsername}");

                sipClient.Accept(sipRequest);

                var offerSdp = await sipClient.CreateOfferAsync();

                sipClient.MediaSessionManager!.IceCandidateGenerated += (candidate) => {
                    if (sipClient.MediaSessionManager.PeerConnection?.signalingState == SIPSorcery.Net.RTCSignalingState.have_remote_offer ||
                        sipClient.MediaSessionManager.PeerConnection?.signalingState == SIPSorcery.Net.RTCSignalingState.stable) {
                        try {
                            _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                        } catch (Exception e) {
                            _logger.LogError(e, "发送ICE candidate失败");
                        }
                    }
                };

                await _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("inCalling", new {
                    caller = new{ 
                        userId = routingResult.CallerUser?.Id,
                        sipUsername = routingResult.CallerUser?.SipAccount?.SipUsername ?? routingResult.CallerNumber,
                    },
                    callee = new { 
                        userId = targetUser.Id,
                        sipUsername = targetUser.SipAccount?.SipUsername
                    },
                    offerSdp = offerSdp.toJSON(),
                    callId = sipRequest.Header.CallId,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogDebug($"Web到Web通话处理完成 - CallId: {sipRequest.Header.CallId}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理Web到Web通话失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        private async Task<bool> HandleNonWebToWebCall(SIPRequest sipRequest, CallRoutingResult routingResult) {
            try {
                var sipClient = routingResult.TargetClient!;
                var targetUser = routingResult.TargetUser!;

                _logger.LogDebug($"处理非Web到Web通话 - CallId: {sipRequest.Header.CallId}, User: {targetUser.SipAccount?.SipUsername}\n{sipRequest.Body}");

                sipClient.Accept(sipRequest);
                var sdp = SDP.ParseSDPDescription(sipRequest.Body);

                sipClient.MediaSessionManager!.IceCandidateGenerated += (candidate) => {
                    if (sipClient.MediaSessionManager.PeerConnection?.signalingState == SIPSorcery.Net.RTCSignalingState.have_remote_offer ||
                        sipClient.MediaSessionManager.PeerConnection?.signalingState == SIPSorcery.Net.RTCSignalingState.stable) {
                        try {
                            _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                        } catch (Exception e) {
                            _logger.LogError(e, "发送ICE candidate失败");
                        }
                    }
                };

                if (sipClient.Dialogue != null) {
                    _logger.LogDebug($"SIP Dialogue exists - CallId: {sipClient.Dialogue.CallId}");
                }

                await _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("inCalling", new {
                    caller = new {
                        userId = routingResult.CallerUser?.Id,
                        sipUsername = routingResult.CallerUser?.SipAccount?.SipUsername ?? routingResult.CallerNumber,
                    },
                    callee = new {
                        userId = targetUser.Id,
                        sipUsername = targetUser.SipAccount?.SipUsername
                    },
                    offerSdp = new RTCSessionDescriptionInit {
                        type = RTCSdpType.offer,
                        sdp = sipRequest.Body
                    },
                    callId = sipRequest.Header.CallId,
                    isExternal = true,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogDebug($"非Web到Web通话处理完成 - CallId: {sipRequest.Header.CallId}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理非Web到Web通话失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        private async Task<bool> HandleFallbackCall(SIPRequest sipRequest, CallRoutingResult routingResult) {
            try {
                _logger.LogDebug($"处理备用情况 - CallId: {sipRequest.Header.CallId}, Message: {routingResult.Message}");



                _logger.LogDebug($"备用处理：拒绝呼叫 - CallId: {sipRequest.Header.CallId}");
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理备用情况失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }
    }
}
