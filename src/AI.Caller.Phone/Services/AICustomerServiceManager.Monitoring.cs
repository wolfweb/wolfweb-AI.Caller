using AI.Caller.Core;
using AI.Caller.Core.Media;
using AI.Caller.Core.CallAutomation;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Exceptions;

namespace AI.Caller.Phone.Services;

/// <summary>
/// AI客服管理器 - 监听与接入功能扩展
/// </summary>
public partial class AICustomerServiceManager {
    private readonly IMonitoringService _monitoringService;
    private readonly IPlaybackControlService _playbackControlService;

    /// <summary>
    /// 开始监听通话
    /// </summary>
    /// <param name="userId">被监听的用户ID</param>
    /// <param name="monitorUserId">监听者用户ID</param>
    /// <param name="monitorUserName">监听者用户名</param>
    /// <param name="callId">通话ID</param>
    public async Task<MonitoringSession> StartMonitoringAsync(
        int userId,
        int monitorUserId,
        string monitorUserName,
        string callId) {
        try {
            // 检查会话是否存在
            var session = GetActiveSession(userId);
            if (session == null) {
                _logger.LogWarning("用户没有活跃的AI客服会话: UserId {UserId}", userId);
                throw new InvalidOperationException($"用户 {userId} 没有活跃的AI客服会话");
            }

            // 创建监听会话记录
            var monitoringSession = await _monitoringService.StartMonitoringAsync(
                callId,
                monitorUserId,
                monitorUserName);

            // 在AudioBridge中添加监听者
            if (session.AudioBridge is AudioBridge audioBridge) {
                audioBridge.AddMonitor(monitorUserId, monitorUserName);
                _logger.LogInformation("监听者已添加到AudioBridge: UserId {UserId}, MonitorUser {MonitorUserId}",
                    userId, monitorUserId);
            }

            _logger.LogInformation("监听会话已开始: SessionId {SessionId}, CallId {CallId}",
                monitoringSession.Id, callId);

            return monitoringSession;
        } catch (Exception ex) {
            _logger.LogError(ex, "开始监听失败: UserId {UserId}, MonitorUser {MonitorUserId}",
                userId, monitorUserId);
            throw;
        }
    }

