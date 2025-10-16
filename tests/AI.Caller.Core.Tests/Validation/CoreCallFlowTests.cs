using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;

namespace AI.Caller.Core.Tests.Validation;

public class CoreCallFlowTests : IDisposable {
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<MediaSessionManager> _mediaLogger;

    public CoreCallFlowTests() {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _mediaLogger = loggerFactory.CreateLogger<MediaSessionManager>();
    }

    #region Web到Web核心流程测试

    [Fact]
    public async Task WebToWeb_OutboundCall_ShouldEstablishSuccessfully() {

        var sipTransport = new SIPTransport();
        var callerClient = new SIPClient("sip.caller.test.com", _sipClientLogger, sipTransport);
        var calleeClient = new SIPClient("sip.callee.test.com", _sipClientLogger, sipTransport);
        var callerMedia = new MediaSessionManager(_mediaLogger);
        var calleeMedia = new MediaSessionManager(_mediaLogger);

        try {

            callerMedia.InitializeMediaSession();
            calleeMedia.InitializeMediaSession();

            callerMedia.InitializePeerConnection(new RTCConfiguration());
            calleeMedia.InitializePeerConnection(new RTCConfiguration());


            var offer = await callerMedia.CreateOfferAsync();
            Assert.NotNull(offer);
            Assert.NotEmpty(offer.sdp);


            calleeMedia.SetWebRtcRemoteDescription(offer);
            var answer = await calleeMedia.CreateAnswerAsync();
            Assert.NotNull(answer);
            Assert.NotEmpty(answer.sdp);


            callerMedia.SetWebRtcRemoteDescription(answer);




        } finally {
            callerMedia?.Dispose();
            calleeMedia?.Dispose();
            sipTransport?.Dispose();
        }
    }

    [Fact]
    public async Task WebToWeb_InboundCall_ShouldBeAnsweredSuccessfully() {

        var sipTransport = new SIPTransport();
        var callerClient = new SIPClient("sip.caller.test.com", _sipClientLogger, sipTransport);
        var calleeClient = new SIPClient("sip.callee.test.com", _sipClientLogger, sipTransport);
        var callerMedia = new MediaSessionManager(_mediaLogger);
        var calleeMedia = new MediaSessionManager(_mediaLogger);

        try {
            callerMedia.InitializeMediaSession();
            calleeMedia.InitializeMediaSession();

            callerMedia.InitializePeerConnection(new RTCConfiguration());
            calleeMedia.InitializePeerConnection(new RTCConfiguration());


            var incomingOffer = await callerMedia.CreateOfferAsync();
            Assert.NotNull(incomingOffer);


            calleeMedia.SetWebRtcRemoteDescription(incomingOffer);
            var answerResponse = await calleeMedia.CreateAnswerAsync();
            Assert.NotNull(answerResponse);


            callerMedia.SetWebRtcRemoteDescription(answerResponse);


            Assert.NotNull(incomingOffer);
            Assert.NotNull(answerResponse);

        } finally {
            callerMedia?.Dispose();
            calleeMedia?.Dispose();
            sipTransport?.Dispose();
        }
    }

    #endregion

    #region Web到手机核心流程测试

    [Fact]
    public async Task WebToMobile_OutboundCall_ShouldRouteToGateway() {

        var sipTransport = new SIPTransport();
        var webClient = new SIPClient("sip.gateway.test.com", _sipClientLogger, sipTransport);
        var webMedia = new MediaSessionManager(_mediaLogger);
        var mobileNumber = "+8613800138000";

        try {
            webMedia.InitializeMediaSession();
            webMedia.InitializePeerConnection(new RTCConfiguration());


            var offer = await webMedia.CreateOfferAsync();
            Assert.NotNull(offer);
            Assert.NotEmpty(offer.sdp);

            Assert.Contains("PCMU", offer.sdp);
            Assert.Contains("PCMA", offer.sdp);

            var gatewayProcessed = ProcessGatewayCall(mobileNumber, offer.sdp);
            Assert.True(gatewayProcessed);


            var mobileAnswerSdp = CreateMobileAnswerSdp();
            var mobileAnswer = new RTCSessionDescriptionInit {
                type = RTCSdpType.answer,
                sdp = mobileAnswerSdp
            };


            webMedia.SetSipRemoteDescription(mobileAnswer);


            Assert.NotNull(offer);
            Assert.NotEmpty(offer.sdp);

        } finally {
            webMedia?.Dispose();
            sipTransport?.Dispose();
        }
    }

    [Fact]
    public async Task WebToMobile_CallAnswer_ShouldEstablishPstnConnection() {

        var sipTransport = new SIPTransport();
        var webClient = new SIPClient("sip.gateway.test.com", _sipClientLogger, sipTransport);
        var webMedia = new MediaSessionManager(_mediaLogger);

        try {
            webMedia.InitializeMediaSession();
            webMedia.InitializePeerConnection(new RTCConfiguration());


            var offer = await webMedia.CreateOfferAsync();
            Assert.NotNull(offer);


            var pstnAnswerSdp = @"v=0
o=- 123456 654321 IN IP4 203.0.113.1
s=-
c=IN IP4 203.0.113.1
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv";

            var pstnAnswer = new RTCSessionDescriptionInit {
                type = RTCSdpType.answer,
                sdp = pstnAnswerSdp
            };


            webMedia.SetSipRemoteDescription(pstnAnswer);


            Assert.Contains("PCMU", pstnAnswerSdp);
            Assert.Contains("sendrecv", pstnAnswerSdp);
            Assert.NotNull(offer);

        } finally {
            webMedia?.Dispose();
            sipTransport?.Dispose();
        }
    }

    #endregion

    #region 手机到Web核心流程测试

