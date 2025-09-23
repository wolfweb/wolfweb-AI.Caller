using AI.Caller.Core.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using System.Collections.Concurrent;
using System.Threading;

namespace AI.Caller.Core {
    public class SIPClientHandle : IDisposable {
        private readonly SIPClientPoolManager _manager;
        internal SIPClientHandle(SIPClientPoolManager manager, SIPClient sipClient) {
            _manager = manager;
            Client   = sipClient;
        }

        public SIPClient Client { get; private set; }

        public void Dispose() {
            _manager.Return(Client);
        }
    }

    public class SIPClientPoolManager {
        private readonly ILogger _logger;
        private readonly WebRTCSettings _webRTCSettings;
        private readonly SemaphoreSlim _poolAccessSemaphore;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly INetworkMonitoringService _networkMonitoringService;

        private readonly ConcurrentQueue<SIPClient> _items = new();
        private readonly TimeSpan _acquireTimeout = TimeSpan.FromSeconds(30);

        private int _numItems;

        public SIPClientPoolManager(
            ILogger<SIPClientPoolManager> logger,
            IOptions<WebRTCSettings> webRTCSettings, 
            SIPTransportManager sipTransportManager,
            IServiceScopeFactory serviceScopeFactory,
            INetworkMonitoringService networkMonitoringService
        ) {
            _logger                   = logger;
            _webRTCSettings           = webRTCSettings.Value;
            _poolAccessSemaphore      = new(1, 1);
            _serviceScopeFactory      = serviceScopeFactory;
            _sipTransportManager      = sipTransportManager;
            _networkMonitoringService = networkMonitoringService;
        }

        public async Task<SIPClientHandle?> AcquireClientAsync(string sipServer, TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
            var acquireTimeout = timeout ?? _acquireTimeout;

            try {
                if (!await _poolAccessSemaphore.WaitAsync(acquireTimeout, cancellationToken)) {
                    _logger.LogWarning("Pool access timeout for server {SIPServer}", sipServer);
                    return null;
                }

                try {
                    if (_items.TryDequeue(out var item)) {
                        Interlocked.Decrement(ref _numItems);
                        return new SIPClientHandle(this, item);
                    }

                    using var scope = _serviceScopeFactory.CreateScope();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<SIPClient>>();
                    item = new SIPClient(sipServer, logger, _sipTransportManager.SIPTransport!, _webRTCSettings, _networkMonitoringService);
                    return new SIPClientHandle(this, item);
                } finally {
                    _poolAccessSemaphore.Release();
                }
            } catch (OperationCanceledException) {
                _logger.LogDebug("Client acquisition cancelled for {SIPServer}", sipServer);
                return null;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to acquire client for {SIPServer}", sipServer);
                return null;
            }
        }

        internal void Return(SIPClient sipClient) {
            try {
                _poolAccessSemaphore.Wait();
                _items.Enqueue(sipClient);                
            } finally {
                _poolAccessSemaphore.Release();
            }
        }
    }
}
        