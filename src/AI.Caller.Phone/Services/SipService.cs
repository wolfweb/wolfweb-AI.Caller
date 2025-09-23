using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIPSorcery.SIP.App;

namespace AI.Caller.Phone.Services {
    public class SipService {
        private readonly ILogger _logger;
        private readonly AppDbContext _dbContext;
        private readonly HangupRetryPolicy _retryPolicy;
        private readonly WebRTCSettings _webRTCSettings;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly HangupMonitoringService _monitoringService;

        private readonly IServiceScopeFactory _serviceScopeProvider;
        private readonly AICustomerServiceManager _aiCustomerServiceManager;
        private readonly AICustomerServiceSettings _aiSettings;

        public SipService(
            ILogger<SipService> logger,
            AppDbContext dbContext,
            IHubContext<WebRtcHub> hubContext,
            ApplicationContext applicationContext,
            SIPTransportManager sipTransportManager,
            IOptions<WebRTCSettings> webRTCSettings,
            IServiceScopeFactory serviceScopeProvider,
            AICustomerServiceManager aiCustomerServiceManager,
            IOptions<AICustomerServiceSettings> aiSettings,
            HangupMonitoringService? monitoringService = null
        ) {
            _logger = logger;
            _dbContext = dbContext;
            _hubContext = hubContext;
            _aiSettings = aiSettings.Value;
            _webRTCSettings = webRTCSettings.Value;
            _applicationContext = applicationContext;
            _sipTransportManager = sipTransportManager;
            _retryPolicy = new HangupRetryPolicy();
            _monitoringService = monitoringService ?? new HangupMonitoringService(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HangupMonitoringService>());
            _serviceScopeProvider = serviceScopeProvider;
            _aiCustomerServiceManager = aiCustomerServiceManager;
        }

        public async Task<bool> RegisterUserAsync(User user) {
            if (user.SipAccount == null || string.IsNullOrEmpty(user.SipAccount.SipUsername)) {
                _logger.LogWarning($"用户 {user.Username} 的SIP账号信息不完整");
                return false;
            }

            try {
                if (user.RegisteredAt == null || user.RegisteredAt < DateTime.UtcNow.AddHours(-2) || user.RegisteredAt < _applicationContext.StartAt) {
                    RegisterAsync(user);
                    user.SipRegistered = true;
                    user.RegisteredAt = DateTime.UtcNow;
                }

                var sipClient = new SIPClient(user.SipAccount.SipServer, _logger, _sipTransportManager.SIPTransport!, _webRTCSettings);

                sipClient.StatusMessage += (_, message) => {
                    _logger.LogDebug($"SIP客户端状态更新: {message}");
                };

                sipClient.CallAnswered += async _ => {
                    await _hubContext.Clients.User(user.Id.ToString()).SendAsync("answered");                   
                };

                sipClient.CallFinishedWithContext += async (client, context) => {
                    await HandleCallFinishedWithContext(user.Id, context);
                };

                _logger.LogDebug($"用户 {user.Username} : {user.SipAccount.SipUsername} 的SIP账号注册成功");
                await _dbContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 的SIP账号注册失败");
                return false;
            }
        }

        private void RegisterAsync(User user) {
            //var tcs = new TaskCompletionSource<bool>();
            if (user.SipAccount?.SipUsername == null || user.SipAccount?.SipPassword == null) {
                throw new InvalidOperationException("用户SIP账号信息不完整");
            }

            var sipRegistrationClient = new SIPRegistrationUserAgent(_sipTransportManager.SIPTransport, user.SipAccount.SipUsername, user.SipAccount.SipPassword, user.SipAccount.SipServer, 180);

            sipRegistrationClient.RegistrationSuccessful += (uri, resp) => {
                _logger.LogInformation($"register success for {uri} => {resp}");
                //tcs.TrySetResult(true);
            };

            sipRegistrationClient.RegistrationFailed += (uri, resp, err) => {
                _logger.LogError($"register failed for {uri} => {resp}, {err}");
                //tcs.TrySetResult(false);
            };

            sipRegistrationClient.Start();

            //return await tcs.Task;
        }

        private async Task HandleCallFinishedWithContext(int userId, HangupEventContext context) {
            try {
                if (context.IsRemoteInitiated) {
                    _logger.LogInformation($"检测到远程挂断，通知Web端用户: {userId}");
                    await NotifyWebClientRemoteHangup(userId, context.Reason);
                } else {
                    _logger.LogInformation($"本地发起的挂断，无需通知Web端: {userId}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理挂断事件失败 - 用户: {userId}");
            }
        }

        private async Task NotifyWebClientRemoteHangup(int userId, string reason) {
            using var scope = _serviceScopeProvider.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await _dbContext.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x=>x.Id == userId);
            if (user == null) {
                _logger.LogWarning($"未找到SIP用户名为 {userId} 的用户");
                return;
            }

            var retryCount = 0;
            var delay = _retryPolicy.RetryDelay;

            while (retryCount < _retryPolicy.MaxRetries) {
                try {
                    using var cts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);
                    await _hubContext.Clients.User(user.Id.ToString())
                        .SendAsync("remoteHangup", new { reason }, cts.Token);

                    _logger.LogInformation($"成功通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 远程挂断事件");
                    return;
                } catch (OperationCanceledException) {
                    _logger.LogWarning($"通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 超时 (第 {retryCount + 1} 次尝试)");
                } catch (Exception ex) {
                    _logger.LogError(ex, $"通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 远程挂断事件失败 (第 {retryCount + 1} 次尝试)");
                }

                retryCount++;
                if (retryCount < _retryPolicy.MaxRetries) {
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _retryPolicy.MaxRetryDelay.TotalMilliseconds));
                }
            }

            _logger.LogError($"通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 远程挂断事件失败，已达到最大重试次数 {_retryPolicy.MaxRetries}");
        }
    }
}