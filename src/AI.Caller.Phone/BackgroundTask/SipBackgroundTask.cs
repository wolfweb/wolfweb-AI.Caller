
using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorceryMedia.Abstractions;
using System.Net;
using WebSocketSharp.Server;

namespace AI.Caller.Phone.BackgroundTask {
    public class SipBackgroundTask : IHostedService {
        private readonly ILogger _logger;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        public SipBackgroundTask(
            ILogger<SipBackgroundTask> logger, 
            IHubContext<WebRtcHub> hubContext,
            SIPTransportManager transportManager,
            ApplicationContext applicationContext,
            IServiceScopeFactory serviceScopeFactory
            ) {
            _logger = logger;
            _hubContext = hubContext;
            _applicationContext = applicationContext;
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
                _logger.LogInformation($"收到呼叫 {sipRequest} ");
                var toHeader = sipRequest.Header.To.ToURI;
                var toUser = toHeader.User;

                using var scope = _serviceScopeFactory.CreateScope();
                var _appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (_applicationContext.SipClients.TryGetValue(toUser, out var client)){
                    if (!client.IsCallActive) {
                        client.Accept(sipRequest);
                        var user = _appDbContext.Users.First(x => x.SipUsername == toUser);
                        var offerSdp = await client.CreateOfferAsync();
                        client.RTCPeerConnection!.onicecandidate += (candidate) => {
                            if (client.RTCPeerConnection.signalingState == RTCSignalingState.have_remote_offer || client.RTCPeerConnection.signalingState == RTCSignalingState.stable) {
                                try
                                {
                                    _hubContext.Clients.User(user.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                                }
                                catch (Exception e)
                                {
                                    _logger.LogError(e, e.Message);
                                }
                            }
                        };                        
                        await _hubContext.Clients.User(user.Id.ToString()).SendAsync("inCalling", new { caller = sipRequest.Header.From.FromURI.User, offerSdp = offerSdp.toJSON() });
                    }
                } else {
                    var item = _applicationContext.SipClients.First();
                    client = item.Value;
                    var user = _appDbContext.Users.First(x => x.SipUsername == item.Key);

                    if (!client.IsCallActive) {
                        client.Accept(sipRequest);

                        await client.AnswerAsync().ConfigureAwait(false);

                        await _hubContext.Clients.User(user.Id.ToString()).SendAsync("callAnswered");
                    }
                }
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, "处理来电时出错");
                return false;
            }
        }
    }
}
