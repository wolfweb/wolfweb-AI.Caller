using Microsoft.AspNetCore.SignalR;
using SIPSorcery.SIP;
using AI.Caller.Phone.Hubs;

namespace AI.Caller.Phone.CallRouting.Handlers {
    public class OutboundCallHandler : ICallHandler {
        private readonly ILogger<OutboundCallHandler> _logger;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public OutboundCallHandler(
            ILogger<OutboundCallHandler> logger,
            IHubContext<WebRtcHub> hubContext) {
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<bool> HandleCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult) {
            try {
                _logger.LogDebug($"开始处理呼出应答 - CallId: {sipRequest.Header.CallId}");

                if (!routingResult.Success || routingResult.TargetClient == null) {
                    _logger.LogError($"路由结果无效 - CallId: {sipRequest.Header.CallId}");
                    return false;
                }

                var sipClient = routingResult.TargetClient;
                var targetUser = routingResult.TargetUser;
                var outboundCallInfo = routingResult.OutboundCallInfo;

                sipClient.Accept(sipRequest);
                _logger.LogDebug($"已接受呼出应答 - CallId: {sipRequest.Header.CallId}");

                if (sipClient.MediaSessionManager?.PeerConnection == null) throw new Exception("webrtc 未初始化");

                var answerResult = await sipClient.AnswerAsync();
                if (!answerResult) {
                    _logger.LogError($"应答呼出通话失败 - CallId: {sipRequest.Header.CallId}");
                    return false;
                }

                _logger.LogDebug($"呼出应答处理成功 - CallId: {sipRequest.Header.CallId}");

                if (targetUser != null) {
                    await NotifyWebClient(targetUser.Id.ToString(), sipRequest, outboundCallInfo);
                }

                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理呼出应答时发生错误 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        private async Task NotifyWebClient(string userId, SIPRequest sipRequest, OutboundCallInfo? outboundCallInfo) {
            try {
                var notificationData = new {
                    callId = sipRequest.Header.CallId,
                    fromUser = sipRequest.Header.From?.FromURI?.User,
                    toUser = sipRequest.Header.To?.ToURI?.User,
                    method = sipRequest.Method.ToString(),
                    timestamp = DateTime.UtcNow,
                    outboundInfo = outboundCallInfo != null ? new {
                        destination = outboundCallInfo.Destination,
                        sipUsername = outboundCallInfo.SipUsername,
                        status = outboundCallInfo.Status.ToString(),
                        createdAt = outboundCallInfo.CreatedAt
                    } : null
                };

                // 根据SIP方法发送不同的通知
                switch (sipRequest.Method) {
                    case SIPMethodsEnum.INVITE:
                        await _hubContext.Clients.User(userId).SendAsync("outboundCallConnected", notificationData);
                        break;
                    case SIPMethodsEnum.ACK:
                        await _hubContext.Clients.User(userId).SendAsync("outboundCallEstablished", notificationData);
                        break;
                    case SIPMethodsEnum.BYE:
                        await _hubContext.Clients.User(userId).SendAsync("outboundCallEnded", notificationData);
                        break;
                    default:
                        await _hubContext.Clients.User(userId).SendAsync("outboundCallUpdate", notificationData);
                        break;
                }

                _logger.LogDebug($"已通知Web客户端 - UserId: {userId}, Method: {sipRequest.Method}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"通知Web客户端失败 - UserId: {userId}");
            }
        }
    }
}