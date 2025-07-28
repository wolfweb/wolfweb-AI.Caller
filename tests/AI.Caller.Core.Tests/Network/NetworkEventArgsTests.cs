using Xunit;
using AI.Caller.Core.Network;

namespace AI.Caller.Core.Tests.Network
{
    /// <summary>
    /// 网络事件参数测试
    /// </summary>
    public class NetworkEventArgsTests
    {
        [Fact]
        public void NetworkStatusEventArgs_WithImprovement_ShouldDetectCorrectly()
        {
            // Arrange
            var previousStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Fair,
                LatencyMs = 200,
                PacketLossRate = 5.0
            };

            var currentStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good,
                LatencyMs = 100,
                PacketLossRate = 2.0
            };

            // Act
            var eventArgs = new NetworkStatusEventArgs(currentStatus, previousStatus);

            // Assert
            Assert.True(eventArgs.IsImprovement);
            Assert.False(eventArgs.IsDegradation);
            Assert.Equal(currentStatus, eventArgs.CurrentStatus);
            Assert.Equal(previousStatus, eventArgs.PreviousStatus);
        }

        [Fact]
        public void NetworkStatusEventArgs_WithDegradation_ShouldDetectCorrectly()
        {
            // Arrange
            var previousStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good,
                LatencyMs = 50,
                PacketLossRate = 1.0
            };

            var currentStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Poor,
                LatencyMs = 300,
                PacketLossRate = 8.0
            };

            // Act
            var eventArgs = new NetworkStatusEventArgs(currentStatus, previousStatus);

            // Assert
            Assert.False(eventArgs.IsImprovement);
            Assert.True(eventArgs.IsDegradation);
        }

        [Fact]
        public void NetworkStatusEventArgs_WithConnectionLoss_ShouldDetectDegradation()
        {
            // Arrange
            var previousStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good
            };

            var currentStatus = new NetworkStatus
            {
                IsConnected = false,
                Quality = NetworkQuality.Disconnected
            };

            // Act
            var eventArgs = new NetworkStatusEventArgs(currentStatus, previousStatus);

            // Assert
            Assert.False(eventArgs.IsImprovement);
            Assert.True(eventArgs.IsDegradation);
        }

        [Fact]
        public void NetworkStatusEventArgs_WithConnectionRestored_ShouldDetectImprovement()
        {
            // Arrange
            var previousStatus = new NetworkStatus
            {
                IsConnected = false,
                Quality = NetworkQuality.Disconnected
            };

            var currentStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good
            };

            // Act
            var eventArgs = new NetworkStatusEventArgs(currentStatus, previousStatus);

            // Assert
            Assert.True(eventArgs.IsImprovement);
            Assert.False(eventArgs.IsDegradation);
        }

        [Fact]
        public void NetworkStatusEventArgs_WithNullPreviousStatus_ShouldNotDetectChanges()
        {
            // Arrange
            var currentStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good
            };

            // Act
            var eventArgs = new NetworkStatusEventArgs(currentStatus, null);

            // Assert
            Assert.False(eventArgs.IsImprovement);
            Assert.False(eventArgs.IsDegradation);
            Assert.Equal(currentStatus, eventArgs.CurrentStatus);
            Assert.Null(eventArgs.PreviousStatus);
        }

        [Fact]
        public void NetworkConnectionLostEventArgs_ShouldInitializeCorrectly()
        {
            // Arrange
            var lastKnownStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good
            };
            var reason = "Network cable disconnected";
            var affectedClients = new[] { "client-001", "client-002" };

            // Act
            var eventArgs = new NetworkConnectionLostEventArgs(lastKnownStatus, reason, affectedClients);

            // Assert
            Assert.Equal(lastKnownStatus, eventArgs.LastKnownStatus);
            Assert.Equal(reason, eventArgs.Reason);
            Assert.Equal(2, eventArgs.AffectedClientIds.Count);
            Assert.Contains("client-001", eventArgs.AffectedClientIds);
            Assert.Contains("client-002", eventArgs.AffectedClientIds);
            Assert.True(eventArgs.LostAt > DateTime.MinValue);
        }

        [Fact]
        public void NetworkConnectionLostEventArgs_WithNullAffectedClients_ShouldInitializeEmptyList()
        {
            // Arrange
            var lastKnownStatus = new NetworkStatus { IsConnected = true };
            var reason = "Network timeout";

            // Act
            var eventArgs = new NetworkConnectionLostEventArgs(lastKnownStatus, reason, null);

            // Assert
            Assert.Empty(eventArgs.AffectedClientIds);
        }

        [Fact]
        public void NetworkConnectionRestoredEventArgs_ShouldInitializeCorrectly()
        {
            // Arrange
            var currentStatus = new NetworkStatus
            {
                IsConnected = true,
                Quality = NetworkQuality.Good
            };
            var outageDuration = TimeSpan.FromMinutes(5);
            var restoredClients = new[] { "client-001", "client-002" };

            // Act
            var eventArgs = new NetworkConnectionRestoredEventArgs(currentStatus, outageDuration, restoredClients);

            // Assert
            Assert.Equal(currentStatus, eventArgs.CurrentStatus);
            Assert.Equal(outageDuration, eventArgs.OutageDuration);
            Assert.Equal(2, eventArgs.RestoredClientIds.Count);
            Assert.Contains("client-001", eventArgs.RestoredClientIds);
            Assert.Contains("client-002", eventArgs.RestoredClientIds);
            Assert.True(eventArgs.RestoredAt > DateTime.MinValue);
        }

        [Fact]
        public void NetworkQualityChangedEventArgs_WithImprovement_ShouldDetectCorrectly()
        {
            // Arrange
            var previousQuality = NetworkQuality.Fair;
            var currentQuality = NetworkQuality.Good;
            var currentStatus = new NetworkStatus { Quality = currentQuality };

            // Act
            var eventArgs = new NetworkQualityChangedEventArgs(currentQuality, previousQuality, currentStatus);

            // Assert
            Assert.True(eventArgs.IsImprovement);
            Assert.False(eventArgs.IsDegradation);
            Assert.Equal(currentQuality, eventArgs.CurrentQuality);
            Assert.Equal(previousQuality, eventArgs.PreviousQuality);
            Assert.Equal(currentStatus, eventArgs.CurrentStatus);
        }

        [Fact]
        public void NetworkQualityChangedEventArgs_WithDegradation_ShouldDetectCorrectly()
        {
            // Arrange
            var previousQuality = NetworkQuality.Excellent;
            var currentQuality = NetworkQuality.Poor;
            var currentStatus = new NetworkStatus { Quality = currentQuality };

            // Act
            var eventArgs = new NetworkQualityChangedEventArgs(currentQuality, previousQuality, currentStatus);

            // Assert
            Assert.False(eventArgs.IsImprovement);
            Assert.True(eventArgs.IsDegradation);
        }

        [Fact]
        public void NetworkQualityChangedEventArgs_WithSameQuality_ShouldNotDetectChanges()
        {
            // Arrange
            var quality = NetworkQuality.Good;
            var currentStatus = new NetworkStatus { Quality = quality };

            // Act
            var eventArgs = new NetworkQualityChangedEventArgs(quality, quality, currentStatus);

            // Assert
            Assert.False(eventArgs.IsImprovement);
            Assert.False(eventArgs.IsDegradation);
        }

        [Fact]
        public void ClientNetworkStatusChangedEventArgs_WithConnectionEstablished_ShouldDetectCorrectly()
        {
            // Arrange
            var clientId = "test-client";
            var previousStatus = new ClientNetworkStatus
            {
                ClientId = clientId,
                IsOnline = false,
                ConnectionStatus = ConnectionStatus.Disconnected
            };
            var currentStatus = new ClientNetworkStatus
            {
                ClientId = clientId,
                IsOnline = true,
                ConnectionStatus = ConnectionStatus.Connected
            };

            // Act
            var eventArgs = new ClientNetworkStatusChangedEventArgs(clientId, currentStatus, previousStatus);

            // Assert
            Assert.True(eventArgs.IsConnectionEstablished);
            Assert.False(eventArgs.IsConnectionLost);
            Assert.Equal(clientId, eventArgs.ClientId);
            Assert.Equal(currentStatus, eventArgs.CurrentStatus);
            Assert.Equal(previousStatus, eventArgs.PreviousStatus);
        }

        [Fact]
        public void ClientNetworkStatusChangedEventArgs_WithConnectionLost_ShouldDetectCorrectly()
        {
            // Arrange
            var clientId = "test-client";
            var previousStatus = new ClientNetworkStatus
            {
                ClientId = clientId,
                IsOnline = true,
                ConnectionStatus = ConnectionStatus.Connected
            };
            var currentStatus = new ClientNetworkStatus
            {
                ClientId = clientId,
                IsOnline = false,
                ConnectionStatus = ConnectionStatus.Disconnected
            };

            // Act
            var eventArgs = new ClientNetworkStatusChangedEventArgs(clientId, currentStatus, previousStatus);

            // Assert
            Assert.False(eventArgs.IsConnectionEstablished);
            Assert.True(eventArgs.IsConnectionLost);
        }

        [Fact]
        public void ClientNetworkStatusChangedEventArgs_WithNullPreviousStatus_ShouldNotDetectChanges()
        {
            // Arrange
            var clientId = "test-client";
            var currentStatus = new ClientNetworkStatus
            {
                ClientId = clientId,
                IsOnline = true,
                ConnectionStatus = ConnectionStatus.Connected
            };

            // Act
            var eventArgs = new ClientNetworkStatusChangedEventArgs(clientId, currentStatus, null);

            // Assert
            Assert.False(eventArgs.IsConnectionEstablished);
            Assert.False(eventArgs.IsConnectionLost);
            Assert.Equal(clientId, eventArgs.ClientId);
            Assert.Equal(currentStatus, eventArgs.CurrentStatus);
            Assert.Null(eventArgs.PreviousStatus);
        }

        [Fact]
        public void NetworkStatusEventArgs_ToString_ShouldFormatCorrectly()
        {
            // Arrange
            var currentStatus = new NetworkStatus { IsConnected = true, Quality = NetworkQuality.Good };
            var previousStatus = new NetworkStatus { IsConnected = true, Quality = NetworkQuality.Fair };
            var eventArgs = new NetworkStatusEventArgs(currentStatus, previousStatus);

            // Act
            var formatted = eventArgs.ToString();

            // Assert
            Assert.Contains("Network status", formatted);
            Assert.Contains("Improved", formatted);
        }

        [Fact]
        public void NetworkConnectionLostEventArgs_ToString_ShouldFormatCorrectly()
        {
            // Arrange
            var lastKnownStatus = new NetworkStatus { IsConnected = true };
            var reason = "Cable disconnected";
            var affectedClients = new[] { "client-001", "client-002" };
            var eventArgs = new NetworkConnectionLostEventArgs(lastKnownStatus, reason, affectedClients);

            // Act
            var formatted = eventArgs.ToString();

            // Assert
            Assert.Contains("Network connection lost", formatted);
            Assert.Contains(reason, formatted);
            Assert.Contains("Affected clients: 2", formatted);
        }

        [Fact]
        public void NetworkConnectionRestoredEventArgs_ToString_ShouldFormatCorrectly()
        {
            // Arrange
            var currentStatus = new NetworkStatus { IsConnected = true };
            var outageDuration = TimeSpan.FromMinutes(5);
            var restoredClients = new[] { "client-001" };
            var eventArgs = new NetworkConnectionRestoredEventArgs(currentStatus, outageDuration, restoredClients);

            // Act
            var formatted = eventArgs.ToString();

            // Assert
            Assert.Contains("Network connection restored", formatted);
            Assert.Contains("05:00", formatted);
            Assert.Contains("Restored clients: 1", formatted);
        }
    }
}