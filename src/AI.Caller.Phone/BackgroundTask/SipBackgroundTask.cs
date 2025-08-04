using AI.Caller.Core;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.CallRouting.Handlers;
using Microsoft.AspNetCore.SignalR;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.BackgroundTask {
    public class SipBackgroundTask : IHostedService {
        private readonly ILogger _logger;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ICallTypeIdentifier _callTypeIdentifier;        
        public SipBackgroundTask(
            ILogger<SipBackgroundTask> logger, 
            IHubContext<WebRtcHub> hubContext,
            SIPTransportManager transportManager,
            ApplicationContext applicationContext,
            IServiceScopeFactory serviceScopeFactory,
            ICallTypeIdentifier callTypeIdentifier
            ) {
            _logger = logger;
            _hubContext = hubContext;
            _applicationContext = applicationContext;
            _sipTransportManager = transportManager;
            _serviceScopeFactory = serviceScopeFactory;
            _callTypeIdentifier = callTypeIdentifier;            
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
            _logger.LogInformation($"Listening on: {listeningEndPoints}");
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            foreach (var client in _applicationContext.SipClients.Values) {
                client.Shutdown();
            }
            _sipTransportManager.Shutdown();
            return Task.CompletedTask;
        }

        private async Task<bool> OnIncomingCall(SIPRequest sipRequest) {
            try {
                var callId = sipRequest.Header.CallId;
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var toUser = sipRequest.Header.To?.ToURI?.User;
                using var scope = _serviceScopeFactory.CreateScope();
                ICallRoutingService _callRoutingService = scope.ServiceProvider.GetRequiredService<ICallRoutingService>();
                OutboundCallHandler _outboundCallHandler = scope.ServiceProvider.GetRequiredService<OutboundCallHandler>();
                InboundCallHandler _inboundCallHandler = scope.ServiceProvider.GetRequiredService<InboundCallHandler>();

                _logger.LogInformation($"收到呼叫 - CallId: {callId}, From: {fromUser}, To: {toUser}, Method: {sipRequest.Method}");

                // 1. 识别来电类型
                var callType = _callTypeIdentifier.IdentifyCallType(sipRequest);
                _logger.LogDebug($"来电类型识别结果: {callType} - CallId: {callId}");

                // 2. 根据来电类型进行路由
                CallRoutingResult routingResult;
                ICallHandler callHandler;

                if (callType == "OutboundResponse")
                {
                    routingResult = await _callRoutingService.RouteOutboundResponseAsync(sipRequest);
                    callHandler = _outboundCallHandler;
                    _logger.LogInformation($"处理呼出应答 - CallId: {callId}, Success: {routingResult.Success}");
                }
                else
                {
                    routingResult = await _callRoutingService.RouteInboundCallAsync(sipRequest);
                    callHandler = _inboundCallHandler;
                    _logger.LogInformation($"处理新呼入 - CallId: {callId}, Success: {routingResult.Success}, Strategy: {routingResult.Strategy}");
                }

                if (!routingResult.Success)
                {
                    _logger.LogWarning($"路由失败 - CallId: {callId}, Message: {routingResult.Message}");
                    return false;
                }

                var handleResult = await callHandler.HandleCallAsync(sipRequest, routingResult);
                
                if (handleResult)
                {
                    _logger.LogInformation($"通话处理成功 - CallId: {callId}, Type: {callType}, Strategy: {routingResult.Strategy}");
                }
                else
                {
                    _logger.LogError($"通话处理失败 - CallId: {callId}, Type: {callType}");
                }

                return handleResult;
            } 
            catch (Exception ex) {
                _logger.LogError(ex, $"处理来电时发生错误 - CallId: {sipRequest.Header.CallId}");
                return false;
            }
        }
    }
}
