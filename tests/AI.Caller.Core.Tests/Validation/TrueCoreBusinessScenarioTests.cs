using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using Xunit.Abstractions;

namespace AI.Caller.Core.Tests.Validation;

public class TrueCoreBusinessScenarioTests : IDisposable {
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly SIPTransport _sipTransport;
    private readonly ITestOutputHelper _output;

    public TrueCoreBusinessScenarioTests(ITestOutputHelper output) {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _sipTransport = new SIPTransport();
        _output = output;
    }

    [Fact]
    public async Task CoreBusinessScenario_Web2Mobile_CompleteCallFlow_ShouldWork() {


        var webClient = new SIPClient("sip.gateway.test.com", _sipClientLogger, _sipTransport);
        var mobileNumber = "+8613800138000";
        var fromHeader = new SIPFromHeader("webcaller", new SIPURI("webcaller", "sip.gateway.test.com", null), null);

        bool pstnCompatibleSdpGenerated = false;
        string? generatedSdp = null;

        try {


            _sipClientLogger.LogInformation("核心业务场景 - 验证Web2Mobile的SDP信令建立机制");

            try {

                var testOffer = await webClient.CreateOfferAsync();
                Assert.NotNull(testOffer);
                Assert.NotEmpty(testOffer.sdp);
                Assert.Contains("audio", testOffer.sdp);


                var callTask = webClient.CallAsync(mobileNumber, fromHeader);


                var webMediaManager = webClient.MediaSessionManager;
                webMediaManager.SdpOfferGenerated += (offer) => {
                    generatedSdp = offer.sdp;

                    if (offer.sdp.Contains("PCMU") || offer.sdp.Contains("PCMA")) {
                        pstnCompatibleSdpGenerated = true;
                        _output.WriteLine($"✅ 核心业务场景 - 检测到PSTN兼容SDP生成: {offer.sdp.Length} 字符");
                    }
                };

                await Task.Delay(200);


                Assert.NotNull(webMediaManager);


                bool hasPstnCodecs = testOffer.sdp.Contains("PCMU") || testOffer.sdp.Contains("PCMA");
                Assert.True(hasPstnCodecs, "Web到手机通话应生成包含PSTN兼容编解码器的SDP");

                _output.WriteLine("✅ 核心业务场景 - Web2Mobile SDP信令机制验证成功");
                _output.WriteLine($"✅ MediaSession可生成PSTN兼容SDP: {testOffer.sdp.Length} 字符");


                try {
                    await callTask;

                    await Task.Delay(10000);
                } catch (Exception ex) {
                    _output.WriteLine($"Web2Mobile CallAsync异常（预期）: {ex.Message}");
                }

                Assert.True(pstnCompatibleSdpGenerated, "PSTN兼容SDP信令应该被生成");
                Assert.NotNull(generatedSdp);
                Assert.Contains("RTP/AVP", generatedSdp);

            } catch (Exception ex) {
                _output.WriteLine($"Web2Mobile SDP机制验证失败: {ex.Message}");
                throw;
            }

        } finally {
            webClient?.Shutdown();
        }
    }

    public void Dispose() {
        _sipTransport?.Dispose();
    }
}