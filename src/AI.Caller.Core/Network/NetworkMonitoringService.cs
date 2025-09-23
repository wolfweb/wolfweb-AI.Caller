using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net;

namespace AI.Caller.Core.Network {
    /// <summary>
    /// 网络监控服务实现
    /// </summary>
    public class NetworkMonitoringService : INetworkMonitoringService {
        private readonly ILogger<NetworkMonitoringService> _logger;
        private readonly ConcurrentDictionary<string, ClientNetworkStatus> _clientStatuses;
        private readonly ConcurrentDictionary<string, object> _registeredClients;
        private readonly Timer? _monitoringTimer;
        private readonly object _lockObject = new();

        private NetworkStatus _currentNetworkStatus;
        private NetworkStatus? _previousNetworkStatus;
        private NetworkMonitoringStats _stats;
        private bool _isMonitoring;
        private bool _disposed;
        private DateTime? _lastConnectionLostTime;

        // 监控配置
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _clientTimeoutDuration = TimeSpan.FromMinutes(5);
        private readonly string[] _testHosts = { "8.8.8.8", "1.1.1.1", "208.67.222.222" };

        public event EventHandler<NetworkStatusEventArgs>? NetworkStatusChanged;
        public event EventHandler<NetworkConnectionLostEventArgs>? NetworkConnectionLost;
        public event EventHandler<NetworkConnectionRestoredEventArgs>? NetworkConnectionRestored;
        public event EventHandler<NetworkQualityChangedEventArgs>? NetworkQualityChanged;

        public bool IsMonitoring => _isMonitoring && !_disposed;

