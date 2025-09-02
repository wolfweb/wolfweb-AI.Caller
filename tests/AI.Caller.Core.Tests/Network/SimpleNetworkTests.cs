using Xunit;
using AI.Caller.Core;
using Microsoft.Extensions.Logging;
using Moq;

namespace AI.Caller.Core.Tests.Network;

public class SimpleNetworkTests {
    [Fact]
    public void SIPClient_Constructor_WithValidParameters_ShouldNotThrow() {
        var mockLogger = new Mock<ILogger<SIPClient>>();
        var mockTransport = new Mock<SIPSorcery.SIP.SIPTransport>();
        
        var exception = Record.Exception(() => 
            new SIPClient("sip.test.com", mockLogger.Object, mockTransport.Object));
        
        Assert.Null(exception);
    }

    [Fact]
    public void MediaSessionManager_Constructor_ShouldNotThrow() {
        var mockLogger = new Mock<ILogger<MediaSessionManager>>();
        
        var exception = Record.Exception(() => 
            new MediaSessionManager(mockLogger.Object));
        
        Assert.Null(exception);
    }

    [Fact]
    public void SIPTransportManager_Constructor_WithValidParameters_ShouldNotThrow() {
        var mockLogger = new Mock<ILogger<SIPTransportManager>>();
        
        var exception = Record.Exception(() => 
            new SIPTransportManager("127.0.0.1", mockLogger.Object));
        
        Assert.Null(exception);
    }

    [Fact]
    public void WebRTCSettings_DefaultValues_ShouldBeValid() {
        var settings = new WebRTCSettings();
        
        Assert.NotNull(settings);
        Assert.NotNull(settings.IceServers);
    }
}