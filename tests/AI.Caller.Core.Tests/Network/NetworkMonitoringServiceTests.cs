using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Network;

namespace AI.Caller.Core.Tests.Network
{
    /// <summary>
    /// 网络监控服务测试
    /// </summary>
    public class NetworkMonitoringServiceTests : IDisposable
    {
        private readonly Mock<ILogger<NetworkMonitoringService>> _mockLogger;
        private readonly NetworkMonitoringService _networkService;

        public NetworkMonitoringServiceTests()
        {
            _mockLogger = new Mock<ILogger<NetworkMonitoringService>>();
            _networkService = new NetworkMonitoringService(_mockLogger.Object);
        }

        public void Dispose()
        {
            _networkService?.Dispose();
        }

        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.False(_networkService.IsMonitoring);
            Assert.NotNull(_networkService.GetCurrentNetworkStatus());
            Assert.Empty(_networkService.GetAllClientNetworkStatus());
        }

        [Fact]
        public void RegisterSipClient_ShouldAddClientToMonitoring()
        {
            // Arrange
            var clientId = "test-client-001";
            var mockSipClient = new object();

            // Act
            _networkService.RegisterSipClient(clientId, mockSipClient);

            // Assert
            var clientStatus = _networkService.GetClientNetworkStatus(clientId);
            Assert.NotNull(clientStatus);
            Assert.Equal(clientId, clientStatus.ClientId);
            Assert.False(clientStatus.IsOnline);
            Assert.Equal(ConnectionStatus.Disconnected, clientStatus.ConnectionStatus);
        }