        public NetworkMonitoringService(ILogger<NetworkMonitoringService> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clientStatuses = new ConcurrentDictionary<string, ClientNetworkStatus>();
            _registeredClients = new ConcurrentDictionary<string, object>();

            _currentNetworkStatus = new NetworkStatus {
                IsConnected = false,
                NetworkType = NetworkType.Unknown,
                Quality = NetworkQuality.Unknown,
                LastChecked = DateTime.UtcNow
            };

            _stats = new NetworkMonitoringStats {
                MonitoringStarted = DateTime.UtcNow
            };

            _monitoringTimer = new Timer(OnMonitoringTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            _logger.LogInformation("Network monitoring service initialized");
        }

        public void RegisterSipClient(string clientId, object sipClient) {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("Client ID cannot be null or empty", nameof(clientId));

            if (sipClient == null)
                throw new ArgumentNullException(nameof(sipClient));

            lock (_lockObject) {
                _registeredClients[clientId] = sipClient;

                var clientStatus = new ClientNetworkStatus {
                    ClientId = clientId,
                    ClientType = sipClient.GetType().Name,
                    IsOnline = false,
                    ConnectionStatus = ConnectionStatus.Disconnected,
                    LastActivity = DateTime.UtcNow
                };

                _clientStatuses[clientId] = clientStatus;
                _stats.RegisteredClientsCount = _registeredClients.Count;

                _logger.LogInformation("Registered SIP client for monitoring: {ClientId} ({ClientType})",
                    clientId, clientStatus.ClientType);
            }
        }

        public void UnregisterSipClient(string clientId) {
            if (string.IsNullOrEmpty(clientId))
                return;

            lock (_lockObject) {
                _registeredClients.TryRemove(clientId, out _);
                _clientStatuses.TryRemove(clientId, out _);
                _stats.RegisteredClientsCount = _registeredClients.Count;

                _logger.LogInformation("Unregistered SIP client from monitoring: {ClientId}", clientId);
            }
        }

        public NetworkStatus GetCurrentNetworkStatus() {
            return _currentNetworkStatus;
        }

        public ClientNetworkStatus? GetClientNetworkStatus(string clientId) {
            return _clientStatuses.TryGetValue(clientId, out var status) ? status : null;
        }

        public IReadOnlyDictionary<string, ClientNetworkStatus> GetAllClientNetworkStatus() {
            return _clientStatuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public async Task StartMonitoringAsync() {
            if (_isMonitoring) {
                _logger.LogWarning("Network monitoring is already running");
                return;
            }

            _logger.LogInformation("Starting network monitoring...");

            try {
                _currentNetworkStatus = await CheckNetworkStatusAsync();
                _stats.MonitoringStarted = DateTime.UtcNow;
                _stats.LastCheckTime = DateTime.UtcNow;

                _monitoringTimer?.Change(_checkInterval, _checkInterval);
                _isMonitoring = true;

                _logger.LogInformation("Network monitoring started successfully. Check interval: {Interval}s",
                    _checkInterval.TotalSeconds);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to start network monitoring: {Message}", ex.Message);
                throw;
            }
        }

        public async Task StopMonitoringAsync() {
            if (!_isMonitoring) {
                _logger.LogWarning("Network monitoring is not running");
                return;
            }

            _logger.LogInformation("Stopping network monitoring...");

            try {
                _monitoringTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _isMonitoring = false;

                _logger.LogInformation("Network monitoring stopped successfully");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error stopping network monitoring: {Message}", ex.Message);
            }

            await Task.CompletedTask;
        }

        public async Task<NetworkStatus> CheckNetworkStatusAsync() {
            var networkStatus = new NetworkStatus {
                LastChecked = DateTime.UtcNow,
                NetworkType = DetermineNetworkType(),
                Issues = new List<NetworkIssue>()
            };

            try {
                _stats.TotalChecks++;

                networkStatus.IsConnected = NetworkInterface.GetIsNetworkAvailable();

                if (networkStatus.IsConnected) {
                    var pingResults = await PerformPingTestsAsync();
                    networkStatus.LatencyMs = pingResults.AverageLatency;
                    networkStatus.PacketLossRate = pingResults.PacketLossRate;

                    networkStatus.BandwidthKbps = EstimateBandwidth(networkStatus.NetworkType);

                    networkStatus.Quality = DetermineNetworkQuality(networkStatus);

                    DetectNetworkIssues(networkStatus);

                    _stats.SuccessfulChecks++;
                } else {
                    networkStatus.Quality = NetworkQuality.Disconnected;
                    networkStatus.Issues.Add(new NetworkIssue {
                        Type = NetworkIssueType.ConnectionLost,
                        Description = "Network connection is not available",
                        Severity = NetworkIssueSeverity.Critical,
                        OccurredAt = DateTime.UtcNow
                    });
                }

                _stats.LastCheckTime = DateTime.UtcNow;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error checking network status: {Message}", ex.Message);
                _stats.FailedChecks++;

                networkStatus.IsConnected = false;
                networkStatus.Quality = NetworkQuality.Unknown;
                networkStatus.Issues.Add(new NetworkIssue {
                    Type = NetworkIssueType.ServerUnreachable,
                    Description = $"Network check failed: {ex.Message}",
                    Severity = NetworkIssueSeverity.Error,
                    OccurredAt = DateTime.UtcNow
                });
            }

            return networkStatus;
        }

        public NetworkMonitoringStats GetMonitoringStats() {
            lock (_lockObject) {
                _stats.OnlineClientsCount = _clientStatuses.Values.Count(c => c.IsOnline);
                _stats.TotalIssuesDetected = _currentNetworkStatus.Issues.Count +
                    _clientStatuses.Values.Sum(c => c.Issues.Count);
                _stats.ResolvedIssuesCount = _currentNetworkStatus.Issues.Count(i => i.IsResolved) +
                    _clientStatuses.Values.Sum(c => c.Issues.Count(i => i.IsResolved));

                if (_stats.TotalChecks > 1) {
                    var totalTime = DateTime.UtcNow - _stats.MonitoringStarted;
                    _stats.AverageCheckIntervalMs = totalTime.TotalMilliseconds / _stats.TotalChecks;
                }

                return _stats;
            }
        }

        private async void OnMonitoringTimerElapsed(object? state) {
            if (_disposed || !_isMonitoring)
                return;

            try {
                var newNetworkStatus = await CheckNetworkStatusAsync();
                await UpdateNetworkStatus(newNetworkStatus);

                await UpdateClientStatuses();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during network monitoring check: {Message}", ex.Message);
            }
        }

        private async Task UpdateNetworkStatus(NetworkStatus newStatus) {
            var previousStatus = _currentNetworkStatus;
            _currentNetworkStatus = newStatus;

            if (HasNetworkStatusChanged(newStatus, previousStatus)) {
                _logger.LogInformation("Network status changed: {NewStatus}", newStatus);

                NetworkStatusChanged?.Invoke(this, new NetworkStatusEventArgs(newStatus, previousStatus));

                await CheckConnectionChanges(newStatus, previousStatus);

                if (newStatus.Quality != previousStatus.Quality) {
                    NetworkQualityChanged?.Invoke(this, new NetworkQualityChangedEventArgs(
                        newStatus.Quality, previousStatus.Quality, newStatus));
                }
            }

            _previousNetworkStatus = previousStatus;
        }

        private async Task CheckConnectionChanges(NetworkStatus newStatus, NetworkStatus previousStatus) {
            if (previousStatus.IsConnected && !newStatus.IsConnected) {
                _lastConnectionLostTime = DateTime.UtcNow;
                var affectedClients = _clientStatuses.Keys.ToList();

                _logger.LogWarning("Network connection lost. Affected clients: {ClientCount}", affectedClients.Count);

                NetworkConnectionLost?.Invoke(this, new NetworkConnectionLostEventArgs(
                    previousStatus, "Network connectivity lost", affectedClients));
            }
            else if (!previousStatus.IsConnected && newStatus.IsConnected) {
                var outageDuration = _lastConnectionLostTime.HasValue ?
                    DateTime.UtcNow - _lastConnectionLostTime.Value :
                    TimeSpan.Zero;

                var restoredClients = _clientStatuses.Keys.ToList();

                _logger.LogInformation("Network connection restored after {Duration}. Restored clients: {ClientCount}",
                    outageDuration, restoredClients.Count);

                NetworkConnectionRestored?.Invoke(this, new NetworkConnectionRestoredEventArgs(
                    newStatus, outageDuration, restoredClients));

                _lastConnectionLostTime = null;
            }

            await Task.CompletedTask;
        }

        private async Task UpdateClientStatuses() {
            var currentTime = DateTime.UtcNow;

            foreach (var kvp in _clientStatuses.ToList()) {
                var clientId = kvp.Key;
                var clientStatus = kvp.Value;

                if (currentTime - clientStatus.LastActivity > _clientTimeoutDuration) {
                    if (clientStatus.IsOnline) {
                        clientStatus.IsOnline = false;
                        clientStatus.ConnectionStatus = ConnectionStatus.Timeout;

                        _logger.LogWarning("Client {ClientId} timed out", clientId);
                    }
                }

                UpdateClientStats(clientStatus);
            }

            await Task.CompletedTask;
        }

        private void UpdateClientStats(ClientNetworkStatus clientStatus) {
            clientStatus.Stats.LastUpdated = DateTime.UtcNow;
        }

        private async Task<PingTestResult> PerformPingTestsAsync() {
            var results = new List<PingReply>();
            var ping = new Ping();

            foreach (var host in _testHosts) {
                try {
                    var reply = await ping.SendPingAsync(host, 5000);
                    results.Add(reply);
                } catch (Exception ex) {
                    _logger.LogDebug("Ping to {Host} failed: {Message}", host, ex.Message);
                }
            }

            if (results.Count == 0) {
                return new PingTestResult { AverageLatency = -1, PacketLossRate = 100.0 };
            }

            var successfulPings = results.Where(r => r.Status == IPStatus.Success).ToList();
            var averageLatency = successfulPings.Any() ?
                (int)successfulPings.Average(r => r.RoundtripTime) : -1;
            var packetLossRate = (double)(results.Count - successfulPings.Count) / results.Count * 100;

            return new PingTestResult {
                AverageLatency = averageLatency,
                PacketLossRate = packetLossRate
            };
        }

        private static NetworkType DetermineNetworkType() {
            try {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .ToList();

                if (interfaces.Any(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    return NetworkType.Ethernet;

                if (interfaces.Any(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    return NetworkType.WiFi;

                if (interfaces.Any(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ppp))
                    return NetworkType.VPN;

                if (interfaces.Any(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Loopback))
                    return NetworkType.Loopback;

                return NetworkType.Unknown;
            } catch {
                return NetworkType.Unknown;
            }
        }

        private static NetworkQuality DetermineNetworkQuality(NetworkStatus status) {
            if (!status.IsConnected)
                return NetworkQuality.Disconnected;

            if (status.LatencyMs < 0)
                return NetworkQuality.Unknown;

            if (status.LatencyMs <= 50 && status.PacketLossRate <= 1.0)
                return NetworkQuality.Excellent;

            if (status.LatencyMs <= 100 && status.PacketLossRate <= 3.0)
                return NetworkQuality.Good;

            if (status.LatencyMs <= 200 && status.PacketLossRate <= 5.0)
                return NetworkQuality.Fair;

            return NetworkQuality.Poor;
        }

        private static int EstimateBandwidth(NetworkType networkType) {
            return networkType switch {
                NetworkType.Ethernet => 100000, // 100 Mbps
                NetworkType.WiFi => 50000,      // 50 Mbps
                NetworkType.Cellular => 10000,  // 10 Mbps
                NetworkType.VPN => 20000,       // 20 Mbps
                _ => 1000                       // 1 Mbps
            };
        }

        private static void DetectNetworkIssues(NetworkStatus status) {
            if (status.LatencyMs > 500) {
                status.Issues.Add(new NetworkIssue {
                    Type = NetworkIssueType.HighLatency,
                    Description = $"High latency detected: {status.LatencyMs}ms",
                    Severity = status.LatencyMs > 1000 ? NetworkIssueSeverity.Critical : NetworkIssueSeverity.Warning,
                    OccurredAt = DateTime.UtcNow
                });
            }

            if (status.PacketLossRate > 5.0) {
                status.Issues.Add(new NetworkIssue {
                    Type = NetworkIssueType.PacketLoss,
                    Description = $"High packet loss detected: {status.PacketLossRate:F1}%",
                    Severity = status.PacketLossRate > 10.0 ? NetworkIssueSeverity.Critical : NetworkIssueSeverity.Warning,
                    OccurredAt = DateTime.UtcNow
                });
            }

            if (status.BandwidthKbps < 1000) {
                status.Issues.Add(new NetworkIssue {
                    Type = NetworkIssueType.LowBandwidth,
                    Description = $"Low bandwidth detected: {status.BandwidthKbps}Kbps",
                    Severity = NetworkIssueSeverity.Warning,
                    OccurredAt = DateTime.UtcNow
                });
            }
        }

        private static bool HasNetworkStatusChanged(NetworkStatus newStatus, NetworkStatus previousStatus) {
            return newStatus.IsConnected != previousStatus.IsConnected ||
                   newStatus.NetworkType != previousStatus.NetworkType ||
                   newStatus.Quality != previousStatus.Quality ||
                   Math.Abs(newStatus.LatencyMs - previousStatus.LatencyMs) > 100 ||
                   Math.Abs(newStatus.PacketLossRate - previousStatus.PacketLossRate) > 2.0;
        }

        public void Dispose() {
            if (_disposed)
                return;

            _disposed = true;

            try {
                _monitoringTimer?.Dispose();
                _isMonitoring = false;

                _logger.LogInformation("Network monitoring service disposed");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error disposing network monitoring service: {Message}", ex.Message);
            }
        }

        private class PingTestResult {
            public int AverageLatency { get; set; }
            public double PacketLossRate { get; set; }
        }
    }
}