    /// <summary>
    /// 停止监听通话
    /// </summary>
    /// <param name="userId">被监听的用户ID</param>
    /// <param name="monitorUserId">监听者用户ID</param>
    /// <param name="sessionId">监听会话ID</param>
    public async Task StopMonitoringAsync(int userId, int monitorUserId, int sessionId) {
        try {
            // 停止监听会话记录
            await _monitoringService.StopMonitoringAsync(sessionId);

            // 从AudioBridge中移除监听者
            var session = GetActiveSession(userId);
            if (session?.AudioBridge is AudioBridge audioBridge) {
                audioBridge.RemoveMonitor(monitorUserId);
                _logger.LogInformation("监听者已从AudioBridge移除: UserId {UserId}, MonitorUser {MonitorUserId}",
                    userId, monitorUserId);
            }

            _logger.LogInformation("监听会话已停止: SessionId {SessionId}", sessionId);
        } catch (Exception ex) {
            _logger.LogError(ex, "停止监听失败: SessionId {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// 人工接入通话
    /// </summary>
    /// <param name="userId">被接入的用户ID</param>
    /// <param name="monitorUserId">接入者用户ID</param>
    /// <param name="sessionId">监听会话ID</param>
    /// <param name="reason">接入原因</param>
    /// <param name="callId">通话ID</param>
    public async Task InterventAsync(
        int userId,
        int monitorUserId,
        int sessionId,
        string reason,
        string callId) {
        try {
            var session = GetActiveSession(userId);
            if (session == null) {
                throw new InterventionException(callId, "用户没有活跃的AI客服会话");
            }

            // 记录人工接入
            await _monitoringService.InterventionAsync(sessionId, reason);

            // 暂停AI播放
            await session.AutoResponder.PauseAsync();
            _logger.LogInformation("AI播放已暂停: UserId {UserId}", userId);

            // 更新播放控制状态
            await _playbackControlService.PausePlaybackAsync(callId);
            await _playbackControlService.RecordInterventionAsync(callId,
                session.AutoResponder.IsPaused ? 0 : -1); // 记录当前片段

            _logger.LogInformation("人工接入成功: UserId {UserId}, MonitorUser {MonitorUserId}, Reason: {Reason}",
                userId, monitorUserId, reason);
        } catch (Exception ex) {
            _logger.LogError(ex, "人工接入失败: UserId {UserId}, SessionId {SessionId}",
                userId, sessionId);
            throw;
        }
    }

    /// <summary>
    /// 退出人工接入，恢复AI播放
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="callId">通话ID</param>
    /// <param name="skipSegmentIds">要跳过的片段ID列表</param>
    /// <param name="resumePlayback">是否恢复播放</param>
    public async Task ExitInterventionAsync(
        int userId,
        string callId,
        List<int>? skipSegmentIds = null,
        bool resumePlayback = true) {
        try {
            var session = GetActiveSession(userId);
            if (session == null) {
                _logger.LogWarning("用户没有活跃的AI客服会话: UserId {UserId}", userId);
                return;
            }

            // 标记要跳过的片段
            if (skipSegmentIds != null && skipSegmentIds.Count > 0) {
                // 设置到AutoResponder中
                session.AutoResponder.SetSkippedSegments(skipSegmentIds);
                
                // 同时记录到PlaybackControl（用于监控和审计）
                foreach (var segmentId in skipSegmentIds) {
                    await _playbackControlService.SkipSegmentAsync(callId, segmentId);
                }
                _logger.LogInformation("已标记跳过片段: CallId {CallId}, Count {Count}",
                    callId, skipSegmentIds.Count);
            }

            // 恢复AI播放
            if (resumePlayback) {
                await session.AutoResponder.ResumeAsync();
                await _playbackControlService.ResumePlaybackAsync(callId);
                _logger.LogInformation("AI播放已恢复: UserId {UserId}", userId);
            } else {
                await _playbackControlService.StopPlaybackAsync(callId);
                _logger.LogInformation("AI播放已停止: UserId {UserId}", userId);
            }

            _logger.LogInformation("退出人工接入成功: UserId {UserId}, CallId {CallId}",
                userId, callId);
        } catch (Exception ex) {
            _logger.LogError(ex, "退出人工接入失败: UserId {UserId}, CallId {CallId}",
                userId, callId);
            throw;
        }
    }

    /// <summary>
    /// 获取通话的监听者列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    public List<MonitoringListener> GetCallMonitors(int userId) {
        var session = GetActiveSession(userId);
        if (session?.AudioBridge is AudioBridge audioBridge) {
            return audioBridge.GetActiveMonitors();
        }
        return new List<MonitoringListener>();
    }

    /// <summary>
    /// 检查通话是否正在被监听
    /// </summary>
    /// <param name="userId">用户ID</param>
    public bool IsCallBeingMonitored(int userId) {
        var session = GetActiveSession(userId);
        if (session?.AudioBridge is AudioBridge audioBridge) {
            return audioBridge.HasActiveMonitors();
        }
        return false;
    }

    /// <summary>
    /// 根据CallId获取会话
    /// </summary>
    /// <param name="callId">通话ID</param>
    public AIAutoResponderSession? GetSessionByCallId(string callId) {
        return _activeSessions.Values.FirstOrDefault(s => s.CallId == callId);
    }

    /// <summary>
    /// 启动场景录音模式的AI客服
    /// </summary>
    /// <param name="user">用户</param>
    /// <param name="sipClient">SIP客户端</param>
    /// <param name="scenarioRecording">场景录音</param>
    /// <param name="variables">变量字典</param>
    /// <param name="callId">通话ID（用于关联DTMF记录）</param>
    public async Task<bool> StartScenarioServiceAsync(
        User user,
        SIPClient sipClient,
        ScenarioRecording scenarioRecording,
        Dictionary<string, string> variables,
        string? callId = null) {
        try {
            if (_activeSessions.ContainsKey(user.Id)) {
                _logger.LogWarning("AI客服已在运行: User {Username}", user.Username);
                return false;
            }

            var mediaProfile = new MediaProfile(
                codec: AudioCodec.PCMA,
                payloadType: 0,
                sampleRate: 8000,
                ptimeMs: 20,
                channels: 1
            );

            var audioBridge = _serviceProvider.GetRequiredService<IAudioBridge>();
            audioBridge.Initialize(mediaProfile);

            if (sipClient.MediaSessionManager == null) {
                _logger.LogError("MediaSessionManager为空，无法启动AI客服: User {Username}", user.Username);
                return false;
            }

            var autoResponder = _autoResponderFactory.CreateAutoResponder(mediaProfile);

            // 设置音频文件播放器和DTMF收集器
            var audioFilePlayer = _serviceProvider.GetRequiredService<AudioFilePlayer>();
            var dtmfCollector = _serviceProvider.GetRequiredService<DtmfCollector>();
            var dtmfInputService = _serviceProvider.GetRequiredService<IDtmfInputService>();

            autoResponder.SetAudioFilePlayer(audioFilePlayer);
            autoResponder.SetDtmfCollector(dtmfCollector);
            autoResponder.SetDtmfInputService(dtmfInputService);

            // 设置CallContext（用于关联DTMF记录到数据库）
            if (!string.IsNullOrEmpty(callId)) {
                autoResponder.SetCallContext(callId);
                _logger.LogDebug("已设置CallContext: {CallId}", callId);
            } else {
                _logger.LogWarning("未提供CallId，DTMF输入将不会保存到数据库");
            }

            Action<byte[]> audioGeneratedHandler = (audioFrame) => {
                sipClient.MediaSessionManager?.SendAudioFrame(audioFrame);
                // 同时发送给监听者
                if (audioBridge is AudioBridge ab) {
                    ab.ProcessOutgoingAudio(audioFrame);
                }
            };
            autoResponder.OutgoingAudioGenerated += audioGeneratedHandler;

            audioBridge.IncomingAudioReceived += (audioFrame) => {
                try {
                    autoResponder.OnUplinkPcmFrame(audioFrame);
                } catch (Exception ex) {
                    _logger.LogError(ex, "处理来电音频失败");
                }
            };

            // 连接DTMF事件
            sipClient.DtmfToneReceived += (client, tone) => {
                autoResponder.OnDtmfToneReceived(tone);
            };

            sipClient.MediaSessionManager.SetAudioBridge(audioBridge);

            var session = new AIAutoResponderSession {
                User = user,
                AutoResponder = autoResponder,
                AudioBridge = audioBridge,
                ScriptText = $"场景录音: {scenarioRecording.Name}",
                StartTime = DateTime.UtcNow,
                AudioGeneratedHandler = audioGeneratedHandler,
                CallId = callId,  // 保存CallId
                ScenarioRecordingId = scenarioRecording.Id,  // 保存场景ID
                ScenarioRecording = scenarioRecording  // 保存场景对象
            };

            await autoResponder.StartAsync();
            audioBridge.Start();

            // 转换场景片段
            var segments = ConvertToScenarioSegments(scenarioRecording);

            _ = Task.Run(async () => {
                try {
                    await autoResponder.PlayScenarioAsync(segments, variables);
                    _logger.LogInformation("场景录音播放完成: User {Username}, Scenario {ScenarioName}",
                        user.Username, scenarioRecording.Name);
                } catch (Exception ex) {
                    _logger.LogError(ex, "场景录音播放失败: User {Username}", user.Username);
                }
            });

            _activeSessions[user.Id] = session;
            _logger.LogInformation("场景录音AI客服已启动: User {Username}, Scenario {ScenarioName}",
                user.Username, scenarioRecording.Name);

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "启动场景录音AI客服失败: User {Username}", user.Username);
            return false;
        }
    }

    /// <summary>
    /// 转换数据库场景片段为执行片段
    /// </summary>
    private List<Core.CallAutomation.ScenarioSegment> ConvertToScenarioSegments(ScenarioRecording scenarioRecording) {
        var segments = new List<Core.CallAutomation.ScenarioSegment>();

        foreach (var dbSegment in scenarioRecording.Segments.OrderBy(s => s.SegmentOrder)) {
            var segment = new Core.CallAutomation.ScenarioSegment {
                Id = dbSegment.Id,
                Order = dbSegment.SegmentOrder,
                Type = ConvertSegmentType(dbSegment.SegmentType),
                FilePath = dbSegment.FilePath,
                TtsText = dbSegment.TtsText,
                ConditionExpression = dbSegment.ConditionExpression,
                NextSegmentIdOnTrue = dbSegment.NextSegmentIdOnTrue,
                NextSegmentIdOnFalse = dbSegment.NextSegmentIdOnFalse
            };

            // 转换DTMF配置
            if (dbSegment.DtmfTemplate != null) {
                segment.DtmfConfig = new Core.CallAutomation.DtmfInputConfig {
                    TemplateId = dbSegment.DtmfTemplate.Id,
                    MaxLength = dbSegment.DtmfTemplate.MaxLength,
                    MinLength = dbSegment.DtmfTemplate.MinLength,
                    TerminationKey = dbSegment.DtmfTemplate.TerminationKey,
                    BackspaceKey = dbSegment.DtmfTemplate.BackspaceKey,
                    TimeoutSeconds = dbSegment.DtmfTemplate.TimeoutSeconds,
                    MaxRetries = dbSegment.DtmfTemplate.MaxRetries,
                    PromptText = dbSegment.DtmfTemplate.PromptText,
                    SuccessText = dbSegment.DtmfTemplate.SuccessText,
                    ErrorText = dbSegment.DtmfTemplate.ErrorText,
                    TimeoutText = dbSegment.DtmfTemplate.TimeoutText,
                    VariableName = dbSegment.DtmfVariableName,
                    ValidatorType = dbSegment.DtmfTemplate.ValidatorType
                };
            }

            segments.Add(segment);
        }

        return segments;
    }

    /// <summary>
    /// 转换片段类型
    /// </summary>
    private Core.CallAutomation.ScenarioSegmentType ConvertSegmentType(SegmentType dbType) {
        return dbType switch {
            SegmentType.Recording => Core.CallAutomation.ScenarioSegmentType.Recording,
            SegmentType.TTS => Core.CallAutomation.ScenarioSegmentType.TTS,
            SegmentType.DtmfInput => Core.CallAutomation.ScenarioSegmentType.DtmfInput,
            SegmentType.Condition => Core.CallAutomation.ScenarioSegmentType.Condition,
            SegmentType.Silence => Core.CallAutomation.ScenarioSegmentType.Silence,
            _ => Core.CallAutomation.ScenarioSegmentType.TTS
        };
    }
}
