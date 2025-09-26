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

        private readonly ConcurrentQueue<SIPClient> _webRtcEnabledItems = new();
        private readonly ConcurrentQueue<SIPClient> _webRtcDisabledItems = new();
        private readonly TimeSpan _acquireTimeout = TimeSpan.FromSeconds(30);

        private int _webRtcEnabledCount;
        private int _webRtcDisabledCount;

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

        public async Task<SIPClientHandle?> AcquireClientAsync(string sipServer, bool enableWebRtcBridging, TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
            var acquireTimeout = timeout ?? _acquireTimeout;

            try {
                if (!await _poolAccessSemaphore.WaitAsync(acquireTimeout, cancellationToken)) {
                    _logger.LogWarning("Pool access timeout for server {SIPServer}", sipServer);
                    return null;
                }

                try {
                    var targetQueue = enableWebRtcBridging ? _webRtcEnabledItems : _webRtcDisabledItems;
                    var targetCount = enableWebRtcBridging ? _webRtcEnabledCount : _webRtcDisabledCount;

                    if (targetQueue.TryDequeue(out var item)) {
                        if (enableWebRtcBridging) {
                            Interlocked.Decrement(ref _webRtcEnabledCount);
                        } else {
                            Interlocked.Decrement(ref _webRtcDisabledCount);
                        }
                        _logger.LogDebug("Reused SIPClient from pool (WebRTC bridging: {EnableBridging})", enableWebRtcBridging);
                        return new SIPClientHandle(this, item);
                    }

                    using var scope = _serviceScopeFactory.CreateScope();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<SIPClient>>();
                    item = new SIPClient(sipServer, logger, _sipTransportManager.SIPTransport!, _webRTCSettings, _networkMonitoringService, enableWebRtcBridging);
                    _logger.LogDebug("Created new SIPClient (WebRTC bridging: {EnableBridging})", enableWebRtcBridging);
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
                
                if (sipClient.EnableWebRtcBridging) {
                    _webRtcEnabledItems.Enqueue(sipClient);
                    Interlocked.Increment(ref _webRtcEnabledCount);
                } else {
                    _webRtcDisabledItems.Enqueue(sipClient);
                    Interlocked.Increment(ref _webRtcDisabledCount);
                }
                
                _logger.LogDebug("Returned SIPClient to pool (WebRTC bridging: {HasBridging})", sipClient.EnableWebRtcBridging);
            } finally {
                _poolAccessSemaphore.Release();
            }
        }
    }
}
        