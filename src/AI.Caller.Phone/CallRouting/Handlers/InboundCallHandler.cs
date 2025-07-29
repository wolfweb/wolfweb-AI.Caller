using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.CallRouting.Handlers
{
    /// <summary>
    /// 新呼入处理器
    /// </summary>
    public class InboundCallHandler : ICallHandler
    {
        private readonly ILogger<InboundCallHandler> _logger;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public InboundCallHandler(
            ILogger<InboundCallHandler> logger,
            IHubContext<WebRtcHub> hubContext,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _hubContext = hubContext;
            _serviceScopeFactory = serviceScopeFactory;
        }

        /// <summary>
        /// 处理新呼入
        /// </summary>
        public async Task<bool> HandleCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult)
        {
            try
            {
                _logger.LogInformation($"开始处理新呼入 - CallId: {sipRequest.Header.CallId}, Strategy: {routingResult.Strategy}");

                if (!routingResult.Success || routingResult.TargetClient == null)
                {
                    _logger.LogError($"路由结果无效 - CallId: {sipRequest.Header.CallId}");
                    return false;
                }

                // 根据处理策略选择不同的处理方式
                switch (routingResult.Strategy)
                {
                    case CallHandlingStrategy.WebToWeb:
                        return await HandleWebToWebCall(sipRequest, routingResult);
                    case CallHandlingStrategy.NonWebToWeb:
                        return await HandleNonWebToWebCall(sipRequest, routingResult);
                    case CallHandlingStrategy.WebToNonWeb:
                    case CallHandlingStrategy.NonWebToNonWeb:
                        return await HandleNonWebCall(sipRequest, routingResult);
                    case CallHandlingStrategy.Fallback:
                        return await HandleFallbackCall(sipRequest, routingResult);
                    default:
                        _logger.LogWarning($"未知的处理策略: {routingResult.Strategy}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理新呼入时发生错误 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        /// <summary>
        /// 处理Web到Web通话
        /// </summary>
        private async Task<bool> HandleWebToWebCall(SIPRequest sipRequest, CallRoutingResult routingResult)
        {
            try
            {
                var sipClient = routingResult.TargetClient!;
                var targetUser = routingResult.TargetUser!;

                _logger.LogInformation($"处理Web到Web通话 - CallId: {sipRequest.Header.CallId}, User: {targetUser.SipUsername}");

                // 1. 接受呼叫
                sipClient.Accept(sipRequest);

                // 2. 创建WebRTC offer
                var offerSdp = await sipClient.CreateOfferAsync();

                // 3. 设置ICE candidate处理
                sipClient.RTCPeerConnection!.onicecandidate += (candidate) => {
                    if (sipClient.RTCPeerConnection.signalingState == SIPSorcery.Net.RTCSignalingState.have_remote_offer || 
                        sipClient.RTCPeerConnection.signalingState == SIPSorcery.Net.RTCSignalingState.stable) {
                        try
                        {
                            _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "发送ICE candidate失败");
                        }
                    }
                };

                // 4. 通知Web客户端有来电
                await _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("inCalling", new { 
                    caller = sipRequest.Header.From.FromURI.User, 
                    offerSdp = offerSdp.toJSON(),
                    callId = sipRequest.Header.CallId,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation($"Web到Web通话处理完成 - CallId: {sipRequest.Header.CallId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理Web到Web通话失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        /// <summary>
        /// 处理非Web到Web通话
        /// </summary>
        private async Task<bool> HandleNonWebToWebCall(SIPRequest sipRequest, CallRoutingResult routingResult)
        {
            try
            {
                var sipClient = routingResult.TargetClient!;
                var targetUser = routingResult.TargetUser!;

                _logger.LogInformation($"处理非Web到Web通话 - CallId: {sipRequest.Header.CallId}, User: {targetUser.SipUsername}");

                // 1. 接受呼叫
                sipClient.Accept(sipRequest);

                // 2. 创建WebRTC offer
                var offerSdp = await sipClient.CreateOfferAsync();

                // 3. 设置ICE candidate处理
                sipClient.RTCPeerConnection!.onicecandidate += (candidate) => {
                    if (sipClient.RTCPeerConnection.signalingState == SIPSorcery.Net.RTCSignalingState.have_remote_offer || 
                        sipClient.RTCPeerConnection.signalingState == SIPSorcery.Net.RTCSignalingState.stable) {
                        try
                        {
                            _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "发送ICE candidate失败");
                        }
                    }
                };

                // 4. 通知Web客户端有来电（标记为外部来电）
                await _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("inCalling", new { 
                    caller = sipRequest.Header.From.FromURI.User, 
                    offerSdp = offerSdp.toJSON(),
                    callId = sipRequest.Header.CallId,
                    isExternal = true,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation($"非Web到Web通话处理完成 - CallId: {sipRequest.Header.CallId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理非Web到Web通话失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        /// <summary>
        /// 处理非Web通话（传统SIP通话）
        /// </summary>
        private async Task<bool> HandleNonWebCall(SIPRequest sipRequest, CallRoutingResult routingResult)
        {
            try
            {
                var sipClient = routingResult.TargetClient!;
                var targetUser = routingResult.TargetUser;

                _logger.LogInformation($"处理非Web通话 - CallId: {sipRequest.Header.CallId}");

                // 1. 接受呼叫
                sipClient.Accept(sipRequest);

                // 2. 直接应答（不需要WebRTC）
                var answerResult = await sipClient.AnswerAsync();
                if (!answerResult)
                {
                    _logger.LogError($"应答非Web通话失败 - CallId: {sipRequest.Header.CallId}");
                    return false;
                }

                // 3. 如果有对应用户，通知Web客户端通话已建立
                if (targetUser != null)
                {
                    await _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("callAnswered", new {
                        callId = sipRequest.Header.CallId,
                        caller = sipRequest.Header.From.FromURI.User,
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation($"非Web通话处理完成 - CallId: {sipRequest.Header.CallId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理非Web通话失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }

        /// <summary>
        /// 处理备用情况
        /// </summary>
        private async Task<bool> HandleFallbackCall(SIPRequest sipRequest, CallRoutingResult routingResult)
        {
            try
            {
                _logger.LogInformation($"处理备用情况 - CallId: {sipRequest.Header.CallId}, Message: {routingResult.Message}");

                // 这里可以实现各种备用处理逻辑：
                // 1. 语音信箱
                // 2. 呼叫转移
                // 3. 自动拒绝
                // 4. 播放忙音

                // 目前简单地拒绝呼叫
                // 在实际实现中，这里应该根据配置选择不同的备用策略
                
                _logger.LogInformation($"备用处理：拒绝呼叫 - CallId: {sipRequest.Header.CallId}");
                return false; // 返回false表示拒绝呼叫
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理备用情况失败 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }
    }
}