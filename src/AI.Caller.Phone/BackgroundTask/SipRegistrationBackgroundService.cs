using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AI.Caller.Phone.BackgroundTask {
    public class SipRegistrationBackgroundService : IHostedService {
        private readonly ILogger _logger;
        private readonly Channel<User> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;

        private readonly ConcurrentDictionary<int, SIPRegistrationUserAgent> _activeAgents = new();

        public SipRegistrationBackgroundService(
            ILogger<SipRegistrationBackgroundService> logger,
            Channel<User> channel,
            IServiceScopeFactory scopeFactory,
            ApplicationContext applicationContext,
            SIPTransportManager sipTransportManager
        ) {
            _logger = logger;
            _channel = channel;
            _scopeFactory = scopeFactory;
            _applicationContext = applicationContext;
            _sipTransportManager = sipTransportManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _ = UserActiviteCustome(cancellationToken);

            var sipAccounts = await dbContext.SipAccounts
                .Where(sa => sa.IsActive && !string.IsNullOrEmpty(sa.SipServer))
                .ToListAsync();

            foreach (var sipAccount in sipAccounts) {
                var agent = new SIPRegistrationUserAgent(
                    _sipTransportManager.SIPTransport,
                    sipAccount.SipUsername,
                    sipAccount.SipPassword,
                    sipAccount.SipServer,
                    180 
                );

                agent.RegistrationFailed += (uri, resp, err) => _logger.LogError($"SIP Register Failed [User {sipAccount.SipUsername}]: {err}");

                agent.RegistrationSuccessful += (uri, resp) => _logger.LogInformation($"SIP Register OK [User {sipAccount.SipUsername}]");

                if (_activeAgents.TryAdd(sipAccount.Id, agent)) {
                    agent.Start();
                    _logger.LogInformation($"Started SIP Agent for User {sipAccount.Id}");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            foreach (var kvp in _activeAgents) {
                try {
                    var userId = kvp.Key;
                    var agent = kvp.Value;
                    _logger.LogInformation($"Unregistering User {userId}...");
                    agent.Stop();
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error during unregistration.");
                }
            }

            _activeAgents.Clear();
            return Task.CompletedTask;
        }

        private async Task UserActiviteCustome(CancellationToken cancellationToken) {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken)) {
                while (_channel.Reader.TryRead(out var user)) {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    if (user.RegisteredAt == null || user.RegisteredAt < DateTime.UtcNow.AddMicroseconds(-5) || user.RegisteredAt < _applicationContext.StartAt) {
                        if(_activeAgents.Keys.Any(x=>x == user.SipAccount!.Id)) {
                            user.SipRegistered = true;
                            user.RegisteredAt = DateTime.UtcNow;
                            await dbContext.SaveChangesAsync();
                        }
                    }
                }
            }
        }
    }
}
