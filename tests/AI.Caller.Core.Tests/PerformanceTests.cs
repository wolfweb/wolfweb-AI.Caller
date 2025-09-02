using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using System.Diagnostics;

namespace AI.Caller.Core.Tests;

public class PerformanceTests : IDisposable {
    private readonly Mock<ILogger> _mockLogger;
    private readonly SIPTransport _sipTransport;

    public PerformanceTests() {
        _mockLogger = new Mock<ILogger>();
        _sipTransport = new SIPTransport();
    }

    [Fact]
    public async Task MediaSessionManager_InitializeMediaSession_ShouldCompleteWithinTimeLimit() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);
        var stopwatch = Stopwatch.StartNew();

        await mediaManager.InitializeMediaSession();
        
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"MediaSession initialization took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
        
        mediaManager.Dispose();
    }

    [Fact]
    public async Task MediaSessionManager_CreateOfferAsync_ShouldCompleteWithinTimeLimit() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);
        await mediaManager.InitializeMediaSession();
        
        var config = new RTCConfiguration {
            iceServers = new List<RTCIceServer> {
                new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
            }
        };
        mediaManager.InitializePeerConnection(config);

        var stopwatch = Stopwatch.StartNew();
        var offer = await mediaManager.CreateOfferAsync();
        stopwatch.Stop();

        Assert.NotNull(offer);
        Assert.True(stopwatch.ElapsedMilliseconds < 3000, $"CreateOffer took {stopwatch.ElapsedMilliseconds}ms, expected < 3000ms");
        
        mediaManager.Dispose();
    }

    [Fact]
    public async Task MediaSessionManager_ConcurrentInitialization_ShouldHandleMultipleRequests() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++) {
            tasks.Add(Task.Run(async () => {
                await mediaManager.InitializeMediaSession();
            }));
        }

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"Concurrent initialization took {stopwatch.ElapsedMilliseconds}ms, expected < 10000ms");
        
        mediaManager.Dispose();
    }

    [Fact]
    public void SIPClient_MultipleInstantiation_ShouldNotExceedMemoryLimit() {
        var clients = new List<SIPClient>();
        var initialMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < 100; i++) {
            var client = new SIPClient($"test{i}.server", _mockLogger.Object, _sipTransport);
            clients.Add(client);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        Assert.True(memoryIncrease < 50 * 1024 * 1024, $"Memory increase: {memoryIncrease / 1024 / 1024}MB, expected < 50MB");

        foreach (var client in clients) {
            client.Shutdown();
        }
    }

    [Fact]
    public async Task MediaSessionManager_MemoryLeak_ShouldNotLeakAfterDispose() {
        var initialMemory = GC.GetTotalMemory(true);
        
        for (int i = 0; i < 50; i++) {
            var mediaManager = new MediaSessionManager(_mockLogger.Object);
            await mediaManager.InitializeMediaSession();
            mediaManager.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        Assert.True(memoryIncrease < 20 * 1024 * 1024, $"Potential memory leak detected. Memory increase: {memoryIncrease / 1024 / 1024}MB");
    }

    public void Dispose() {
        _sipTransport?.Dispose();
    }
}