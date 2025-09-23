using AI.Caller.Core.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

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

        private readonly int _minClientsPerPool = 3;
        private readonly int _maxClientsPerPool = 20;
        private readonly ConcurrentQueue<SIPClient> _items = new();
        private readonly TimeSpan _acquireTimeout = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _clientIdleTimeout = TimeSpan.FromMinutes(15);
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(2);

        private int _numItems;
        private SIPClient? _fastItem;

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
                    var item = _fastItem;
                    if (item == null || Interlocked.CompareExchange(ref _fastItem, null, item) != item) {
                        if (_items.TryDequeue(out item)) {
                            Interlocked.Decrement(ref _numItems);
                            return new SIPClientHandle(this, item);
                        }

                        using var scope = _serviceScopeFactory.CreateScope();
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SIPClient>>();
                        item = new SIPClient(sipServer, logger, _sipTransportManager.SIPTransport!, _webRTCSettings, _networkMonitoringService);
                        return new SIPClientHandle(this, item);
                    }

                    return null;
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
            if (_fastItem != null || Interlocked.CompareExchange(ref _fastItem, sipClient, null) != null) {
                if (Interlocked.Increment(ref _numItems) <= _maxClientsPerPool) {
                    _items.Enqueue(sipClient);
                }

                Interlocked.Decrement(ref _numItems);
            }
        }
    }
}
        