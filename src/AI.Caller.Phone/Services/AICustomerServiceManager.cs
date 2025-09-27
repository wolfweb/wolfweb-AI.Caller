using AI.Caller.Core;
using AI.Caller.Core.Interfaces;
using AI.Caller.Phone.Entities;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// AI客服管理器，负责管理AI自动应答实例的生命周期
    /// </summary>
    public class AICustomerServiceManager : IDisposable {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IAIAutoResponderFactory _autoResponderFactory;
        
        private readonly ConcurrentDictionary<int, AIAutoResponderSession> _activeSessions = new();

        public AICustomerServiceManager(
            ILogger<AICustomerServiceManager> logger,
            IServiceProvider serviceProvider,
            IAIAutoResponderFactory autoResponderFactory
            ) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _autoResponderFactory = autoResponderFactory;
        }

        /// <summary>
        /// 为用户启动AI客服会话
        /// </summary>
        public async Task<bool> StartAICustomerServiceAsync(User user, SIPClient sipClient, string scriptText) {
            try {
                if (_activeSessions.ContainsKey(user.Id)) {
                    _logger.LogWarning($"AI customer service already active for user {user.Username}");
                    return false;
                }

                var mediaProfile = new MediaProfile(
                    codec: AI.Caller.Core.AudioCodec.PCMU,
                    payloadType: 0,
                    sampleRate: 8000,
                    ptimeMs: 20,
                    channels: 1
                );

                using var scope = _serviceProvider.CreateScope();
                var audioBridge = scope.ServiceProvider.GetRequiredService<IAudioBridge>();
                audioBridge.Initialize(mediaProfile);

                var autoResponder = _autoResponderFactory.CreateAutoResponder(audioBridge, mediaProfile);

                if (sipClient.MediaSessionManager != null) {
                    sipClient.MediaSessionManager.SetAudioBridge(audioBridge);
                }

                var session = new AIAutoResponderSession {
                    User = user,
                    AutoResponder = autoResponder,
                    AudioBridge = audioBridge,
                    ScriptText = scriptText,
                    StartTime = DateTime.UtcNow
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

                await session.AutoResponder.StopAsync();
                session.AudioBridge.Stop();
                
                await session.AutoResponder.DisposeAsync();
                session.AudioBridge.Dispose();

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

        public void Dispose() {
            var sessions = _activeSessions.Values.ToList();
            _activeSessions.Clear();

            foreach (var session in sessions) {
                try {
                    session.AutoResponder.StopAsync().Wait(TimeSpan.FromSeconds(5));
                    session.AudioBridge.Stop();
                    session.AutoResponder.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                    session.AudioBridge.Dispose();
                } catch (Exception ex) {
                    _logger.LogError(ex, $"Error disposing AI customer service session for user {session.User.Username}");
                }
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
    }
}