using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using System.Diagnostics;

namespace AI.Caller.Core.Tests.Integration;

public class EndToEndIntegrationTests : IDisposable {
    private readonly Mock<ILogger> _mockLogger;
    private readonly SIPTransport _sipTransport;

    public EndToEndIntegrationTests() {
        _mockLogger = new Mock<ILogger>();
        _sipTransport = new SIPTransport();
    }

    [Fact]
    public async Task CompleteCallFlow_WebToWeb_ShouldEstablishAndTerminateSuccessfully() {
        var callerClient = new SIPClient("caller.test.com", _mockLogger.Object, _sipTransport);
        var calleeClient = new SIPClient("callee.test.com", _mockLogger.Object, _sipTransport);

        bool callAnswered = false;
        bool callEnded = false;

        callerClient.CallAnswered += (client) => callAnswered = true;
        callerClient.CallEnded += (client) => callEnded = true;

        try {
            var callerOffer = await callerClient.CreateOfferAsync();
            Assert.NotNull(callerOffer);
            Assert.Equal(RTCSdpType.offer, callerOffer.type);
            Assert.Contains("m=audio", callerOffer.sdp);

            var calleeAnswer = await calleeClient.OfferAsync(callerOffer);
            Assert.NotNull(calleeAnswer);
            Assert.Equal(RTCSdpType.answer, calleeAnswer.type);
            Assert.Contains("m=audio", calleeAnswer.sdp);

            callerClient.SetRemoteDescription(calleeAnswer);

            await Task.Delay(1000);

            callerClient.Hangup();

            await Task.Delay(500);

            Assert.True(callEnded, "Call should have ended after hangup");
        } finally {
            callerClient.Shutdown();
            calleeClient.Shutdown();
        }
    }

    [Fact]
    public async Task MediaSessionFlow_ShouldHandleOfferAnswerNegotiation() {
        var callerMedia = new MediaSessionManager(_mockLogger.Object);
        var calleeMedia = new MediaSessionManager(_mockLogger.Object);

        try {
            await callerMedia.InitializeMediaSession();
            await calleeMedia.InitializeMediaSession();

            var config = new RTCConfiguration {
                iceServers = new List<RTCIceServer> {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            callerMedia.InitializePeerConnection(config);
            calleeMedia.InitializePeerConnection(config);

            var offer = await callerMedia.CreateOfferAsync();
            Assert.NotNull(offer);
            Assert.Contains("v=0", offer.sdp);
            Assert.Contains("m=audio", offer.sdp);

            calleeMedia.SetWebRtcRemoteDescription(offer);
            var answer = await calleeMedia.CreateAnswerAsync();
            Assert.NotNull(answer);
            Assert.Contains("v=0", answer.sdp);
            Assert.Contains("m=audio", answer.sdp);

            callerMedia.SetWebRtcRemoteDescription(answer);

            await Task.Delay(2000);

            Assert.NotNull(callerMedia.PeerConnection);
            Assert.NotNull(calleeMedia.PeerConnection);
        } finally {
            callerMedia.Dispose();
            calleeMedia.Dispose();
        }
    }

    [Fact]
    public async Task StressTest_MultipleSimultaneousCalls_ShouldHandleLoad() {
        var tasks = new List<Task>();
        var clients = new List<SIPClient>();

        for (int i = 0; i < 10; i++) {
            var client = new SIPClient($"test{i}.server", _mockLogger.Object, _sipTransport);
            clients.Add(client);

            tasks.Add(Task.Run(async () => {
                try {
                    var offer = await client.CreateOfferAsync();
                    Assert.NotNull(offer);
                    await Task.Delay(100);
                } catch (Exception ex) {
                    _mockLogger.Object.LogError(ex, $"Error in stress test for client {i}");
                }
            }));
        }

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 15000, $"Stress test took {stopwatch.ElapsedMilliseconds}ms, expected < 15000ms");

        foreach (var client in clients) {
            client.Shutdown();
        }
    }

    [Fact]
    public async Task ResourceManagement_CreateAndDisposeMultipleManagers_ShouldNotLeak() {
        var managers = new List<MediaSessionManager>();

        for (int i = 0; i < 20; i++) {
            var manager = new MediaSessionManager(_mockLogger.Object);
            await manager.InitializeMediaSession();
            managers.Add(manager);
        }

        var initialMemory = GC.GetTotalMemory(false);

        foreach (var manager in managers) {
            manager.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(false);
        var memoryDifference = Math.Abs(finalMemory - initialMemory);

        Assert.True(memoryDifference < 10 * 1024 * 1024, $"Potential memory leak: {memoryDifference / 1024 / 1024}MB difference");
    }

    public void Dispose() {
        _sipTransport?.Dispose();
    }
}