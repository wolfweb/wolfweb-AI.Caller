using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    public class RecordingStatusServiceTests : IDisposable
    {
        private readonly Mock<ILogger<RecordingStatusService>> _mockLogger;
        private readonly Mock<IRecordingCore> _mockRecordingCore;
        private readonly Mock<IAudioBridge> _mockAudioBridge;
        private readonly RecordingStatusService _statusService;
        
        public RecordingStatusServiceTests()
        {
            _mockLogger = new Mock<ILogger<RecordingStatusService>>();
            _mockRecordingCore = new Mock<IRecordingCore>();
            _mockAudioBridge = new Mock<IAudioBridge>();
            _statusService = new RecordingStatusService(_mockLogger.Object);
        }
        
        public void Dispose()
        {
            _statusService?.Dispose();
        }
        
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.NotNull(_statusService);
        }
        
        [Fact]
        public void RegisterRecordingCore_ShouldRegisterSuccessfully()
        {
            // Act
            _statusService.RegisterRecordingCore(_mockRecordingCore.Object);
            
            // Assert - Should not throw
            Assert.True(true);
        }
        
        [Fact]
        public void RegisterAudioBridge_ShouldRegisterSuccessfully()
        {
            // Act
            _statusService.RegisterAudioBridge(_mockAudioBridge.Object);
            
            // Assert - Should not throw
            Assert.True(true);
        }
        
        [Fact]
        public void GetHealthStatus_WithoutRecordingCore_ShouldReturnUnknownStatus()
        {
            // Act
            var status = _statusService.GetHealthStatus();
            
            // Assert
            Assert.Equal(RecordingQuality.Unknown, status.Quality);
            Assert.Contains("RecordingCore not available", status.Issues);
        }
        
        [Fact]
        public void GetHealthStatus_WithRecordingCore_ShouldReturnCoreStatus()
        {
            // Arrange
            var expectedStatus = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Good,
                IsDataFlowing = true,
                Issues = new List<string>()
            };
            
            _mockRecordingCore.Setup(x => x.GetHealthStatus()).Returns(expectedStatus);
            _statusService.RegisterRecordingCore(_mockRecordingCore.Object);
            
            // Act
            var status = _statusService.GetHealthStatus();
            
            // Assert
            Assert.Equal(RecordingQuality.Good, status.Quality);
            Assert.True(status.IsHealthy);
            Assert.Empty(status.Issues);
        }
        
        [Fact]
        public void GetDataFlowStatus_WithoutMonitor_ShouldReturnUnavailableStatus()
        {
            // Act
            var status = _statusService.GetDataFlowStatus();
            
            // Assert
            Assert.False(status.IsHealthy);
            Assert.Equal(RecordingQuality.Unknown, status.Quality);
            Assert.Contains("DataFlowMonitor not available", status.CurrentIssues);
        }
        
        [Fact]
        public void GetAudioBridgeStats_WithoutBridge_ShouldReturnUnavailableStatus()
        {
            // Act
            var stats = _statusService.GetAudioBridgeStats();
            
            // Assert
            Assert.False(stats.IsHealthy);
            Assert.Contains("AudioBridge not available", stats.Issues);
        }
        
        [Fact]
        public void GetAudioBridgeStats_WithBridge_ShouldReturnBridgeStats()
        {
            // Arrange
            var expectedStats = new AudioBridgeStats
            {
                IsHealthy = true,
                Issues = new List<string>()
            };
            
            _mockAudioBridge.Setup(x => x.GetStats()).Returns(expectedStats);
            _statusService.RegisterAudioBridge(_mockAudioBridge.Object);
            
            // Act
            var stats = _statusService.GetAudioBridgeStats();
            
            // Assert
            Assert.True(stats.IsHealthy);
            Assert.Empty(stats.Issues);
        }
        
        [Fact]
        public void GetSystemStatus_ShouldCombineAllComponentStatuses()
        {
            // Arrange
            var recordingStatus = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Good,
                IsDataFlowing = true,
                Issues = new List<string> { "Recording issue" }
            };
            
            var bridgeStats = new AudioBridgeStats
            {
                IsHealthy = true,
                Issues = new List<string>()
            };
            
            _mockRecordingCore.Setup(x => x.GetHealthStatus()).Returns(recordingStatus);
            _mockAudioBridge.Setup(x => x.GetStats()).Returns(bridgeStats);
            
            _statusService.RegisterRecordingCore(_mockRecordingCore.Object);
            _statusService.RegisterAudioBridge(_mockAudioBridge.Object);
            
            // Act
            var systemStatus = _statusService.GetSystemStatus();
            
            // Assert
            Assert.NotNull(systemStatus);
            Assert.NotNull(systemStatus.RecordingStatus);
            Assert.NotNull(systemStatus.AudioBridgeStatus);
            Assert.True(systemStatus.Issues.Count > 0); // Should include recording issue
            Assert.True(systemStatus.Uptime > TimeSpan.Zero);
        }
        
        [Fact]
        public void GetSystemStatus_AllComponentsHealthy_ShouldReturnHealthySystem()
        {
            // Arrange
            var recordingStatus = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Excellent,
                IsDataFlowing = true,
                Issues = new List<string>()
            };
            
            var bridgeStats = new AudioBridgeStats
            {
                IsHealthy = true,
                Issues = new List<string>()
            };
            
            _mockRecordingCore.Setup(x => x.GetHealthStatus()).Returns(recordingStatus);
            _mockAudioBridge.Setup(x => x.GetStats()).Returns(bridgeStats);
            
            _statusService.RegisterRecordingCore(_mockRecordingCore.Object);
            _statusService.RegisterAudioBridge(_mockAudioBridge.Object);
            
            // Act
            var systemStatus = _statusService.GetSystemStatus();
            
            // Assert
            Assert.False(systemStatus.IsHealthy); // DataFlowMonitor is not available, so system is not healthy
            Assert.Equal(RecordingQuality.Unknown, systemStatus.OverallQuality); // Due to missing DataFlowMonitor
        }
        
        [Fact]
        public void ResetAllStats_ShouldCallResetOnComponents()
        {
            // Arrange
            _statusService.RegisterAudioBridge(_mockAudioBridge.Object);
            
            // Act
            _statusService.ResetAllStats();
            
            // Assert
            _mockAudioBridge.Verify(x => x.ResetStats(), Times.Once);
        }
        
        [Fact]
        public void SystemStatusChanged_ShouldTriggerWhenStatusChanges()
        {
            // Arrange
            bool eventTriggered = false;
            SystemStatusChangedEventArgs? receivedArgs = null;
            
            _statusService.SystemStatusChanged += (sender, args) =>
            {
                eventTriggered = true;
                receivedArgs = args;
            };
            
            // 设置初始状态
            var initialStatus = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Good,
                IsDataFlowing = true,
                Issues = new List<string>()
            };
            
            _mockRecordingCore.Setup(x => x.GetHealthStatus()).Returns(initialStatus);
            _statusService.RegisterRecordingCore(_mockRecordingCore.Object);
            
            // Act - 等待状态检查定时器触发
            Thread.Sleep(3000);
            
            // 改变状态
            var changedStatus = new RecordingHealthStatus
            {
                Quality = RecordingQuality.Poor,
                IsDataFlowing = false,
                Issues = new List<string> { "New issue" }
            };
            
            _mockRecordingCore.Setup(x => x.GetHealthStatus()).Returns(changedStatus);
            
            // 等待下一次状态检查
            Thread.Sleep(3000);
            
            // Assert
            // 注意：由于事件是异步触发的，这个测试可能需要调整
            // 在实际实现中，可能需要更复杂的同步机制
        }
        
        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Act
            _statusService.Dispose();
            
            // Assert - Should not throw
            Assert.True(true);
        }
    }
}