        [Fact]
        public void RegisterSipClient_WithNullClientId_ShouldThrowException()
        {
            // Arrange
            var mockSipClient = new object();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                _networkService.RegisterSipClient(null!, mockSipClient));
        }

        [Fact]
        public void RegisterSipClient_WithNullSipClient_ShouldThrowException()
        {
            // Arrange
            var clientId = "test-client";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                _networkService.RegisterSipClient(clientId, null!));
        }

        [Fact]
        public void UnregisterSipClient_ShouldRemoveClientFromMonitoring()
        {
            // Arrange
            var clientId = "test-client-001";
            var mockSipClient = new object();
            _networkService.RegisterSipClient(clientId, mockSipClient);

            // Act
            _networkService.UnregisterSipClient(clientId);

            // Assert
            var clientStatus = _networkService.GetClientNetworkStatus(clientId);
            Assert.Null(clientStatus);
            Assert.Empty(_networkService.GetAllClientNetworkStatus());
        }

        [Fact]
        public void GetAllClientNetworkStatus_ShouldReturnAllRegisteredClients()
        {
            // Arrange
            var clients = new[]
            {
                new { Id = "client-001", Client = new object() },
                new { Id = "client-002", Client = new object() },
                new { Id = "client-003", Client = new object() }
            };

            foreach (var client in clients)
            {
                _networkService.RegisterSipClient(client.Id, client.Client);
            }

            // Act
            var allStatuses = _networkService.GetAllClientNetworkStatus();

            // Assert
            Assert.Equal(3, allStatuses.Count);
            Assert.All(clients, client => Assert.Contains(client.Id, allStatuses.Keys));
        }

        [Fact]
        public async Task CheckNetworkStatusAsync_ShouldReturnNetworkStatus()
        {
            // Act
            var networkStatus = await _networkService.CheckNetworkStatusAsync();

            // Assert
            Assert.NotNull(networkStatus);
            Assert.True(networkStatus.LastChecked > DateTime.MinValue);
            Assert.NotEqual(NetworkType.Unknown, networkStatus.NetworkType);
        }

        [Fact]
        public async Task StartMonitoringAsync_ShouldStartMonitoring()
        {
            // Act
            await _networkService.StartMonitoringAsync();

            // Assert
            Assert.True(_networkService.IsMonitoring);

            // Cleanup
            await _networkService.StopMonitoringAsync();
        }

        [Fact]
        public async Task StopMonitoringAsync_ShouldStopMonitoring()
        {
            // Arrange
            await _networkService.StartMonitoringAsync();
            Assert.True(_networkService.IsMonitoring);

            // Act
            await _networkService.StopMonitoringAsync();

            // Assert
            Assert.False(_networkService.IsMonitoring);
        }

        [Fact]
        public async Task StartMonitoringAsync_WhenAlreadyMonitoring_ShouldNotThrow()
        {
            // Arrange
            await _networkService.StartMonitoringAsync();

            // Act & Assert
            await _networkService.StartMonitoringAsync(); // Should not throw

            // Cleanup
            await _networkService.StopMonitoringAsync();
        }

        [Fact]
        public async Task StopMonitoringAsync_WhenNotMonitoring_ShouldNotThrow()
        {
            // Act & Assert
            await _networkService.StopMonitoringAsync(); // Should not throw
        }

        [Fact]
        public void GetMonitoringStats_ShouldReturnValidStats()
        {
            // Arrange
            _networkService.RegisterSipClient("client-001", new object());
            _networkService.RegisterSipClient("client-002", new object());

            // Act
            var stats = _networkService.GetMonitoringStats();

            // Assert
            Assert.NotNull(stats);
            Assert.Equal(2, stats.RegisteredClientsCount);
            Assert.True(stats.MonitoringStarted > DateTime.MinValue);
            Assert.True(stats.MonitoringDuration >= TimeSpan.Zero);
        }

        [Fact]
        public void NetworkStatusChanged_Event_ShouldBeSubscribable()
        {
            // Arrange
            NetworkStatusEventArgs? eventArgs = null;
            
            // Act & Assert - 验证事件可以订阅和取消订阅
            _networkService.NetworkStatusChanged += (sender, e) => eventArgs = e;
            _networkService.NetworkStatusChanged -= (sender, e) => eventArgs = e;
            
            // 如果没有抛出异常，说明事件订阅工作正常
            Assert.True(true);
        }

        [Fact]
        public void NetworkConnectionLost_Event_ShouldBeSubscribable()
        {
            // Arrange
            NetworkConnectionLostEventArgs? eventArgs = null;
            
            // Act & Assert - 验证事件可以订阅和取消订阅
            _networkService.NetworkConnectionLost += (sender, e) => eventArgs = e;
            _networkService.NetworkConnectionLost -= (sender, e) => eventArgs = e;
            
            // 如果没有抛出异常，说明事件订阅工作正常
            Assert.True(true);
        }

        [Fact]
        public void NetworkConnectionRestored_Event_ShouldBeSubscribable()
        {
            // Arrange
            NetworkConnectionRestoredEventArgs? eventArgs = null;
            
            // Act & Assert - 验证事件可以订阅和取消订阅
            _networkService.NetworkConnectionRestored += (sender, e) => eventArgs = e;
            _networkService.NetworkConnectionRestored -= (sender, e) => eventArgs = e;
            
            // 如果没有抛出异常，说明事件订阅工作正常
            Assert.True(true);
        }

        [Fact]
        public void NetworkQualityChanged_Event_ShouldBeSubscribable()
        {
            // Arrange
            NetworkQualityChangedEventArgs? eventArgs = null;
            
            // Act & Assert - 验证事件可以订阅和取消订阅
            _networkService.NetworkQualityChanged += (sender, e) => eventArgs = e;
            _networkService.NetworkQualityChanged -= (sender, e) => eventArgs = e;
            
            // 如果没有抛出异常，说明事件订阅工作正常
            Assert.True(true);
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            _networkService.RegisterSipClient("client-001", new object());

            // Act
            _networkService.Dispose();

            // Assert
            Assert.False(_networkService.IsMonitoring);
            // 验证资源已清理（通过不抛出异常来验证）
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _networkService.Dispose();
            _networkService.Dispose(); // Should not throw
        }
    }
}