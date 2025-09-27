using AI.Caller.Core;
using AI.Caller.Core.Media;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.SignalR;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.BackgroundTask {
    public class SipBackgroundTask : IHostedService {
        private readonly ILogger _logger;
        private readonly ICallManager _callManager;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        public SipBackgroundTask(
            ILogger<SipBackgroundTask> logger,
            IHubContext<WebRtcHub> hubContext,
            ICallManager callManager,
            ITTSEngine ttsEngine,
            SIPTransportManager transportManager,
            IServiceScopeFactory serviceScopeFactory
            ) {
            _logger = logger;
            _callManager = callManager;
            _sipTransportManager = transportManager;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            await _sipTransportManager.InitialiseSIP();

            _sipTransportManager.IncomingCall += OnIncomingCall;

            string? listeningEndPoints = null;
            foreach (var sipChannel in _sipTransportManager.SIPTransport!.GetSIPChannels()) {
                SIPEndPoint sipChannelEP = sipChannel.ListeningSIPEndPoint.CopyOf();
                sipChannelEP.ChannelID = null;
                listeningEndPoints += (listeningEndPoints == null) ? sipChannelEP.ToString() : $", {sipChannelEP}";
            }
            _logger.LogDebug($"Listening on: {listeningEndPoints}");
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _sipTransportManager.Shutdown();
            return Task.CompletedTask;
        }

        private async Task<bool> OnIncomingCall(SIPRequest sipRequest) {
            try {
                var callId   = sipRequest.Header.CallId;
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var toUser   = sipRequest.Header.To?.ToURI?.User;
                if(string.IsNullOrEmpty(toUser)) {
                    _logger.LogWarning($"收到无效呼叫 - CallId: {callId}, From: {fromUser}, To: {toUser}, Method: {sipRequest.Method}");
                    return false;
                }

                _logger.LogDebug($"收到呼叫 - CallId: {callId}, From: {fromUser}, To: {toUser}, Method: {sipRequest.Method}");

                using var scope = _serviceScopeFactory.CreateScope();

                ICallRoutingService _callRoutingService = scope.ServiceProvider.GetRequiredService<ICallRoutingService>();
                CallRoutingResult routingResult = await _callRoutingService.RouteInboundCallAsync(toUser, sipRequest);
                _logger.LogDebug($"处理新呼入 - CallId: {callId}, Success: {routingResult.Success}, Strategy: {routingResult.Strategy}");

                if (!routingResult.Success) {
                    _logger.LogWarning($"路由失败 - CallId: {callId}, Message: {routingResult.Message}");
                    return false;
                }

                var handleResult = await _callManager.IncomingCallAsync(sipRequest, routingResult);
                if (handleResult) {
                    _logger.LogDebug($"通话处理成功 - CallId: {callId}, Strategy: {routingResult.Strategy}");
                } else {
                    _logger.LogError($"通话处理失败 - CallId: {callId}");
                }

                return handleResult;
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理来电时发生错误 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }
    }
}
