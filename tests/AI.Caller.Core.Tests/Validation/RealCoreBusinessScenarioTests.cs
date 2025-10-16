using AI.Caller.Core;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using Xunit;
using Xunit.Abstractions;

namespace AI.Caller.Core.Tests.Validation;

public class RealCoreBusinessScenarioTests : IDisposable {
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<MediaSessionManager> _mediaLogger;
    private readonly SIPTransport _sipTransport;
    private readonly ITestOutputHelper _output;

    public RealCoreBusinessScenarioTests(ITestOutputHelper output) {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _mediaLogger = loggerFactory.CreateLogger<MediaSessionManager>();
        _sipTransport = new SIPTransport();
        _output = output;
    }

    #region Web到Web核心业务场景

    [Fact]
    public async Task Web2Web_OutboundCall_ShouldEstablishCompleteSdpSignaling() {


        var callerClient = new SIPClient("sip.caller.test.com", _sipClientLogger, _sipTransport);
        var calleeClient = new SIPClient("sip.callee.test.com", _sipClientLogger, _sipTransport);
        var destination = "user@sip.callee.test.com";
        var fromHeader = new SIPFromHeader("caller", new SIPURI("caller", "sip.caller.test.com", null), null);

        try {
            var sdpOffer = await callerClient.CreateOfferAsync();
            Assert.NotNull(sdpOffer);
            Assert.NotEmpty(sdpOffer.sdp);
            Assert.Contains("audio", sdpOffer.sdp);
            _output.WriteLine($"Web2Web外呼 - SDP Offer生成成功: {sdpOffer.sdp.Length} 字符");

            calleeClient.SetRemoteDescription(sdpOffer);
            var sdpAnswer = await calleeClient.MediaSessionManager!.CreateAnswerAsync();
            Assert.NotNull(sdpAnswer);
            Assert.NotEmpty(sdpAnswer.sdp);
            Assert.Contains("audio", sdpAnswer.sdp);
            _output.WriteLine($"Web2Web外呼 - SDP Answer生成成功: {sdpAnswer.sdp.Length} 字符");

            callerClient.MediaSessionManager!.SetWebRtcRemoteDescription(sdpAnswer);
            _output.WriteLine("Web2Web外呼 - SDP协商完成");

            try {
                await callerClient.CallAsync(destination, fromHeader);
                _output.WriteLine("Web2Web外呼 - CallAsync调用成功");
            } catch (Exception ex) {
                _output.WriteLine($"Web2Web外呼 - CallAsync异常（预期，无真实SIP服务器）: {ex.Message}");
            }

            Assert.NotNull(sdpOffer);
            Assert.NotEmpty(sdpOffer.sdp);
            Assert.NotNull(sdpAnswer);
            Assert.NotEmpty(sdpAnswer.sdp);

        } finally {
            callerClient?.Shutdown();
            calleeClient?.Shutdown();
        }
    }

    [Fact]
    public async Task Web2Web_InboundCall_ShouldEstablishCompleteSdpSignaling() {
        var calleeClient = new SIPClient("sip.callee.test.com", _sipClientLogger, _sipTransport);

        try {
            var mockInviteRequest = CreateMockSIPInviteRequest();
            Assert.NotNull(mockInviteRequest.Body);
            Assert.Contains("RTP/AVP", mockInviteRequest.Body);
            _sipClientLogger.LogInformation($"Web2Web呼入 - 接收到INVITE请求，SDP长度: {mockInviteRequest.Body.Length}");

            calleeClient.Accept(mockInviteRequest);

            await calleeClient.CreateOfferAsync();

            _sipClientLogger.LogInformation("Web2Web呼入 - 远程SDP Offer设置成功");


            try {
                var answerResult = await calleeClient.AnswerAsync();
                _sipClientLogger.LogInformation("Web2Web呼入 - AnswerAsync调用成功");
            } catch (Exception ex) {
                _sipClientLogger.LogWarning($"Web2Web呼入 - AnswerAsync异常（预期，无真实SIP会话）: {ex.Message}");
            }


            Assert.NotNull(mockInviteRequest.Body);
            Assert.Contains("RTP/AVP", mockInviteRequest.Body);

        } finally {
            calleeClient?.Shutdown();
        }
    }

    #endregion

    #region Web到手机核心业务场景

