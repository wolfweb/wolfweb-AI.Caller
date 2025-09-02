using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using System.Net;

namespace AI.Caller.Core.Tests;

public class ExceptionHandlingTests : IDisposable {
    private readonly Mock<ILogger> _mockLogger;
    private readonly SIPTransport _sipTransport;

    public ExceptionHandlingTests() {
        _mockLogger = new Mock<ILogger>();
        _sipTransport = new SIPTransport();
    }

    [Fact]
    public async Task SIPClient_CallAsync_WithInvalidDestination_ShouldHandleException() {
        var sipClient = new SIPClient("invalid.server", _mockLogger.Object, _sipTransport);
        var fromHeader = new SIPFromHeader("test", SIPURI.ParseSIPURI("sip:test@test.com"), null);

        await Assert.ThrowsAsync<ArgumentException>(async () => {
            await sipClient.CallAsync("", fromHeader);
        });
    }

    [Fact]
    public async Task SIPClient_CallAsync_WithNetworkError_ShouldHandleGracefully() {
        var sipClient = new SIPClient("unreachable.server", _mockLogger.Object, _sipTransport);
        var fromHeader = new SIPFromHeader("test", SIPURI.ParseSIPURIRelaxed("sip:test@test.com"), null);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () => {
            await sipClient.CallAsync("sip:test@unreachable.server", fromHeader);
        });

        Assert.NotNull(exception);
    }

    [Fact]
    public void SIPClient_Hangup_WhenNotInCall_ShouldNotThrow() {
        var sipClient = new SIPClient("test.server", _mockLogger.Object, _sipTransport);

        var exception = Record.Exception(() => sipClient.Hangup());

        Assert.Null(exception);
    }

    [Fact]
    public async Task MediaSessionManager_InitializeMediaSession_WhenAlreadyInitialized_ShouldNotThrow() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);

        await mediaManager.InitializeMediaSession();
        var exception = await Record.ExceptionAsync(async () => {
            await mediaManager.InitializeMediaSession();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void MediaSessionManager_AddIceCandidate_WithNullCandidate_ShouldThrowArgumentNullException() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);

        Assert.Throws<ArgumentNullException>(() => {
            mediaManager.AddIceCandidate(null);
        });
    }

    [Fact]
    public void MediaSessionManager_SetWebRtcRemoteDescription_WithNullDescription_ShouldThrowArgumentNullException() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);

        Assert.Throws<ArgumentNullException>(() => {
            mediaManager.SetWebRtcRemoteDescription(null);
        });
    }

    [Fact]
    public void MediaSessionManager_SetWebRtcRemoteDescription_WithoutPeerConnection_ShouldThrowInvalidOperationException() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);
        var description = new RTCSessionDescriptionInit {
            type = RTCSdpType.offer,
            sdp = "v=0\r\no=- 123456 654321 IN IP4 192.168.1.100\r\ns=-\r\nc=IN IP4 192.168.1.100\r\nt=0 0\r\nm=audio 5004 RTP/AVP 0 8"
        };

        Assert.Throws<InvalidOperationException>(() => {
            mediaManager.SetWebRtcRemoteDescription(description);
        });
    }

    [Fact]
    public async Task MediaSessionManager_CreateOfferAsync_AfterDispose_ShouldThrowObjectDisposedException() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);
        mediaManager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => {
            await mediaManager.CreateOfferAsync();
        });
    }

    [Fact]
    public async Task MediaSessionManager_CreateAnswerAsync_AfterDispose_ShouldThrowObjectDisposedException() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);
        mediaManager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => {
            await mediaManager.CreateAnswerAsync();
        });
    }

    [Fact]
    public void MediaSessionManager_Dispose_MultipleTimes_ShouldNotThrow() {
        var mediaManager = new MediaSessionManager(_mockLogger.Object);

        mediaManager.Dispose();
        var exception = Record.Exception(() => mediaManager.Dispose());

        Assert.Null(exception);
    }

    public void Dispose() {
        _sipTransport?.Dispose();
    }
}