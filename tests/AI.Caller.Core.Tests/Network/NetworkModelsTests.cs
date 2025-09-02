using Xunit;
using AI.Caller.Core.Network;

namespace AI.Caller.Core.Tests.Network {
    public class NetworkModelsTests {
        [Fact]
        public void NetworkStatus_IsHealthy_ShouldReturnCorrectValue() {
            var healthyStatus = new NetworkStatus {
                IsConnected = true,
                Quality = NetworkQuality.Good,
                Issues = new List<NetworkIssue>()
            };

            var unhealthyStatus = new NetworkStatus {
                IsConnected = false,
                Quality = NetworkQuality.Poor,
                Issues = new List<NetworkIssue>
                {
                    new NetworkIssue { Type = NetworkIssueType.ConnectionLost }
                }
            };

            Assert.True(healthyStatus.IsHealthy);
            Assert.False(unhealthyStatus.IsHealthy);
        }

        [Fact]
        public void NetworkStatus_ToString_ShouldFormatCorrectly() {
            var status = new NetworkStatus {
                IsConnected = true,
                NetworkType = NetworkType.WiFi,
                Quality = NetworkQuality.Good,
                LatencyMs = 50,
                PacketLossRate = 1.5,
                BandwidthKbps = 50000
            };

            var formatted = status.ToString();

            Assert.Contains("Connected: True", formatted);
            Assert.Contains("Type: WiFi", formatted);
            Assert.Contains("Quality: Good", formatted);
            Assert.Contains("Latency: 50ms", formatted);
            Assert.Contains("Loss: 1.5%", formatted);
            Assert.Contains("Bandwidth: 50000Kbps", formatted);
        }

        [Fact]
        public void ClientNetworkStatus_ConnectionDuration_ShouldCalculateCorrectly() {
            // Arrange
            var connectedAt = DateTime.UtcNow.AddMinutes(-30);
            var clientStatus = new ClientNetworkStatus {
                ClientId = "test-client",
                ConnectedAt = connectedAt
            };

            // Act
            var duration = clientStatus.ConnectionDuration;

            // Assert
            Assert.NotNull(duration);
            Assert.True(duration.Value.TotalMinutes >= 29); // Allow for execution time
            Assert.True(duration.Value.TotalMinutes <= 31); // Allow for execution time
        }

        [Fact]
        public void ClientNetworkStatus_ConnectionDuration_WithoutConnectedAt_ShouldReturnNull() {
            // Arrange
            var clientStatus = new ClientNetworkStatus {
                ClientId = "test-client",
                ConnectedAt = null
            };

            // Act
            var duration = clientStatus.ConnectionDuration;

            // Assert
            Assert.Null(duration);
        }

        [Fact]
        public void ClientNetworkStats_PacketLossRate_ShouldCalculateCorrectly() {
            // Arrange
            var stats = new ClientNetworkStats {
                PacketsSent = 1000,
                PacketsLost = 50
            };

            // Act
            var lossRate = stats.PacketLossRate;

            // Assert
            Assert.Equal(5.0, lossRate);
        }

        [Fact]
        public void ClientNetworkStats_PacketLossRate_WithZeroSent_ShouldReturnZero() {
            // Arrange
            var stats = new ClientNetworkStats {
                PacketsSent = 0,
                PacketsLost = 10
            };

            // Act
            var lossRate = stats.PacketLossRate;

            // Assert
            Assert.Equal(0.0, lossRate);
        }

        [Fact]
        public void NetworkIssue_Duration_ShouldCalculateCorrectly() {
            // Arrange
            var occurredAt = DateTime.UtcNow.AddMinutes(-10);
            var resolvedAt = DateTime.UtcNow.AddMinutes(-5);

            var resolvedIssue = new NetworkIssue {
                OccurredAt = occurredAt,
                ResolvedAt = resolvedAt,
                IsResolved = true
            };

            var ongoingIssue = new NetworkIssue {
                OccurredAt = occurredAt,
                IsResolved = false
            };

            // Act
            var resolvedDuration = resolvedIssue.Duration;
            var ongoingDuration = ongoingIssue.Duration;

            // Assert
            Assert.Equal(TimeSpan.FromMinutes(5), resolvedDuration);
            Assert.True(ongoingDuration.TotalMinutes >= 9); // Allow for execution time
            Assert.True(ongoingDuration.TotalMinutes <= 11); // Allow for execution time
        }

        [Fact]
        public void NetworkMonitoringStats_SuccessRate_ShouldCalculateCorrectly() {
            // Arrange
            var stats = new NetworkMonitoringStats {
                TotalChecks = 100,
                SuccessfulChecks = 95,
                FailedChecks = 5
            };

            // Act
            var successRate = stats.SuccessRate;

            // Assert
            Assert.Equal(95.0, successRate);
        }

        [Fact]
        public void NetworkMonitoringStats_SuccessRate_WithZeroChecks_ShouldReturnZero() {
            // Arrange
            var stats = new NetworkMonitoringStats {
                TotalChecks = 0,
                SuccessfulChecks = 0,
                FailedChecks = 0
            };

            // Act
            var successRate = stats.SuccessRate;

            // Assert
            Assert.Equal(0.0, successRate);
        }

        [Fact]
        public void NetworkMonitoringStats_MonitoringDuration_ShouldCalculateCorrectly() {
            // Arrange
            var startTime = DateTime.UtcNow.AddHours(-2);
            var stats = new NetworkMonitoringStats {
                MonitoringStarted = startTime
            };

            // Act
            var duration = stats.MonitoringDuration;

            // Assert
            Assert.True(duration.TotalHours >= 1.9); // Allow for execution time
            Assert.True(duration.TotalHours <= 2.1); // Allow for execution time
        }


    }
}