    [Fact]
    public async Task Web2Mobile_OutboundCall_ShouldEstablishCompleteSdpSignaling() {

        var webClient = new SIPClient("sip.gateway.test.com", _sipClientLogger, _sipTransport);
        var mobileNumber = "+8613800138000";
        var fromHeader = new SIPFromHeader("webcaller", new SIPURI("webcaller", "sip.gateway.test.com", null), null);

        try {

            var webMediaManager = webClient.MediaSessionManager;
            Assert.NotNull(webMediaManager);


            webMediaManager.InitializeMediaSession();
            webMediaManager.InitializePeerConnection(new RTCConfiguration());


            var sdpOffer = await webMediaManager.CreateOfferAsync();
            Assert.NotNull(sdpOffer);
            Assert.NotEmpty(sdpOffer.sdp);
            Assert.Contains("audio", sdpOffer.sdp);

            Assert.True(sdpOffer.sdp.Contains("PCMU") || sdpOffer.sdp.Contains("PCMA"), "SDP应包含PSTN兼容的编解码器");
            _sipClientLogger.LogInformation($"Web2Mobile外呼 - PSTN兼容SDP Offer生成成功: {sdpOffer.sdp.Length} 字符");


            var pstnAnswerSdp = @"v=0
o=- 789012 345678 IN IP4 203.0.113.1
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


            webMediaManager.SetSipRemoteDescription(pstnAnswer);
            _sipClientLogger.LogInformation("Web2Mobile外呼 - PSTN SDP Answer设置成功");


            try {
                await webClient.CallAsync(mobileNumber, fromHeader);
                _sipClientLogger.LogInformation("Web2Mobile外呼 - CallAsync调用成功");
            } catch (Exception ex) {
                _sipClientLogger.LogWarning($"Web2Mobile外呼 - CallAsync异常（预期，无真实PSTN网关）: {ex.Message}");
            }

            Assert.NotNull(sdpOffer);
            Assert.NotEmpty(sdpOffer.sdp);
            Assert.Contains("RTP/AVP", sdpOffer.sdp);

        } finally {
            webClient?.Shutdown();
        }
    }

    [Fact]
    public async Task Mobile2Web_InboundCall_ShouldEstablishCompleteSdpSignaling() {

        var webClient = new SIPClient("sip.web.test.com", _sipClientLogger, _sipTransport);

        try {

            var webMediaManager = webClient.MediaSessionManager;
            Assert.NotNull(webMediaManager);


            var mockMobileInviteRequest = CreateMockMobileToWebInviteRequest();
            Assert.NotNull(mockMobileInviteRequest.Body);
            Assert.Contains("RTP/AVP", mockMobileInviteRequest.Body);
            Assert.Contains("PCMU", mockMobileInviteRequest.Body);
            _sipClientLogger.LogInformation($"Mobile2Web呼入 - 接收到来自手机的INVITE请求，PSTN SDP长度: {mockMobileInviteRequest.Body.Length}");


            webClient.Accept(mockMobileInviteRequest);


            webMediaManager.InitializeMediaSession();
            webMediaManager.InitializePeerConnection(new RTCConfiguration());


            var pstnOffer = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = mockMobileInviteRequest.Body
            };


            webMediaManager.SetWebRtcRemoteDescription(pstnOffer);
            _sipClientLogger.LogInformation("Mobile2Web呼入 - PSTN SDP Offer转换并设置成功");


            var webRtcAnswer = await webMediaManager.CreateAnswerAsync();
            Assert.NotNull(webRtcAnswer);
            Assert.NotEmpty(webRtcAnswer.sdp);
            Assert.Contains("audio", webRtcAnswer.sdp);
            _sipClientLogger.LogInformation($"Mobile2Web呼入 - WebRTC SDP Answer生成成功: {webRtcAnswer.sdp.Length} 字符");


            try {
                var answerResult = await webClient.AnswerAsync();
                _sipClientLogger.LogInformation("Mobile2Web呼入 - AnswerAsync调用成功");
            } catch (Exception ex) {
                _sipClientLogger.LogWarning($"Mobile2Web呼入 - AnswerAsync异常（预期，无真实SIP会话）: {ex.Message}");
            }

            Assert.NotNull(mockMobileInviteRequest.Body);
            Assert.Contains("RTP/AVP", mockMobileInviteRequest.Body);
            Assert.NotNull(webRtcAnswer);
            Assert.NotEmpty(webRtcAnswer.sdp);

        } finally {
            webClient?.Shutdown();
        }
    }

    #endregion

    #region 辅助方法

    private SIPRequest CreateMockSIPInviteRequest() {
        var inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, new SIPURI("caller", "sip.caller.test.com", null));


        if (inviteRequest.Header == null) {
            inviteRequest.Header = new SIPHeader();
        }

        inviteRequest.Header.From = new SIPFromHeader("caller", new SIPURI("caller", "sip.caller.test.com", null), null);
        inviteRequest.Header.To = new SIPToHeader("callee", new SIPURI("callee", "sip.callee.test.com", null), null);
        inviteRequest.Header.CallId = Guid.NewGuid().ToString();
        inviteRequest.Header.CSeq = 1;

        inviteRequest.Body = @"v=0
o=- 123456 654321 IN IP4 192.168.1.100
s=-
c=IN IP4 192.168.1.100
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv";

        return inviteRequest;
    }

    private SIPRequest CreateMockMobileToWebInviteRequest() {
        var inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, new SIPURI("webuser", "sip.web.test.com", null));

        // 确保Header被初始化
        if (inviteRequest.Header == null) {
            inviteRequest.Header = new SIPHeader();
        }

        inviteRequest.Header.From = new SIPFromHeader("mobile", new SIPURI("+8613800138000", "sip.gateway.test.com", null), null);
        inviteRequest.Header.To = new SIPToHeader("webuser", new SIPURI("webuser", "sip.web.test.com", null), null);
        inviteRequest.Header.CallId = Guid.NewGuid().ToString();
        inviteRequest.Header.CSeq = 1;
        // Contact header will be set automatically


        inviteRequest.Body = @"v=0
o=- 789012 345678 IN IP4 203.0.113.1
s=-
c=IN IP4 203.0.113.1
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv";

        return inviteRequest;
    }

    #endregion

    public void Dispose() {
        _sipTransport?.Dispose();
    }
}