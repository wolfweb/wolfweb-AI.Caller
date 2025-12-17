using AI.Caller.Core;
using AI.Caller.Core.Interfaces;
using AI.Caller.Phone.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// AI客服管理器，负责管理AI自动应答实例的生命周期
    /// </summary>
    public partial class AICustomerServiceManager : IDisposable {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IAIAutoResponderFactory _autoResponderFactory;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ConcurrentDictionary<int, AIAutoResponderSession> _activeSessions = new();

        public AICustomerServiceManager(
            ILogger<AICustomerServiceManager> logger,
            IServiceProvider serviceProvider,
            IAIAutoResponderFactory autoResponderFactory,
            IServiceScopeFactory scopeFactory
            ) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _autoResponderFactory = autoResponderFactory;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// 为用户启动AI客服会话
        /// </summary>
        /// <param name="user">用户</param>
        /// <param name="sipClient">SIP客户端</param>
        /// <param name="scriptText">脚本文本</param>
        /// <param name="callId">通话ID（可选，用于关联DTMF记录）</param>
        public async Task<bool> StartAICustomerServiceAsync(User user, SIPClient sipClient, string scriptText, string? callId = null) {
            try {
                if (_activeSessions.ContainsKey(user.Id)) {
                    _logger.LogWarning($"AI customer service already active for user {user.Username}");
                    return false;
                }

                var scope = _scopeFactory.CreateScope(); // Create scope for the session
                
                try {
                    var mediaProfile = new MediaProfile(
                        codec: AudioCodec.PCMA,
                        payloadType: 0,
                        sampleRate: 8000,
                        ptimeMs: 20,
                        channels: 1
                    );

                    var audioBridge = scope.ServiceProvider.GetRequiredService<IAudioBridge>();
                    audioBridge.Initialize(mediaProfile);

                    if (sipClient.MediaSessionManager == null) {
                        _logger.LogError("MediaSessionManager is null, cannot start AI customer service for user {Username}", user.Username);
                        scope.Dispose(); // Dispose if failed
                        return false;
                    }

                    var autoResponder = _autoResponderFactory.CreateAutoResponder(mediaProfile);
                    
                    if (!string.IsNullOrEmpty(callId)) {
                        autoResponder.SetCallContext(callId);
                        _logger.LogDebug("已设置CallContext: {CallId}", callId);
                    }
                    
                    Action<DtmfInputEventArgs> dtmfHandler = async (dtmfEventArgs) => {
                        await HandleDtmfInputCollectedAsync(dtmfEventArgs);
                    };
                    autoResponder.OnDtmfInputCollected += dtmfHandler;
                    
                    Action<byte[]> audioGeneratedHandler = (audioFrame) => {
                        sipClient.MediaSessionManager?.SendAudioFrame(audioFrame);
                    };
                    autoResponder.OutgoingAudioGenerated += audioGeneratedHandler;

                    audioBridge.IncomingAudioReceived += (audioFrame) => {
                        try {
                            autoResponder.OnUplinkPcmFrame(audioFrame);
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error processing incoming audio in AutoResponder");
                        }
                    };

                    sipClient.MediaSessionManager.SetAudioBridge(audioBridge);

                    var session = new AIAutoResponderSession {
                        User = user,
                        AutoResponder = autoResponder,
                        AudioBridge = audioBridge,
                        ScriptText = scriptText,
                        StartTime = DateTime.UtcNow,
                        AudioGeneratedHandler = audioGeneratedHandler,
                        DtmfInputHandler = dtmfHandler,
                        Scope = scope // Store scope
                    };

                    await autoResponder.StartAsync();
                    audioBridge.Start();

                    _ = Task.Run(async () => {
                        try {
                            await autoResponder.PlayScriptAsync(scriptText);
                            _logger.LogInformation($"AI customer service script completed for user {user.Username}");
                        } catch (Exception ex) {
                            _logger.LogError(ex, $"Error playing script for user {user.Username}");
                        }
                    });

                    _activeSessions[user.Id] = session;
                    _logger.LogInformation($"AI customer service started for user {user.Username}");

                    return true;
                } catch {
                    scope.Dispose(); // Dispose if exception
                    throw;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Failed to start AI customer service for user {user.Username}");
                return false;
            }
        }

        /// <summary>
        /// 停止用户的AI客服会话
        /// </summary>
        public async Task<bool> StopAICustomerServiceAsync(int userId) {
            try {
                if (!_activeSessions.TryRemove(userId, out var session)) {
                    _logger.LogWarning($"No active AI customer service session found for user ID {userId}");
                    return false;
                }

                if (session.AudioGeneratedHandler != null) {
                    session.AutoResponder.OutgoingAudioGenerated -= session.AudioGeneratedHandler;
                }

                if (session.DtmfInputHandler != null) {
                    session.AutoResponder.OnDtmfInputCollected -= session.DtmfInputHandler;
                }

                if (session.PlaybackTask != null && !session.PlaybackTask.IsCompleted) {
                    try {
                        await session.PlaybackTask.WaitAsync(TimeSpan.FromSeconds(2));
                        _logger.LogDebug("播放任务已正常完成");
                    } catch (TimeoutException) {
                        _logger.LogWarning("播放任务超时，强制停止");
                    } catch (Exception ex) {
                        _logger.LogWarning(ex, "等待播放任务完成时出错");
                    }
                }

                await session.AutoResponder.StopAsync();
                session.AudioBridge.Stop();

                await session.AutoResponder.DisposeAsync();
                session.AudioBridge.Dispose();
                session.Scope?.Dispose(); // Dispose scope

                _logger.LogInformation($"AI customer service stopped for user {session.User.Username}");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error stopping AI customer service for user ID {userId}");
                return false;
            }
        }

        /// <summary>
        /// 检查用户是否有活跃的AI客服会话
        /// </summary>
        public bool IsAICustomerServiceActive(int userId) {
            return _activeSessions.ContainsKey(userId);
        }

        /// <summary>
        /// 获取活跃会话信息
        /// </summary>
        public AIAutoResponderSession? GetActiveSession(int userId) {
            _activeSessions.TryGetValue(userId, out var session);
            return session;
        }

        /// <summary>
        /// 获取所有活跃会话
        /// </summary>
        public IEnumerable<AIAutoResponderSession> GetAllActiveSessions() {
            return _activeSessions.Values.ToList();
        }

        /// <summary>
        /// 获取会话的播放状态
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>播放状态信息</returns>
        public (bool IsPlaying, TaskStatus? TaskStatus, string? StatusMessage) GetPlaybackStatus(int userId) {
            if (!_activeSessions.TryGetValue(userId, out var session)) {
                return (false, null, "会话不存在");
            }

            if (session.PlaybackTask == null) {
                return (false, null, "无播放任务");
            }

            var taskStatus = session.PlaybackTask.Status;
            var isPlaying = taskStatus == TaskStatus.Running || taskStatus == TaskStatus.WaitingForActivation;
            
            var statusMessage = taskStatus switch {
                TaskStatus.Running => "正在播放",
                TaskStatus.RanToCompletion => "播放完成",
                TaskStatus.Canceled => "播放已取消",
                TaskStatus.Faulted => $"播放失败: {session.PlaybackTask.Exception?.GetBaseException().Message}",
                TaskStatus.WaitingForActivation => "等待开始",
                _ => taskStatus.ToString()
            };

            return (isPlaying, taskStatus, statusMessage);
        }

        public void Dispose() {
            var sessions = _activeSessions.Values.ToList();
            _activeSessions.Clear();

            var disposeTasks = sessions.Select(async session => {
                try {
                    var stopTask = session.AutoResponder.StopAsync();
                    var disposeTask = session.AutoResponder.DisposeAsync().AsTask();
                    
                    await Task.WhenAll(
                        stopTask.WaitAsync(TimeSpan.FromSeconds(3)),
                        disposeTask.WaitAsync(TimeSpan.FromSeconds(3))
                    );
                    
                    session.AudioBridge.Stop();
                    session.AudioBridge.Dispose();
                    session.Scope?.Dispose();
                    
                    _logger.LogDebug("Successfully disposed session for user {Username}", session.User.Username);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error disposing AI customer service session for user {Username}", session.User.Username);
                }
            });

            try {
                Task.WaitAll(disposeTasks.ToArray(), TimeSpan.FromSeconds(10));
            } catch (Exception ex) {
                _logger.LogError(ex, "Timeout or error during bulk session disposal");
            }
        }

        /// <summary>
        /// 处理DTMF输入收集完成事件
        /// </summary>
        private async Task HandleDtmfInputCollectedAsync(AI.Caller.Core.DtmfInputEventArgs eventArgs) {
            try {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var dtmfRecord = new DtmfInputRecord {
                    CallId = eventArgs.CallId,
                    SegmentId = eventArgs.SegmentId,
                    TemplateId = eventArgs.TemplateId,
                    InputValue = eventArgs.InputValue,
                    IsValid = eventArgs.IsValid,
                    ValidationMessage = eventArgs.ValidationMessage,
                    InputTime = eventArgs.InputTime,
                    RetryCount = eventArgs.RetryCount,
                    Duration = eventArgs.Duration
                };

                dbContext.DtmfInputRecords.Add(dtmfRecord);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("DTMF输入记录已保存: CallId={CallId}, SegmentId={SegmentId}, IsValid={IsValid}, RetryCount={RetryCount}", eventArgs.CallId, eventArgs.SegmentId, eventArgs.IsValid, eventArgs.RetryCount);

                if (!string.IsNullOrEmpty(eventArgs.VariableName)) {
                    _logger.LogDebug("DTMF输入变量: {VariableName} = {Value}", eventArgs.VariableName, eventArgs.InputValue);
                }

            } catch (Exception ex) {
                _logger.LogError(ex, "保存DTMF输入记录失败: CallId={CallId}, SegmentId={SegmentId}", eventArgs.CallId, eventArgs.SegmentId);
            }
        }
    }

    /// <summary>
    /// AI自动应答会话信息
    /// </summary>
    public class AIAutoResponderSession {
        public User User { get; set; } = null!;
        public AIAutoResponder AutoResponder { get; set; } = null!;
        public IAudioBridge AudioBridge { get; set; } = null!;
        public string ScriptText { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public Action<byte[]>? AudioGeneratedHandler { get; set; }
        public Action<DtmfInputEventArgs>? DtmfInputHandler { get; set; }
        public string? CallId { get; set; }
        public int? ScenarioRecordingId { get; set; }
        public ScenarioRecording? ScenarioRecording { get; set; }
        public IServiceScope? Scope { get; set; }
        public Task? PlaybackTask { get; set; }
    }
}