using AI.Caller.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIPSorcery.SIP.App;

namespace AI.Caller.Phone.BackgroundTask {
    public class AISipRegistrationService : IHostedService {
        private readonly ILogger _logger;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly AICustomerServiceSettings _aiCustomerServiceSettings;
        private readonly List<SIPRegistrationUserAgent> _registrationAgents = new();

        public AISipRegistrationService(
            ILogger<AISipRegistrationService> logger,
            IServiceScopeFactory serviceScopeFactory,
            SIPTransportManager sipTransportManager,
            IOptions<AICustomerServiceSettings> aiCustomerServiceSettings
            ) {
            _logger                    = logger;
            _serviceScopeFactory       = serviceScopeFactory;
            _sipTransportManager       = sipTransportManager;
            _aiCustomerServiceSettings = aiCustomerServiceSettings.Value;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            if (!_aiCustomerServiceSettings.Enabled) {
                _logger.LogInformation("AI Customer Service is disabled. Skipping AI SIP account registrations.");
                return;
            }

            _logger.LogInformation("Starting AI SIP account registration service.");

            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var accounts = await dbContext.SipAccounts.ToListAsync(cancellationToken);

            if (!accounts.Any()) {
                _logger.LogInformation("No AI-enabled users found for SIP registration.");
                return;
            }

            foreach (var account in accounts) {
                if (string.IsNullOrEmpty(account.SipUsername) || string.IsNullOrEmpty(account.SipServer)) {
                    _logger.LogWarning($"SIP account for AI user '{account.SipUsername}' is incomplete. Skipping registration.");
                    continue;
                }

                _logger.LogInformation($"Registering SIP account for AI user '{account.SipUsername}' ({account.SipServer}).");

                var sipRegistrationClient = new SIPRegistrationUserAgent(
                    _sipTransportManager.SIPTransport,
                    account.SipUsername,
                    account.SipPassword,
                    account.SipServer,
                    300); // 5 minutes expiry, will be renewed automatically

                sipRegistrationClient.RegistrationSuccessful += (uri, resp) => {
                    _logger.LogInformation($"AI SIP account '{uri}' registration successful.");
                };

                sipRegistrationClient.RegistrationFailed += (uri, resp, err) => {
                    _logger.LogError($"AI SIP account '{uri}' registration failed. Response: {resp}. Error: {err}");
                };

                sipRegistrationClient.Start();
                _registrationAgents.Add(sipRegistrationClient);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Stopping AI SIP account registration service.");
            foreach (var agent in _registrationAgents) {
                agent.Stop();
            }
            _logger.LogInformation("All AI SIP registration agents stopped.");
            return Task.CompletedTask;
        }
    }
}