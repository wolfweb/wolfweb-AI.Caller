using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.Services {
    public interface ICallManager {
        void AddIceCandidate(string callId, int userId, RTCIceCandidateInit candidate);
        IEnumerable<User> GetActiviteUsers();
        bool GetSecureContextState(string callId, int userId);

        Task AnswerAsync(string callId, RTCSessionDescriptionInit? answer);
        Task HangupCallAsync(string callId, int hangupUser);
        Task<bool> IncomingCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult);
        Task SendDtmfAsync(byte tone, int sendUser, string callId);
        Task<CallContext> MakeCallAsync(string destination, User caller, RTCSessionDescriptionInit? offer, CallScenario scenario);
    }

    public class CallManager : ICallManager, IDisposable {
        private readonly ILogger _logger;
        private readonly Timer _monitoringTimer;
        private readonly HashSet<CallContext> _contexts;
        private readonly RecordingManager _recordingManager;
        private readonly HangupRetryPolicy _hangupRetryPolicy;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly AICustomerServiceSettings _aiSettings;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public CallManager(
            ILogger<ICallManager> logger,
            RecordingManager recordingManager,
            IHubContext<WebRtcHub> hubContext,
            IServiceScopeFactory serviceScopeFactory,
            IOptions<AICustomerServiceSettings> aiSettings
            ) {
            _logger              = logger;
            _contexts            = new();
            _aiSettings          = aiSettings.Value;
            _hubContext          = hubContext;
            _recordingManager    = recordingManager;
            _hangupRetryPolicy   = new();
            _serviceScopeFactory = serviceScopeFactory;

            _monitoringTimer = new Timer(OnCleanupContext, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void AddIceCandidate(string callId, int userId, RTCIceCandidateInit candidate) {
            var ctx = _contexts.FirstOrDefault(x => x.CallId == callId) ?? throw new Exception($"无效的呼叫标识:{callId}");
            if (ctx.Caller != null && ctx.Caller.User != null && ctx.Caller.User.Id == userId) {
                if (ctx.Caller.Client == null) throw new Exception($"呼叫上下文呼叫未初始化:{callId}");
                ctx.Caller.Client.Client.AddIceCandidate(candidate);
            }

            if (ctx.Callee != null && ctx.Callee.User != null && ctx.Callee.User.Id == userId) {
                if (ctx.Callee.Client == null) throw new Exception($"呼叫上下文呼叫未初始化:{callId}");
                ctx.Callee.Client.Client.AddIceCandidate(candidate);
            }
        }

        public IEnumerable<User> GetActiviteUsers() {
            foreach (var it in _contexts) {
                if (it.Caller != null && it.Caller.User != null && it.Caller.Client!=null && it.Caller.Client.Client.IsCallActive) yield return it.Caller.User;
                if (it.Callee != null && it.Callee.User != null && it.Callee.Client != null && it.Callee.Client.Client.IsCallActive) yield return it.Callee.User;
            }
        }

        public async Task AnswerAsync(string callId, RTCSessionDescriptionInit? answer) {
            var ctx = _contexts.FirstOrDefault(x => x.CallId == callId) ?? throw new Exception($"无效的呼叫标识:{callId}");

            if (ctx.Callee == null) throw new Exception($"呼叫上下文被叫不能是空:{callId}");
            if (ctx.Callee.Client == null) throw new Exception($"呼叫上下文被叫未初始化:{callId}");
            if (answer != null) {
                ctx.Callee.Client.Client.SetRemoteDescription(answer);

                var timeout = TimeSpan.FromSeconds(10);
                var start = DateTime.UtcNow;
                while (!ctx.Callee.Client.Client.IsSecureContextReady()) {
                    if (DateTime.UtcNow - start > timeout) {
                        _logger.LogError($"*** SECURE CONTEXT TIMEOUT *** User: {ctx.Callee.User!.Username}, waited {timeout.TotalSeconds}s");
                        return ;
                    }
                    await Task.Delay(100);
                }
            }
            await ctx.Callee.Client.Client.AnswerAsync();

            await _hubContext.Clients.User(ctx.Caller!.User!.Id.ToString()).SendAsync("answered");
        }

        public bool GetSecureContextState(string callId, int userId) {
            var ctx = _contexts.FirstOrDefault(x => x.CallId == callId) ?? throw new Exception($"无效的呼叫标识:{callId}");
            if (ctx.Caller != null && ctx.Caller.User != null && ctx.Caller.User.Id == userId) {
                if (ctx.Caller.Client == null) throw new Exception($"呼叫上下文呼叫未初始化:{callId}");
                return ctx.Caller.Client.Client.IsSecureContextReady();
            }

            if (ctx.Callee != null && ctx.Callee.User != null && ctx.Callee.User.Id == userId) {
                if (ctx.Callee.Client == null) throw new Exception($"呼叫上下文呼叫未初始化:{callId}");
                return ctx.Callee.Client.Client.IsSecureContextReady();
            }

            return false;
        }

        public async Task HangupCallAsync(string callId, int hangupUser) {
            var ctx = _contexts.FirstOrDefault(x => x.CallId == callId) ?? throw new Exception($"无效的呼叫标识:{callId}");

            if (ctx.Callee != null && ctx.Callee.Client != null) {
                if (ctx.Callee.User!.Id == hangupUser) {
                    ctx.Callee.Client.Client.Hangup();
                    await NotifyHangupStatusAsync("已挂断", ctx.Callee.User!.Id);
                } else {
                    ctx.Callee.Client.Client.Cancel();
                    await NotifyHangupStatusAsync("对方已挂断", ctx.Callee.User!.Id);
                }
                ctx.Callee.Client.Client.Shutdown();
            }

            if (ctx.Caller != null && ctx.Caller.Client != null) {
                if (ctx.Caller.User!.Id == hangupUser) {
                    ctx.Caller.Client.Client.Hangup();
                    await NotifyHangupStatusAsync("已挂断", ctx.Caller.User!.Id);
                } else {
                    ctx.Caller.Client.Client.Cancel();
                    await NotifyHangupStatusAsync("对方已挂断", ctx.Caller.User!.Id);
                }
                ctx.Caller.Client.Client.Shutdown();
            }

            OnHangupCall(ctx);
        }

        public async Task<bool> IncomingCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult) {
            using var scope = _serviceScopeFactory.CreateScope();
            ICallScenario? callScenario = null;
            var id = sipRequest.Header.From.FromURI.Parameters.Get("id");

            CallContext? ctx = null;
            if (!string.IsNullOrEmpty(id)) {
                ctx = _contexts.FirstOrDefault(x => x.CallId == id) ?? throw new Exception($"无效的呼入id: {id}");
            } else {
                ctx = new CallContext {
                    Caller = new Models.Caller {
                        User   = routingResult.CallerUser,
                        Number = routingResult.CallerNumber
                    }
                };
            }

            if (_aiSettings.Enabled) {
                if (routingResult.Strategy == CallHandlingStrategy.WebToWeb) {
                    callScenario = scope.ServiceProvider.GetRequiredService<WebToServerScenario>();
                } else if (routingResult.Strategy == CallHandlingStrategy.NonWebToWeb) {
                    callScenario = scope.ServiceProvider.GetRequiredService<MobileToServerScenario>();
                }
            } else {
                if (routingResult.Strategy == CallHandlingStrategy.WebToWeb) {
                    callScenario = scope.ServiceProvider.GetRequiredService<WebToWebScenario>();
                } else if (routingResult.Strategy == CallHandlingStrategy.NonWebToWeb) {
                    callScenario = scope.ServiceProvider.GetRequiredService<MobileToWebScenario>();
                }
            }

            if(callScenario == null) {
                _logger.LogWarning($"新呼入{sipRequest.Header.From.FromURI.User} : {sipRequest.Header.To.ToURI.User} 路由策略: {routingResult.Strategy} 未找到可以处理通话的场景");
                return false;
            }

            
            return await callScenario.HandleInboundCallAsync(sipRequest, routingResult, ctx);
        }

        public async Task<CallContext> MakeCallAsync(string destination, User caller, RTCSessionDescriptionInit? offer, CallScenario scenario) {
            using var scope = _serviceScopeFactory.CreateScope();
            ICallScenario callScenario = scenario switch {
                CallScenario.WebToWeb => scope.ServiceProvider.GetRequiredService<WebToWebScenario>(),
                CallScenario.WebToMobile => scope.ServiceProvider.GetRequiredService<WebToMobileScenario>(),
                CallScenario.ServerToWeb => scope.ServiceProvider.GetRequiredService<ServerToWebScenario>(),
                CallScenario.WebToServer => scope.ServiceProvider.GetRequiredService<WebToServerScenario>(),
                CallScenario.ServerToMobile => scope.ServiceProvider.GetRequiredService<ServerToMobileScenario>(),
            };

            var ctx = new CallContext() {
                Type   = scenario,
                Caller = new Models.Caller {
                    User = caller
                }
            };
            _contexts.Add(ctx);

            var result = await callScenario.HandleOutboundCallAsync(destination, caller, offer, ctx);
            if (!result) throw new Exception($"呼叫失败");

            OnMakeCalled(ctx);

            return ctx;
        }

        public async Task SendDtmfAsync(byte tone, int sendUser, string callId) {
            var ctx = _contexts.FirstOrDefault(x => x.CallId == callId) ?? throw new Exception($"无效的呼叫标识:{callId}");
            if (ctx.Caller != null && ctx.Caller.Client != null && ctx.Caller.User !=null && ctx.Caller.User.Id == sendUser) {
                await ctx.Caller.Client!.Client.SendDTMFAsync(tone);
            } else if (ctx.Callee != null && ctx.Callee.Client != null && ctx.Callee.User != null && ctx.Callee.User.Id == sendUser) {
                await ctx.Callee.Client!.Client.SendDTMFAsync(tone);
            }
        }

        private void OnHangupCall(CallContext ctx) {
            if (ctx.Caller != null && ctx.Caller.User != null && ctx.Caller.Client != null) {
                _recordingManager.OnSipHanguped(ctx.Caller.User.Id, ctx.Caller.Client.Client);
                ctx.Caller.Client.Dispose();
            }

            if (ctx.Callee != null && ctx.Callee.User != null && ctx.Callee.Client != null) {
                _recordingManager.OnSipHanguped(ctx.Callee.User.Id, ctx.Callee.Client.Client);
                ctx.Callee.Client.Dispose();
            }

            _contexts.Remove(ctx);
        }

        private void OnMakeCalled(CallContext ctx) {
            if(ctx.Caller !=null && ctx.Caller.User != null && ctx.Caller.Client != null) {
                _recordingManager.OnSipCalled(ctx.Caller.User.Id, ctx.Caller.Client.Client);
            }

            if (ctx.Callee != null && ctx.Callee.User != null && ctx.Callee.Client != null) {
                _recordingManager.OnSipCalled(ctx.Callee.User.Id, ctx.Callee.Client.Client);
            }
        }

        private void OnCleanupContext(object? state) {
            foreach(var ctx in _contexts) {
                if(ctx.Caller!=null && ctx.Callee!=null && ctx.Caller.Client!=null && ctx.Callee.Client !=null && !ctx.Caller.Client.Client.IsCallActive && !ctx.Callee.Client.Client.IsCallActive && ctx.Duration> TimeSpan.FromMinutes(1)) {
                    _contexts.Remove(ctx);
                }
            }
        }

        protected async Task NotifyHangupStatusAsync(string message, int userId, string status = "callEnded") {
            using var notificationCts = new CancellationTokenSource(_hangupRetryPolicy.NotificationTimeout);

            try {
                var notificationTask = _hubContext.Clients.User(userId.ToString())
                    .SendAsync(status, new {
                        message = message,
                        timestamp = DateTime.UtcNow
                    }, notificationCts.Token);

                var completedTask = await Task.WhenAny(
                    notificationTask,
                    Task.Delay(_hangupRetryPolicy.NotificationTimeout, notificationCts.Token)
                );

                if (completedTask == notificationTask) {
                    await notificationTask;
                    _logger.LogDebug($"已向用户 {userId} 发送状态通知: {status} - {message}");
                } else {
                    _logger.LogWarning($"向用户 {userId} 发送状态通知超时: {status} - {message}");
                }
            } catch (OperationCanceledException) when (notificationCts.Token.IsCancellationRequested) {
                _logger.LogWarning($"向用户 {userId} 发送状态通知被取消或超时: {status}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"发送挂断状态通知失败 - 用户: {userId}, 状态: {status}");
            }
        }

        public void Dispose() {
            _monitoringTimer.Dispose();
        }
    }

    public interface ICallScenario {
        Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext); 
        Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext);
    }

    public abstract class CallScenarioBase : ICallScenario {
        private readonly ILogger _logger;
        protected CallScenarioBase(ILogger logger) {
            _logger = logger;
        }
        public virtual Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) { 
            return Task.FromResult(true);
        }
        public virtual Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            return Task.FromResult(true);
        }
        
        protected async Task<bool> CheckSecureContextReady(User user, SIPClient client) {
            var timeout = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (!client.IsSecureContextReady()) {
                if (DateTime.UtcNow - start > timeout) {
                    _logger.LogError($"*** SECURE CONTEXT TIMEOUT *** User: {user.Username}, waited {timeout.TotalSeconds}s");
                    return false;
                }
                await Task.Delay(100);
            }
            return true;
        }

        protected string GenerateFromTag(User user, CallContext ctx) {
            return ctx.CallId;
        }
    }

    public class WebToWebScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public WebToWebScenario(
            ILogger<WebToWebScenario> logger,             
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager
        ) : base(logger) {
            _logger      = logger;
            _hubContext  = hubContext;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            if (routingResult.TargetUser == null) throw new Exception($"被叫坐席用户不能为空");
            if (routingResult.TargetUser.SipAccount == null) throw new Exception($"被叫用户不是有效坐席：{routingResult.TargetUser.Id}");
            var handle = await _poolManager.AcquireClientAsync(routingResult.TargetUser.SipAccount.SipServer, true);
            if (handle == null) return false;

            handle.Client.Accept(sipRequest);

            callContext.Callee = new Callee {
                User = routingResult.TargetUser,
                Client = handle
            };

            var offer = await handle.Client.CreateOfferAsync();
            handle.Client.MediaSessionManager!.IceCandidateGenerated += (candidate) => {
                if (handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.have_remote_offer ||
                    handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.stable) {
                    try {
                        _hubContext.Clients.User(routingResult.TargetUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                    } catch (Exception e) {
                        _logger.LogError(e, "发送ICE candidate失败");
                    }
                }
            };

            await _hubContext.Clients.User(routingResult.TargetUser.Id.ToString()).SendAsync("inCalling", new {
                caller = new {
                    userId = routingResult.CallerUser?.Id,
                    sipUsername = routingResult.CallerUser?.SipAccount?.SipUsername ?? routingResult.CallerNumber,
                },
                callee = new {
                    userId = routingResult.TargetUser.Id,
                    sipUsername = routingResult.TargetUser.SipAccount?.SipUsername
                },
                offerSdp = offer.toJSON(),
                callId = callContext.CallId,
                timestamp = DateTime.UtcNow
            });
            return true;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (sdpOffer == null) throw new ArgumentNullException(nameof(sdpOffer));
            if(callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, true);
            if (handle == null) return false;

            callContext.Type = CallScenario.WebToWeb;
            callContext.Caller!.Client = handle;

            var answer = await handle.Client.OfferAsync(sdpOffer) ?? throw new Exception("无法配置RTPPeerContext");
            await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("sdpAnswered", answer.toJSON());

            handle.Client.MediaSessionManager!.IceCandidateGenerated += async (candidate) => {
                if (candidate != null) {
                    try {
                        await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                    } catch (Exception e) {
                        _logger.LogError(e, e.Message);
                    }
                }
            };

            await CheckSecureContextReady(callerUser, handle.Client);

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class WebToMobileScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public WebToMobileScenario(
            ILogger<WebToMobileScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager) : base(logger) {
            _logger      = logger;
            _hubContext  = hubContext;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (sdpOffer == null) throw new ArgumentNullException(nameof(sdpOffer));
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, true);
            if (handle == null) return false;

            callContext.Type = CallScenario.WebToMobile; 
            callContext.Caller!.Client = handle;

            var answer = await handle.Client.OfferAsync(sdpOffer) ?? throw new Exception("无法配置RTPPeerContext");
            await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("sdpAnswered", answer.toJSON());

            handle.Client.MediaSessionManager!.IceCandidateGenerated += async (candidate) => {
                if (candidate != null) {
                    try {
                        await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                    } catch (Exception e) {
                        _logger.LogError(e, e.Message);
                    }
                }
            };

            await CheckSecureContextReady(callerUser, handle.Client);

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class MobileToWebScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public MobileToWebScenario(
            ILogger<MobileToWebScenario> logger,
            IServiceScopeFactory serviceScopeFactory,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager
        ) : base(logger) {
            _logger              = logger;
            _hubContext          = hubContext;
            _poolManager         = poolManager;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            var user = routingResult.TargetUser!;

            var handle = await _poolManager.AcquireClientAsync(user.SipAccount!.SipServer, true);
            if (handle == null) return false;

            callContext.Type = CallScenario.MobileToWeb;

            callContext.Caller = new Models.Caller { 
                Number = routingResult.CallerNumber 
            };
            callContext.Callee = new Models.Callee { 
                User = user,
                Client = handle,
            };

            handle.Client.Accept(sipRequest);

            var sdp = SDP.ParseSDPDescription(sipRequest.Body);
            await handle.Client.MediaSessionManager!.SetSipRemoteDescriptionAsync(new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = sipRequest.Body
            });

            var offer = await handle.Client.CreateOfferAsync();
            handle.Client.MediaSessionManager!.IceCandidateGenerated += (candidate) => {
                if (handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.have_remote_offer ||
                    handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.stable) {
                    try {
                        _hubContext.Clients.User(user.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                    } catch (Exception e) {
                        _logger.LogError(e, "发送ICE candidate失败");
                    }
                }
            };

            await _hubContext.Clients.User(user.Id.ToString()).SendAsync("inCalling", new {
                caller = new {
                    userId = routingResult.CallerUser?.Id,
                    sipUsername = routingResult.CallerUser?.SipAccount?.SipUsername ?? routingResult.CallerNumber,
                },
                callee = new {
                    userId = user.Id,
                    sipUsername = user.SipAccount?.SipUsername
                },
                offerSdp = offer.toJSON(),
                callId = sipRequest.Header.CallId,
                isExternal = true,
                timestamp = DateTime.UtcNow
            });

            return true;
        }
    }

    public class ServerToWebScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public ServerToWebScenario(
            ILogger<ServerToWebScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager
            ) : base(logger) {
            _logger      = logger;
            _hubContext  = hubContext;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            if (routingResult.TargetUser == null) throw new Exception($"被叫坐席用户不能为空");
            if (routingResult.TargetUser.SipAccount == null) throw new Exception($"被叫用户不是有效坐席：{routingResult.TargetUser.Id}");
            var handle = await _poolManager.AcquireClientAsync(routingResult.TargetUser.SipAccount.SipServer, true);
            if (handle == null) return false;

            handle.Client.Accept(sipRequest);

            callContext.Callee = new Callee {
                User = routingResult.TargetUser,
                Client = handle
            };

            var offer = await handle.Client.CreateOfferAsync();
            handle.Client.MediaSessionManager!.IceCandidateGenerated += (candidate) => {
                if (handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.have_remote_offer ||
                    handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.stable) {
                    try {
                        _hubContext.Clients.User(routingResult.TargetUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                    } catch (Exception e) {
                        _logger.LogError(e, "发送ICE candidate失败");
                    }
                }
            };

            await _hubContext.Clients.User(routingResult.TargetUser.Id.ToString()).SendAsync("inCalling", new {
                caller = new {
                    userId = routingResult.CallerUser?.Id,
                    sipUsername = routingResult.CallerUser?.SipAccount?.SipUsername ?? routingResult.CallerNumber,
                },
                callee = new {
                    userId = routingResult.TargetUser.Id,
                    sipUsername = routingResult.TargetUser.SipAccount?.SipUsername
                },
                offerSdp = offer.toJSON(),
                callId = sipRequest.Header.CallId,
                timestamp = DateTime.UtcNow
            });
            return true;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");
            if (callContext.Callee == null || callContext.Callee.User == null) throw new Exception("被叫用户不能为空");

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, false);
            if (handle == null) return false;

            callContext.Type = CallScenario.ServerToWeb;
            callContext.Caller!.Client = handle;

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class WebToServerScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly AICustomerServiceManager _aiManager;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public WebToServerScenario(
            ILogger<ServerToWebScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager,
            AICustomerServiceManager aiManager
            ) : base(logger) {
            _logger = logger;
            _aiManager  = aiManager;
            _hubContext = hubContext;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            if (routingResult.TargetUser == null) throw new Exception($"被叫坐席用户不能为空");
            if (routingResult.TargetUser.SipAccount == null) throw new Exception($"被叫用户不是有效坐席：{routingResult.TargetUser.Id}");
            var handle = await _poolManager.AcquireClientAsync(routingResult.TargetUser.SipAccount.SipServer, false);
            if (handle == null) return false;

            handle.Client.Accept(sipRequest);

            callContext.Callee = new Callee {
                User = routingResult.TargetUser,
                Client = handle
            };

            await handle.Client.AnswerAsync();

            try {
                _ = Task.Run(() => {
                    _ = Task.Run(async () => {
                        await Task.Delay(3000);                        
                        var success = await _aiManager.StartAICustomerServiceAsync(
                            callContext.Callee.User,
                            callContext.Callee.Client.Client,
                            "您好，欢迎致电我们公司，我是AI客服小助手。请问有什么可以帮助您的吗？您好，欢迎致电我们公司，我是AI客服小助手。请问有什么可以帮助您的吗？"
                        );
                        var session = _aiManager.GetActiveSession(callContext.Callee.User.Id);                        

                        if (success) {
                            _logger.LogInformation($"AI TTS started for WebToServer call: {callContext.CallId}");
                        } else {
                            _logger.LogWarning($"Failed to start AI TTS for call: {callContext.CallId}");
                        }
                    });
                });

                await Task.Delay(30 * 1000);
                handle.Client.Hangup();
                await _aiManager.StopAICustomerServiceAsync(callContext.Callee.User.Id);
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error starting AI TTS for call: {callContext.CallId}");
            }

            return true;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");
            if (callContext.Callee == null || callContext.Callee.User == null) throw new Exception("被叫用户不能为空");
            if (sdpOffer == null) throw new ArgumentNullException(nameof(sdpOffer));

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, true);
            if (handle == null) return false;

            callContext.Type = CallScenario.WebToServer;
            callContext.Caller!.Client = handle;

            var answer = await handle.Client.OfferAsync(sdpOffer) ?? throw new Exception("无法配置RTPPeerContext");
            await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("sdpAnswered", answer.toJSON());

            handle.Client.MediaSessionManager!.IceCandidateGenerated += (candidate) => {
                if (handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.have_remote_offer ||
                    handle.Client.MediaSessionManager.PeerConnection?.signalingState == RTCSignalingState.stable) {
                    try {
                        _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                    } catch (Exception e) {
                        _logger.LogError(e, "发送ICE candidate失败");
                    }
                }
            };

            await CheckSecureContextReady(callerUser, handle.Client);

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class ServerToMobileScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public ServerToMobileScenario(
            ILogger<ServerToMobileScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager
            ) : base(logger) {
            _logger = logger;
            _hubContext = hubContext;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");
            if (callContext.Callee == null || callContext.Callee.User == null) throw new Exception("被叫用户不能为空");

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, false);
            if (handle == null) return false;

            callContext.Type = CallScenario.ServerToMobile;
            callContext.Caller!.Client = handle;

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class MobileToServerScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;

        public MobileToServerScenario(
            ILogger<MobileToServerScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager
            ) : base(logger) {
            _logger = logger;
            _hubContext = hubContext;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            var user = routingResult.TargetUser!;

            var handle = await _poolManager.AcquireClientAsync(user.SipAccount!.SipServer, false);
            if (handle == null) return false;

            callContext.Type = CallScenario.MobileToServer;

            callContext.Caller = new Models.Caller {
                Number = routingResult.CallerNumber
            };
            callContext.Callee = new Models.Callee {
                User = user,
                Client = handle,
            };

            handle.Client.Accept(sipRequest);

            var sdp = SDP.ParseSDPDescription(sipRequest.Body);
            await handle.Client.MediaSessionManager!.SetSipRemoteDescriptionAsync(new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = sipRequest.Body
            });

            await handle.Client.AnswerAsync();

            return true;
        }
    }
}