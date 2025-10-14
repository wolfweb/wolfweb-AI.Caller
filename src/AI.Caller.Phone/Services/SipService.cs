using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
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

        private readonly IServiceScopeFactory _serviceScopeProvider;

        public SipService(
            ILogger<SipService> logger,
            AppDbContext dbContext,
            IHubContext<WebRtcHub> hubContext,
            ApplicationContext applicationContext,
            IOptions<WebRTCSettings> webRTCSettings,
            SIPTransportManager sipTransportManager,
            IServiceScopeFactory serviceScopeProvider
        ) {
            _logger = logger;
            _dbContext = dbContext;
            _hubContext = hubContext;
            _webRTCSettings = webRTCSettings.Value;            
            _retryPolicy = new HangupRetryPolicy();
            _applicationContext = applicationContext;
            _sipTransportManager = sipTransportManager;
            _serviceScopeProvider = serviceScopeProvider;
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
    }
}