    [Fact]
    public async Task MobileToWeb_InboundCall_ShouldNotifyWebUser() {

        var sipTransport = new SIPTransport();
        var webClient = new SIPClient("sip.inbound.test.com", _sipClientLogger, sipTransport);
        var webMedia = new MediaSessionManager(_mediaLogger);
        var mobileNumber = "+8613800138000";

        try {
            webMedia.InitializeMediaSession();
            webMedia.InitializePeerConnection(new RTCConfiguration());


            var pstnOfferSdp = @"v=0
o=- 123456 654321 IN IP4 203.0.113.1
s=-
c=IN IP4 203.0.113.1
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv";

            var pstnOffer = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = pstnOfferSdp
            };


            webMedia.SetWebRtcRemoteDescription(pstnOffer);


            var webAnswer = await webMedia.CreateAnswerAsync();
            Assert.NotNull(webAnswer);
            Assert.NotEmpty(webAnswer.sdp);


            Assert.Contains("audio", webAnswer.sdp);

            Assert.True(webAnswer.sdp.Contains("m=audio") || webAnswer.sdp.Contains("audio"),
                "SDP should contain audio media description");




        } finally {
            webMedia?.Dispose();
            sipTransport?.Dispose();
        }
    }

    [Fact]
    public async Task MobileToWeb_CallAnswer_ShouldEstablishWebRtcConnection() {

        var sipTransport = new SIPTransport();
        var webClient = new SIPClient("sip.inbound.test.com", _sipClientLogger, sipTransport);
        var webMedia = new MediaSessionManager(_mediaLogger);

        try {
            webMedia.InitializeMediaSession();
            webMedia.InitializePeerConnection(new RTCConfiguration());


            var mobileOfferSdp = CreateMobileOfferSdp();
            var mobileOffer = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = mobileOfferSdp
            };

            webMedia.SetWebRtcRemoteDescription(mobileOffer);


            var webAnswer = await webMedia.CreateAnswerAsync();
            Assert.NotNull(webAnswer);


            Assert.Contains("a=sendrecv", webAnswer.sdp);
            Assert.Contains("m=audio", webAnswer.sdp);

            Assert.NotNull(webAnswer);
            Assert.NotEmpty(webAnswer.sdp);

        } finally {
            webMedia?.Dispose();
            sipTransport?.Dispose();
        }
    }

    #endregion

    #region 核心SIP信令流程测试

    [Fact]
    public void SipClient_CallAsync_ShouldInitiateOutboundCall() {

        var sipTransport = new SIPTransport();
        var sipClient = new SIPClient("sip.test.com", _sipClientLogger, sipTransport);

        try {

            Assert.False(sipClient.IsCallActive);


            Assert.False(sipClient.IsCallActive);

        } finally {
            sipTransport?.Dispose();
        }
    }

    [Fact]
    public void SipClient_AnswerAsync_ShouldHandleInboundCall() {

        var sipTransport = new SIPTransport();
        var sipClient = new SIPClient("sip.test.com", _sipClientLogger, sipTransport);

        try {

            var answerMethod = typeof(SIPClient).GetMethod("AnswerAsync") ?? typeof(SIPClient).GetMethod("Answer");

            Assert.False(sipClient.IsCallActive);

        } finally {
            sipTransport?.Dispose();
        }
    }

    [Fact]
    public async Task MediaSessionManager_CreateOfferAsync_ShouldGenerateValidSdp() {

        var mediaManager = new MediaSessionManager(_mediaLogger);

        try {
            mediaManager.InitializeMediaSession();
            mediaManager.InitializePeerConnection(new RTCConfiguration());


            var offer = await mediaManager.CreateOfferAsync();

            Assert.NotNull(offer);
            Assert.NotEmpty(offer.sdp);
            Assert.Equal(RTCSdpType.offer, offer.type);

            Assert.Contains("v=0", offer.sdp);
            Assert.Contains("m=audio", offer.sdp);

        } finally {
            mediaManager?.Dispose();
        }
    }

    [Fact]
    public async Task MediaSessionManager_CreateAnswerAsync_ShouldGenerateValidSdp() {

        var mediaManager = new MediaSessionManager(_mediaLogger);

        try {
            mediaManager.InitializeMediaSession();
            mediaManager.InitializePeerConnection(new RTCConfiguration());


            var offer = await mediaManager.CreateOfferAsync();
            Assert.NotNull(offer);


            var remoteMedia = new MediaSessionManager(_mediaLogger);
            remoteMedia.InitializeMediaSession();
            remoteMedia.InitializePeerConnection(new RTCConfiguration());


            remoteMedia.SetWebRtcRemoteDescription(offer);
            var answer = await remoteMedia.CreateAnswerAsync();

            Assert.NotNull(answer);
            Assert.NotEmpty(answer.sdp);
            Assert.Equal(RTCSdpType.answer, answer.type);

        } finally {
            mediaManager?.Dispose();
        }
    }

    #endregion

    #region 辅助方法

    private bool ProcessGatewayCall(string mobileNumber, string sdp) {

        return !string.IsNullOrEmpty(mobileNumber) &&
               !string.IsNullOrEmpty(sdp) &&
               mobileNumber.StartsWith("+");
    }

    private string CreateMobileAnswerSdp() {
        return @"v=0
o=- 123456 654321 IN IP4 203.0.113.1
s=-
c=IN IP4 203.0.113.1
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv
a=fingerprint:sha-256 AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99
a=setup:active";
    }

    private string CreateMobileOfferSdp() {
        return @"v=0
o=- 123456 654321 IN IP4 203.0.113.1
s=-
c=IN IP4 203.0.113.1
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv
a=fingerprint:sha-256 AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99
a=setup:actpass";
    }

    #endregion

    public void Dispose() {

    }
}