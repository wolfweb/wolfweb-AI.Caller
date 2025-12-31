using AI.Caller.Core;
using AI.Caller.Core.Media;
using AI.Caller.Core.Services;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Services {
    public interface ICallManager {
        void AddIceCandidate(string callId, int userId, RTCIceCandidateInit candidate);
        IEnumerable<User> GetActiviteUsers();
        bool GetSecureContextState(string callId, int userId);

        Task AnswerAsync(string callId, RTCSessionDescriptionInit? answer);
        Task HangupCallAsync(string callId, int hangupUser);
        Task<bool> IncomingCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult);
        Task SendDtmfAsync(byte tone, int sendUser, string callId);
        Task<CallContext> MakeCallAsync(string destination, User caller, RTCSessionDescriptionInit? offer, CallScenario scenario, int? preferredLineId = null, bool autoSelectLine = true);

        Task<string> StartDtmfCollectionAsync(string callId, AI.Caller.Core.Services.DtmfCollectionConfig? config = null, CancellationToken ct = default);
        Task StopDtmfCollectionAsync(string callId);
        Task ResetDtmfCollectionAsync(string callId);
        Task<string> GetCurrentDtmfInputAsync(string callId);
        Task<bool> IsDtmfCollectingAsync(string callId);

        Task<AudioCodec> GetCurrentCodecAsync(string callId);
        /// <summary>
        /// 切换编码
        /// </summary>
        /// <param name="callId"></param>
        /// <param name="targetCodec"></param>
        /// <param name="reason"></param>
        /// <returns></returns>
        Task<bool> SwitchCodecAsync(string callId, AudioCodec targetCodec, string reason);
    }

    public partial class CallManager : ICallManager, IDisposable {
        private readonly ILogger _logger;
        private readonly Timer _monitoringTimer;
        private readonly IDtmfService _dtmfService;
        private readonly ConcurrentDictionary<string, CallContext> _contexts;
        private readonly RecordingManager _recordingManager;
        private readonly HangupRetryPolicy _hangupRetryPolicy;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly AICustomerServiceManager _aiManager;
        private readonly SemaphoreSlim _semaphoreSlim = new (1, 1);

        public CallManager(
            ILogger<ICallManager> logger,
            IDtmfService dtmfService,
            RecordingManager recordingManager,
            IHubContext<WebRtcHub> hubContext,
            IServiceScopeFactory serviceScopeFactory,
            AICustomerServiceManager aiManager
            ) {
            _logger              = logger;
            _contexts            = new();
            _hubContext          = hubContext;
            _recordingManager    = recordingManager;
            _hangupRetryPolicy   = new();
            _serviceScopeFactory = serviceScopeFactory;
            _aiManager           = aiManager;
            _dtmfService         = dtmfService;

            _monitoringTimer = new Timer(OnCleanupContext, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5));
        }

        public void AddIceCandidate(string callId, int userId, RTCIceCandidateInit candidate) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                throw new Exception($"无效的呼叫标识:{callId}");
            }
            if (ctx.Caller != null && ctx.Caller.User != null && ctx.Caller.User.Id == userId) {
                if (ctx.Caller.Client == null) throw new Exception($"呼叫上下文呼叫未初始化:{callId}");
                ctx.Caller.Client.Client.AddIceCandidate(candidate);
            }

            if (ctx.Callee != null && ctx.Callee.User != null && ctx.Callee.User.Id == userId) {
                if (ctx.Callee.Client == null) throw new Exception($"呼叫上下文被叫未初始化:{callId}");
                ctx.Callee.Client.Client.AddIceCandidate(candidate);
            }
        }

        public IEnumerable<User> GetActiviteUsers() {
            foreach (var it in _contexts.Values) {
                if (it.Caller != null && it.Caller.User != null && it.Caller.Client != null && it.Caller.Client.Client.IsCallActive) yield return it.Caller.User;
                if (it.Callee != null && it.Callee.User != null && it.Callee.Client != null && it.Callee.Client.Client.IsCallActive) yield return it.Callee.User;
            }
        }

        public async Task AnswerAsync(string callId, RTCSessionDescriptionInit? answer) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                throw new Exception($"无效的呼叫标识:{callId}");
            }

            CleanRingback(ctx);

            if (ctx.Callee == null) throw new Exception($"呼叫上下文被叫不能是空:{callId}");
            if (ctx.Callee.Client == null) throw new Exception($"呼叫上下文被叫未初始化:{callId}");
            if (answer != null) {
                ctx.Callee.Client.Client.SetRemoteDescription(answer);

                var timeout = TimeSpan.FromSeconds(10);
                var start = DateTime.UtcNow;
                while (!ctx.Callee.Client.Client.IsSecureContextReady()) {
                    if (DateTime.UtcNow - start > timeout) {
                        _logger.LogError($"*** SECURE CONTEXT TIMEOUT *** User: {ctx.Callee.User!.Username}, waited {timeout.TotalSeconds}s");
                        return;
                    }
                    await Task.Delay(100);
                }
            }
            await ctx.Callee.Client.Client.AnswerAsync();

            if (ctx.Caller != null && ctx.Caller.User != null) {
                await _hubContext.Clients.User(ctx.Caller.User.Id.ToString()).SendAsync("answered");
            }
        }

        public bool GetSecureContextState(string callId, int userId) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                throw new Exception($"无效的呼叫标识:{callId}");
            }
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
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                _logger.LogWarning("HangupCallAsync failed, call context with ID {CallId} not found.", callId);
                return;
            }

            SIPClient? targetClient = null;

            if (ctx.Callee != null && ctx.Callee.Client != null && ctx.Callee.User!.Id == hangupUser) {
                targetClient = ctx.Callee.Client.Client;
            } else if (ctx.Caller != null && ctx.Caller.Client != null && ctx.Caller.User!.Id == hangupUser) {
                targetClient = ctx.Caller.Client.Client;
            }

            if (targetClient != null) {
                _logger.LogInformation("Initiating active hangup for CallId: {CallId}, User: {UserId}", callId, hangupUser);
                targetClient.Hangup();
                await NotifyHangupStatusAsync("已挂断", hangupUser);
            } else {
                _logger.LogWarning("Could not find matching client for user {UserId} in call {CallId}", hangupUser, callId);
            }
        }

        public async Task<bool> IncomingCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult) {
            using var scope = _serviceScopeFactory.CreateScope();
            var aiSettingsProvider = scope.ServiceProvider.GetRequiredService<IAICustomerServiceSettingsProvider>();
            var aiSettings = await aiSettingsProvider.GetSettingsAsync();

            ICallScenario? callScenario = null;
            var id = sipRequest.Header.From.FromURI.Parameters.Get("id");

            CallContext? ctx = null;
            if (!string.IsNullOrEmpty(id) && id.StartsWith("AI_Caller_")) {
                if (!_contexts.TryGetValue(id, out ctx)) {
                    throw new Exception($"无效的呼入id: {id}");
                }
            } else {
                ctx = new CallContext {
                    Caller = new Models.Caller {
                        User = routingResult.CallerUser,
                        Number = routingResult.CallerNumber
                    }
                };
                _contexts.TryAdd(ctx.CallId, ctx);
            }

            if (aiSettings.Enabled) {
                if (routingResult.Strategy == CallHandlingStrategy.WebToWeb) {
                    callScenario = ctx.IsServerCalling ? scope.ServiceProvider.GetRequiredService<ServerToWebScenario>() : scope.ServiceProvider.GetRequiredService<WebToServerScenario>();
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

            if (callScenario == null) {
                _logger.LogWarning($"新呼入{sipRequest.Header.From.FromURI.User} : {sipRequest.Header.To.ToURI.User} 路由策略: {routingResult.Strategy} 未找到可以处理通话的场景");
                return false;
            }

            var result = await callScenario.HandleInboundCallAsync(sipRequest, routingResult, ctx);

            // 创建来电CallLog记录
            if (result) {
                _ = CreateInboundCallLogAsync(ctx, routingResult);
            }

            if (result && ctx.Callee != null) {
                ctx.Callee.Client!.Client.CallEnded += (client, status) => {
                    _logger.LogInformation("被叫方CallEnded事件触发: {CallId}, Status: {Status}", ctx.CallId, status);
                    _ = HandleCallEndedAsync(ctx.CallId, ctx.Callee.User?.Id ?? 0, status);
                };

                if (!aiSettings.Enabled) {
                    var ringtoneService = scope.ServiceProvider.GetRequiredService<IRingtoneService>();
                    var ringbackTone = await ringtoneService.GetRingtoneForUserAsync(ctx.Callee.User!.Id, RingtoneType.Ringback);

                    StartRingbackTone(ctx, ringbackTone.FilePath);
                }
            }

            OnMakeCalled(ctx);

            return result;
        }

        public async Task<CallContext> MakeCallAsync(string destination, User caller, RTCSessionDescriptionInit? offer, CallScenario scenario, int? preferredLineId = null, bool autoSelectLine = true) {
            using var scope = _serviceScopeFactory.CreateScope();

            if (caller.SipAccount == null) {
                throw new Exception($"用户{caller.Username}没有有效的SIP账户");
            }

            ISipLineSelector _lineSelector = scope.ServiceProvider.GetRequiredService<ISipLineSelector>();

            var phoneRoutingInfo = await _lineSelector.SelectRoutingAsync(
                caller.SipAccount,
                preferredLineId,
                autoSelectLine
            );

            var coreRoutingInfo = new AI.Caller.Core.Models.SipRoutingInfo {
                ProxyServer = phoneRoutingInfo.ProxyServer,
                OutboundProxy = phoneRoutingInfo.OutboundProxy
            };

            ICallScenario callScenario = scenario switch {
                CallScenario.WebToWeb => scope.ServiceProvider.GetRequiredService<WebToWebScenario>(),
                CallScenario.WebToMobile => scope.ServiceProvider.GetRequiredService<WebToMobileScenario>(),
                CallScenario.WebToServer => scope.ServiceProvider.GetRequiredService<WebToServerScenario>(),
                CallScenario.ServerToWeb => scope.ServiceProvider.GetRequiredService<ServerToWebScenario>(),
                CallScenario.ServerToMobile => scope.ServiceProvider.GetRequiredService<ServerToMobileScenario>(),
                _ => throw new ArgumentException($"不支持的通话场景: {scenario}")
            };

            var ctx = new CallContext() {
                Type = scenario,
                Caller = new Models.Caller {
                    User = caller
                },
                RoutingInfo = coreRoutingInfo
            };
            _contexts.TryAdd(ctx.CallId, ctx);

            var result = await callScenario.HandleOutboundCallAsync(destination, caller, offer, ctx);
            if (!result) throw new Exception($"呼叫失败");

            // 创建外呼CallLog记录
            if (scenario != CallScenario.ServerToMobile && scenario != CallScenario.ServerToWeb)
                _ = CreateOutboundCallLogAsync(ctx, caller, destination);

            if (ctx.Caller?.Client?.Client != null) {
                ctx.Caller.Client.Client.CallRinging += (client) => {
                    _logger.LogDebug("CallRinging事件触发，启动回铃音: {CallId}", ctx.CallId);
                    StartRingbackTone(ctx);
                };

                ctx.Caller.Client.Client.CallAnswered += (client) => {
                    _logger.LogInformation("CallAnswered事件触发，停止回铃音: {CallId}", ctx.CallId);
                    CleanRingback(ctx);
                    if (ctx.Caller != null && ctx.Caller.User != null) {
                        _ = _hubContext.Clients.User(ctx.Caller.User.Id.ToString()).SendAsync("answered");
                    }
                };

                ctx.Caller.Client.Client.CallEnded += (client, status) => {
                    _logger.LogInformation("主叫方CallEnded事件触发: {CallId}, Status: {Status}", ctx.CallId, status);
                    _ = HandleCallEndedAsync(ctx.CallId, ctx.Caller.User?.Id ?? 0, status);
                };

            }

            OnMakeCalled(ctx);

            return ctx;
        }

        public async Task SendDtmfAsync(byte tone, int sendUser, string callId) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                throw new Exception($"无效的呼叫标识:{callId}");
            }
            if (ctx.Caller != null && ctx.Caller.Client != null && ctx.Caller.User != null && ctx.Caller.User.Id == sendUser) {
                await ctx.Caller.Client!.Client.SendDTMFAsync(tone);
            } else if (ctx.Callee != null && ctx.Callee.Client != null && ctx.Callee.User != null && ctx.Callee.User.Id == sendUser) {
                await ctx.Callee.Client!.Client.SendDTMFAsync(tone);
            }
        }

        public async Task<string> StartDtmfCollectionAsync(string callId, DtmfCollectionConfig? config = null, CancellationToken ct = default) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                throw new Exception($"无效的呼叫标识:{callId}");
            }

            // 订阅DTMF接收事件
            ctx.Caller!.Client!.Client.DtmfToneReceived += (client, tone) => {
                _logger.LogDebug("主叫方DTMF按键接收: {CallId}, 按键: {Tone}", ctx.CallId, tone);
                _dtmfService.OnDtmfToneReceived(ctx.CallId, tone);
            };

            config ??= new AI.Caller.Core.Services.DtmfCollectionConfig {
                MaxLength = 18,
                TerminationKey = '#',
                BackspaceKey = '*',
                Timeout = TimeSpan.FromSeconds(30),
                EnableLogging = true,
                Description = $"通话 {callId} 的DTMF收集"
            };

            _logger.LogInformation("开始DTMF收集: {CallId}, 配置: {Config}", callId, System.Text.Json.JsonSerializer.Serialize(config));

            return await _dtmfService.StartCollectionWithConfigAsync(callId, config, ct);
        }

        public async Task StopDtmfCollectionAsync(string callId) {
            await _dtmfService.StopCollectionAsync(callId);
            _logger.LogInformation("停止DTMF收集: {CallId}", callId);
        }

        public async Task ResetDtmfCollectionAsync(string callId) {
            await _dtmfService.ResetCollectionAsync(callId);
            _logger.LogInformation("重置DTMF收集: {CallId}", callId);
        }

        public async Task<string> GetCurrentDtmfInputAsync(string callId) {
            return await _dtmfService.GetCurrentInputAsync(callId);
        }

        public async Task<bool> IsDtmfCollectingAsync(string callId) {
            return await _dtmfService.IsCollectingAsync(callId);
        }

        public Task<AudioCodec> GetCurrentCodecAsync(string callId) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                return Task.FromResult(AudioCodec.PCMA); // Default value
            }

            var client = ctx.Caller?.Client?.Client ?? ctx.Callee?.Client?.Client;
            return Task.FromResult(client?.MediaSessionManager?.SelectedCodec ?? AudioCodec.PCMA);
        }

        public Task<bool> SwitchCodecAsync(string callId, AudioCodec targetCodec, string reason) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                return Task.FromResult(false);
            }

            var client = ctx.Caller?.Client?.Client ?? ctx.Callee?.Client?.Client;
            if (client?.MediaSessionManager != null) {
                return client.MediaSessionManager.SwitchCodec(targetCodec, reason);
            }
            
            return Task.FromResult(false);
        }

        private void OnHangupCall(CallContext ctx) {
            CleanRingback(ctx);

            _ = _dtmfService.StopCollectionAsync(ctx.CallId);

            if (ctx.Caller != null && ctx.Caller.User != null) {
                if (ctx.Caller.Client != null) {
                    _recordingManager.OnSipHanguped(ctx.Caller.User.Id, ctx.Caller.Client.Client);
                }
                _logger.LogInformation("Hangup detected. Stopping AI service for Caller User ID: {UserId}", ctx.Caller.User.Id);
                _ = _aiManager.StopAICustomerServiceAsync(ctx.Caller.User.Id);
            }

            if (ctx.Callee != null && ctx.Callee.User != null) {
                if (ctx.Callee.Client != null) {
                    _recordingManager.OnSipHanguped(ctx.Callee.User.Id, ctx.Callee.Client.Client);
                }
                _logger.LogInformation("Hangup detected. Stopping AI service for Callee User ID: {UserId}", ctx.Callee.User.Id);
                _ = _aiManager.StopAICustomerServiceAsync(ctx.Callee.User.Id);
            }

            _contexts.TryRemove(ctx.CallId, out _);
            _logger.LogWarning($"OnHangupCall=>已清理会话:{ctx.CallId}");
        }

        private void OnMakeCalled(CallContext ctx) {
            if (ctx.Caller != null && ctx.Caller.User != null && ctx.Caller.Client != null && !ctx.Caller.IsRecording) {
                ctx.Caller.IsRecording = true;
                _recordingManager.OnSipCalled(ctx.Caller.User.Id, ctx.Caller.Client.Client);
            }

            if (ctx.Callee != null && ctx.Callee.User != null && ctx.Callee.Client != null && !ctx.Callee.IsRecording) {
                ctx.Callee.IsRecording = true;
                _recordingManager.OnSipCalled(ctx.Callee.User.Id, ctx.Callee.Client.Client);
            }
        }

        private void OnCleanupContext(object? state) {
            var inactiveContexts = _contexts.Values.Where(ctx =>
                ctx.Caller != null &&
                ctx.Callee != null &&
                ctx.Caller.Client != null &&
                ctx.Callee.Client != null &&
                !ctx.Caller.Client.Client.IsCallActive &&
                !ctx.Callee.Client.Client.IsCallActive &&
                ctx.Duration > TimeSpan.FromSeconds(30)).ToList();

            foreach (var ctx in inactiveContexts) {
                try {
                    if (_contexts.TryRemove(ctx.CallId, out var item)) {
                        if (item.Caller!.Client!.Client.IsCallActive) {
                            item.Caller.Client.Dispose();
                        } else if (item!.Callee!.Client!.Client.IsCallActive) {
                            item.Callee.Client.Dispose();
                        }
                        _logger.LogWarning($"已清理会话:{ctx.CallId}");
                    }
                } catch(Exception e) {
                    _logger.LogError(e, "过期CallContext会话异常");
                }
            }

            inactiveContexts = _contexts.Values.Where(ctx =>
               ctx.Caller !=null && ctx.Caller.Client!=null && !ctx.Caller.Client.Client.IsCallActive && (ctx.Callee == null || ctx.Callee.Client == null) && ctx.Duration > TimeSpan.FromSeconds(30)
            ).ToList();

            foreach (var ctx in inactiveContexts) {
                _contexts.TryRemove(ctx.CallId, out _);
                _logger.LogWarning($"已清理无效会话:{ctx.CallId}");
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

        private async Task HandleCallEndedAsync(string callId, int userId, CallFinishStatus status) {
            if (!_contexts.TryGetValue(callId, out var ctx)) {
                _logger.LogDebug("CallEnded触发，但呼叫上下文不存在: {CallId}, {status}", callId, status);
                return;
            }

            _logger.LogInformation("呼叫结束处理中: {CallId}, UserId={UserId}, Status={status}", callId, userId, status);

            // 更新CallLog记录
            _ = UpdateCallLogOnEndAsync(callId, status);

            try {
                if (status == CallFinishStatus.RemoteHangUp || status == CallFinishStatus.Hangup) {
                    if (ctx.Callee != null && ctx.Callee.User != null) {
                        await NotifyHangupStatusAsync(ctx.Callee.User.Id == userId ? "已挂断" : "对方已挂断", ctx.Callee.User!.Id);
                    }
                    if (ctx.Caller != null && ctx.Caller.User != null) {
                        await NotifyHangupStatusAsync(ctx.Caller.User.Id == userId ? "已挂断" : "对方已挂断", ctx.Caller.User!.Id);
                    }
                } else if(status == CallFinishStatus.Failed) {
                    await NotifyHangupStatusAsync("呼叫失败/超时", userId, "callTimeout");
                } else {
                    string notifyMsg = "通话结束";
                    switch (status) {                        
                        case CallFinishStatus.Rejected:
                            notifyMsg = "对方已拒绝";
                            break;
                        case CallFinishStatus.Busy:
                            notifyMsg = "对方忙线中";
                            break;
                        case CallFinishStatus.Cancelled:
                            notifyMsg = "已取消";
                            break;
                    }
                    await NotifyHangupStatusAsync(notifyMsg, userId);
                }

                if (ctx.Callee?.Client?.Client != null) {
                    ctx.Callee.Client.Dispose();
                }

                if (ctx.Caller?.Client?.Client != null) {
                    ctx.Caller.Client.Dispose();
                }

                OnHangupCall(ctx);
            } catch (Exception ex) {
                _logger.LogError(ex, "处理CallEnded时发生错误: {CallId}, {status}", callId, status);
            }
        }

        private void StartRingbackTone(CallContext ctx, string? customAudioFilePath = null) {
            try {
                var mediaSessionManager = ctx.Callee?.Client?.Client?.MediaSessionManager;
                if (mediaSessionManager == null) {
                    _logger.LogWarning("无法启动回铃音：被叫方MediaSessionManager未初始化, CallId: {CallId}", ctx.CallId);
                    return;
                }

                var voipSession = mediaSessionManager.MediaSession as VoIPMediaSession;
                if (voipSession == null) {
                    _logger.LogWarning("无法启动回铃音：被叫方VoIPMediaSession未初始化, CallId: {CallId}", ctx.CallId);
                    return;
                }

                if (voipSession.AudioDestinationEndPoint == null) {
                    _logger.LogDebug("跳过回铃音：RTP远程端点未建立（无Early Media），CallId: {CallId}", ctx.CallId);
                    return;
                }

                CleanRingback(ctx);

                string audioFilePath;
                if (!string.IsNullOrEmpty(customAudioFilePath)) {
                    audioFilePath = Path.Combine("wwwroot", customAudioFilePath.TrimStart('/'));
                    _logger.LogInformation("使用自定义回铃音: {FilePath}", customAudioFilePath);
                } else {
                    audioFilePath = Path.Combine("wwwroot", "ringtones", "default.mp3");
                    _logger.LogInformation("使用默认回铃音");
                }
                
                ctx.RingbackPlayer = new RingbackTonePlayer(
                    _logger,
                    mediaSessionManager,
                    GetAudioCodecFactory(), // 通过服务容器获取
                    audioFilePath
                );
                ctx.RingbackPlayer.Start();
                
                _logger.LogInformation("回铃音已启动: {CallId}, 文件: {FilePath}", ctx.CallId, audioFilePath);
            } catch (Exception ex) {
                _logger.LogError(ex, "启动回铃音失败: {CallId}", ctx.CallId);
            }
        }

        private void CleanRingback(CallContext ctx) {
            try {
                _semaphoreSlim.Wait();
                if (ctx.RingbackPlayer != null) {
                    _logger.LogDebug("停止回铃音: {CallId}", ctx.CallId);
                    try {
                        ctx.RingbackPlayer.Stop();
                        ctx.RingbackPlayer.Dispose();
                        ctx.RingbackPlayer = null;
                    } catch {
                    }
                }
            } finally {
                _semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// 通过服务容器获取AudioCodecFactory实例
        /// </summary>
        private AudioCodecFactory GetAudioCodecFactory() {
            using var scope = _serviceScopeFactory.CreateScope();
            var codecFactory = scope.ServiceProvider.GetRequiredService<AudioCodecFactory>();
            return codecFactory;
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
            var handle = await _poolManager.AcquireClientAsync(routingResult.TargetUser.SipAccount.SipServer, true, callContext.RoutingInfo);
            if (handle == null) return false;

            handle.Client.Accept(sipRequest);

            callContext.Callee = new Callee {
                User = routingResult.TargetUser,
                Client = handle
            };

            try {
                var sessionProgressSent = await handle.Client.SendSessionProgressAsync();
                if (sessionProgressSent) {
                    _logger.LogInformation("Early Media established for WebToWeb");
                } else {
                    _logger.LogWarning("Failed to establish Early Media for WebToWeb");
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Error establishing Early Media in WebToWeb");
            }

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
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, true, callContext.RoutingInfo);
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

            handle.Client.CallTrying += async (client) => {
                try {
                    await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("callTrying", new {
                        callId = callContext.CallId,
                        message = "正在呼叫...",
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogInformation($"Sent callTrying event to user {callerUser.Id}");
                } catch (Exception e) {
                    _logger.LogError(e, "发送 callTrying 事件失败");
                }
            };

            handle.Client.CallRinging += async (client) => {
                try {
                    await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("callRinging", new {
                        callId = callContext.CallId,
                        message = "对方振铃中...",
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogInformation($"Sent callRinging event to user {callerUser.Id}");
                } catch (Exception e) {
                    _logger.LogError(e, "发送 callRinging 事件失败");
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

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, true, callContext.RoutingInfo);
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

            handle.Client.CallTrying += async (client) => {
                try {
                    await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("callTrying", new {
                        callId = callContext.CallId,
                        message = "正在呼叫...",
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogInformation($"Sent callTrying event to user {callerUser.Id}");
                } catch (Exception e) {
                    _logger.LogError(e, "发送 callTrying 事件失败");
                }
            };

            handle.Client.CallRinging += async (client) => {
                try {
                    await _hubContext.Clients.User(callerUser.Id.ToString()).SendAsync("callRinging", new {
                        callId = callContext.CallId,
                        message = "对方振铃中...",
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogInformation($"Sent callRinging event to user {callerUser.Id}");
                } catch (Exception e) {
                    _logger.LogError(e, "发送 callRinging 事件失败");
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

        public MobileToWebScenario(
            ILogger<MobileToWebScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager
        ) : base(logger) {
            _logger              = logger;
            _hubContext          = hubContext;
            _poolManager         = poolManager;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            var user = routingResult.TargetUser!;

            var handle = await _poolManager.AcquireClientAsync(user.SipAccount!.SipServer, true, callContext.RoutingInfo);
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
            handle.Client.MediaSessionManager!.SetSipRemoteDescription(new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = sipRequest.Body
            });

            try {
                var sessionProgressSent = await handle.Client.SendSessionProgressAsync();
                if (sessionProgressSent) {
                    _logger.LogInformation("Early Media established for MobileToWeb");
                } else {
                    _logger.LogWarning("Failed to establish Early Media for MobileToWeb");
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Error establishing Early Media in MobileToWeb");
            }

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
                callId = callContext.CallId,
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
            var handle = await _poolManager.AcquireClientAsync(routingResult.TargetUser.SipAccount.SipServer, true, callContext.RoutingInfo);
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
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, false, callContext.RoutingInfo);
            if (handle == null) return false;

            callContext.Type = CallScenario.ServerToWeb;
            callContext.Caller!.Client = handle;
            callContext.IsServerCalling = true;

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class WebToServerScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ICallFlowOrchestrator _orchestrator;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public WebToServerScenario(
            ILogger<WebToServerScenario> logger,
            IHubContext<WebRtcHub> hubContext,
            SIPClientPoolManager poolManager,
            ICallFlowOrchestrator orchestrator,
            IServiceScopeFactory serviceScopeFactory
            ) : base(logger) {
            _logger = logger;
            _hubContext = hubContext;
            _poolManager = poolManager;
            _orchestrator = orchestrator;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            if (routingResult.TargetUser == null) throw new Exception($"被叫坐席用户不能为空");
            if (routingResult.TargetUser.SipAccount == null) throw new Exception($"被叫用户不是有效坐席：{routingResult.TargetUser.Id}");
            var handle = await _poolManager.AcquireClientAsync(routingResult.TargetUser.SipAccount.SipServer, false, callContext.RoutingInfo);
            if (handle == null) return false;

            handle.Client.Accept(sipRequest);

            callContext.Callee = new Callee {
                User = routingResult.TargetUser,
                Client = handle
            };

            //callContext.Caller!.Client!.Client.MediaSessionManager!.MediaConfigurationChanged += (codec, sampleRate, payloadType) => {
            //    _ = callContext.Callee!.Client!.Client.MediaSessionManager!.SwitchCodec(codec);
            //};

            //callContext.Callee.Client.Client.MediaSessionManager!.MediaConfigurationChanged += (codec, sampleRate, payloadType) => {
            //    _ = callContext.Caller!.Client!.Client.MediaSessionManager!.SwitchCodec(codec);
            //};

            await Task.Delay(1500);

            await handle.Client.AnswerAsync();

            _ = _orchestrator.HandleInboundCallAsync(callContext);
            
            return true;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");            
            if (sdpOffer == null) throw new ArgumentNullException(nameof(sdpOffer));

            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, true, callContext.RoutingInfo);
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

        public ServerToMobileScenario(
            ILogger<ServerToMobileScenario> logger,
            SIPClientPoolManager poolManager
            ) : base(logger) {
            _logger = logger;
            _poolManager = poolManager;
        }

        public override async Task<bool> HandleOutboundCallAsync(string destination, User callerUser, RTCSessionDescriptionInit? sdpOffer, CallContext callContext) {
            if (callerUser.SipAccount == null) throw new Exception($"用户{callerUser.Username}:{callerUser.Id}不是有效的SIP客服");
            
            var handle = await _poolManager.AcquireClientAsync(callerUser.SipAccount.SipServer, false, callContext.RoutingInfo);
            if (handle == null) return false;

            callContext.Type = CallScenario.ServerToMobile;
            callContext.Caller!.Client = handle;
            callContext.IsServerCalling = true;

            var fromTag = GenerateFromTag(callerUser, callContext);
            var fromHeader = new SIPFromHeader(callerUser.Username, new SIPURI(callerUser.SipAccount.SipUsername, callerUser.SipAccount.SipServer, $"id={fromTag}"), fromTag);
            await handle.Client.CallAsync(destination, fromHeader);

            return true;
        }
    }

    public class MobileToServerScenario : CallScenarioBase {
        private readonly ILogger _logger;
        private readonly SIPClientPoolManager _poolManager;
        private readonly ICallFlowOrchestrator _orchestrator;

        public MobileToServerScenario(
            ILogger<MobileToServerScenario> logger,
            SIPClientPoolManager poolManager,
            ICallFlowOrchestrator orchestrator
            ) : base(logger) {
            _logger = logger;
            _poolManager = poolManager;
            _orchestrator = orchestrator;
        }

        public override async Task<bool> HandleInboundCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult, CallContext callContext) {
            var user = routingResult.TargetUser!;

            var handle = await _poolManager.AcquireClientAsync(user.SipAccount!.SipServer, false, callContext.RoutingInfo);
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
            handle.Client.MediaSessionManager!.SetSipRemoteDescription(new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = sipRequest.Body
            });

            await Task.Delay(1500);

            await handle.Client.AnswerAsync();

            _ = _orchestrator.HandleInboundCallAsync(callContext);

            return true;
        }
    }
}