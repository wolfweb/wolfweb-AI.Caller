using AI.Caller.Core.Models;
using AI.Caller.Core.Network;
using AI.Caller.Core.Media;
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
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly INetworkMonitoringService _networkMonitoringService;

        private readonly ConcurrentDictionary<ClientPoolKey, ConcurrentQueue<SIPClient>> _pool = new();
        private readonly TimeSpan _acquireTimeout = TimeSpan.FromSeconds(30);

        private int _totalClientsCreated;

        public SIPClientPoolManager(
        ILogger<SIPClientPoolManager> logger,
        IOptions<WebRTCSettings> webRTCSettings, 
        SIPTransportManager sipTransportManager,
        IServiceScopeFactory serviceScopeFactory,
        INetworkMonitoringService networkMonitoringService
        ) {
            _logger                   = logger;
            _webRTCSettings           = webRTCSettings.Value;
            _serviceScopeFactory      = serviceScopeFactory;
            _sipTransportManager      = sipTransportManager;
            _networkMonitoringService = networkMonitoringService;
        }

        public async Task<SIPClientHandle?> AcquireClientAsync(string sipServer, bool enableWebRtcBridging, SipRoutingInfo? routingInfo) {
            var key = new ClientPoolKey(sipServer, enableWebRtcBridging, routingInfo);
            SIPClient? client = null;

            if (_pool.TryGetValue(key, out var queue)) {
                if (queue.TryDequeue(out client)) {
                    if (IsClientHealthy(client)) {
                        _logger.LogDebug("Reused SIPClient from pool. Key: {Key}", key);
                        return new SIPClientHandle(this, client);
                    } else {
                        _logger.LogWarning("Discarding unhealthy client from pool. Key: {Key}", key);
                        CleanupClient(client);
                        client = null;
                    }
                }
            }
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                var clientLogger = scope.ServiceProvider.GetRequiredService<ILogger<SIPClient>>();
                
                // Get codec components from DI container
                var codecFactory = scope.ServiceProvider.GetService<AudioCodecFactory>();
                var codecHealthMonitor = scope.ServiceProvider.GetService<CodecHealthMonitor>();

                client = new SIPClient(
                    sipServer,
                    clientLogger,
                    _sipTransportManager.SIPTransport!,
                    _webRTCSettings,
                    _networkMonitoringService,
                    enableWebRtcBridging,
                    routingInfo,
                    codecFactory,
                    codecHealthMonitor
                );

                Interlocked.Increment(ref _totalClientsCreated);
                _logger.LogDebug("Created new SIPClient. Total Created: {Count}. Key: {Key}", _totalClientsCreated, key);

                return new SIPClientHandle(this, client);
            } catch (OperationCanceledException) {
                _logger.LogDebug("Client acquisition cancelled for {SIPServer}", sipServer);
                return null;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to acquire client for {SIPServer}", sipServer);
                return null;
            }
        }

        internal void Return(SIPClient sipClient) {
            if (sipClient == null) return;

            sipClient.Reset();

            var key = new ClientPoolKey(
                sipClient.SipServer,
                sipClient.EnableWebRtcBridging,
                sipClient.RoutingInfo
            );

            var queue = _pool.GetOrAdd(key, _ => new ConcurrentQueue<SIPClient>());
            queue.Enqueue(sipClient);
            _logger.LogDebug("Returned SIPClient to pool. Queue Size: {Count}. Key: {Key}", queue.Count, key);            
        }

        private bool IsClientHealthy(SIPClient client) {
            return client != null;
        }

        private void CleanupClient(SIPClient client) {
            try {
                client.Shutdown();
            } catch(Exception ex) {
                _logger.LogError(ex, "Error shutting down unhealthy client");
            }
        }

        internal record ClientPoolKey(string SipServer, bool EnableWebRtc, SipRoutingInfo? RoutingInfo);
    }
}