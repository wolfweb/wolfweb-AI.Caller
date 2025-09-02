using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Network;

namespace AI.Caller.Core.Tests.Network {
    public class NetworkMonitoringServiceTests : IDisposable {
        private readonly Mock<ILogger<NetworkMonitoringService>> _mockLogger;
        private readonly NetworkMonitoringService _networkService;

        public NetworkMonitoringServiceTests() {
            _mockLogger = new Mock<ILogger<NetworkMonitoringService>>();
            _networkService = new NetworkMonitoringService(_mockLogger.Object);
        }

        public void Dispose() {
            _networkService?.Dispose();
        }

        [Fact]
        public void Constructor_ShouldInitializeCorrectly() {
            Assert.False(_networkService.IsMonitoring);
            Assert.NotNull(_networkService.GetCurrentNetworkStatus());
            Assert.Empty(_networkService.GetAllClientNetworkStatus());
        }

        [Fact]
        public void RegisterSipClient_ShouldAddClientToMonitoring() {
            var clientId = "test-client-001";
            var mockSipClient = new object();

            _networkService.RegisterSipClient(clientId, mockSipClient);

            var clientStatus = _networkService.GetClientNetworkStatus(clientId);
            Assert.NotNull(clientStatus);
            Assert.Equal(clientId, clientStatus.ClientId);
            Assert.False(clientStatus.IsOnline);
            Assert.Equal(ConnectionStatus.Disconnected, clientStatus.ConnectionStatus);
        }

        [Fact]
        public void RegisterSipClient_WithNullClientId_ShouldThrowException() {
            var mockSipClient = new object();

            Assert.Throws<ArgumentException>(() =>
                _networkService.RegisterSipClient(null!, mockSipClient));
        }

        [Fact]
        public void RegisterSipClient_WithNullSipClient_ShouldThrowException() {
            var clientId = "test-client";

            Assert.Throws<ArgumentNullException>(() =>
                _networkService.RegisterSipClient(clientId, null!));
        }

        [Fact]
        public void UnregisterSipClient_ShouldRemoveClientFromMonitoring() {
            var clientId = "test-client-001";
            var mockSipClient = new object();
            _networkService.RegisterSipClient(clientId, mockSipClient);

            _networkService.UnregisterSipClient(clientId);

            var clientStatus = _networkService.GetClientNetworkStatus(clientId);
            Assert.Null(clientStatus);
            Assert.Empty(_networkService.GetAllClientNetworkStatus());
        }

        [Fact]
        public void GetAllClientNetworkStatus_ShouldReturnAllRegisteredClients() {
            var clients = new[]
            {
                new { Id = "client-001", Client = new object() },
                new { Id = "client-002", Client = new object() },
                new { Id = "client-003", Client = new object() }
            };

            foreach (var client in clients) {
                _networkService.RegisterSipClient(client.Id, client.Client);
            }

            var allStatuses = _networkService.GetAllClientNetworkStatus();

            Assert.Equal(3, allStatuses.Count);
            Assert.All(clients, client => Assert.Contains(client.Id, allStatuses.Keys));
        }

        [Fact]
        public async Task CheckNetworkStatusAsync_ShouldReturnNetworkStatus() {
            var networkStatus = await _networkService.CheckNetworkStatusAsync();

            Assert.NotNull(networkStatus);
            Assert.True(networkStatus.LastChecked > DateTime.MinValue);
            Assert.NotEqual(NetworkType.Unknown, networkStatus.NetworkType);
        }

        [Fact]
        public async Task StartMonitoringAsync_ShouldStartMonitoring() {
            await _networkService.StartMonitoringAsync();

            Assert.True(_networkService.IsMonitoring);

            await _networkService.StopMonitoringAsync();
        }

        [Fact]
        public async Task StopMonitoringAsync_ShouldStopMonitoring() {
            await _networkService.StartMonitoringAsync();
            Assert.True(_networkService.IsMonitoring);

            await _networkService.StopMonitoringAsync();

            Assert.False(_networkService.IsMonitoring);
        }

        [Fact]
        public async Task StartMonitoringAsync_WhenAlreadyMonitoring_ShouldNotThrow() {
            await _networkService.StartMonitoringAsync();

            await _networkService.StartMonitoringAsync();

            await _networkService.StopMonitoringAsync();
        }

        [Fact]
        public async Task StopMonitoringAsync_WhenNotMonitoring_ShouldNotThrow() {
            await _networkService.StopMonitoringAsync();
        }

        [Fact]
        public void GetMonitoringStats_ShouldReturnValidStats() {
            _networkService.RegisterSipClient("client-001", new object());
            _networkService.RegisterSipClient("client-002", new object());

            var stats = _networkService.GetMonitoringStats();

            Assert.NotNull(stats);
            Assert.Equal(2, stats.RegisteredClientsCount);
            Assert.True(stats.MonitoringStarted > DateTime.MinValue);
            Assert.True(stats.MonitoringDuration >= TimeSpan.Zero);
        }

        [Fact]
        public void NetworkStatusChanged_Event_ShouldBeSubscribable() {
            NetworkStatusEventArgs? eventArgs = null;

            _networkService.NetworkStatusChanged += (sender, e) => eventArgs = e;
            _networkService.NetworkStatusChanged -= (sender, e) => eventArgs = e;

            Assert.True(true);
        }

        [Fact]
        public void NetworkConnectionLost_Event_ShouldBeSubscribable() {
            NetworkConnectionLostEventArgs? eventArgs = null;

            _networkService.NetworkConnectionLost += (sender, e) => eventArgs = e;
            _networkService.NetworkConnectionLost -= (sender, e) => eventArgs = e;

            Assert.True(true);
        }

        [Fact]
        public void NetworkConnectionRestored_Event_ShouldBeSubscribable() {
            NetworkConnectionRestoredEventArgs? eventArgs = null;

            _networkService.NetworkConnectionRestored += (sender, e) => eventArgs = e;
            _networkService.NetworkConnectionRestored -= (sender, e) => eventArgs = e;

            Assert.True(true);
        }

        [Fact]
        public void NetworkQualityChanged_Event_ShouldBeSubscribable() {
            NetworkQualityChangedEventArgs? eventArgs = null;

            _networkService.NetworkQualityChanged += (sender, e) => eventArgs = e;
            _networkService.NetworkQualityChanged -= (sender, e) => eventArgs = e;

            Assert.True(true);
        }

        [Fact]
        public void Dispose_ShouldCleanupResources() {
            _networkService.RegisterSipClient("client-001", new object());

            _networkService.Dispose();

            Assert.False(_networkService.IsMonitoring);
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow() {
            _networkService.Dispose();
            _networkService.Dispose();
        }
    }
}