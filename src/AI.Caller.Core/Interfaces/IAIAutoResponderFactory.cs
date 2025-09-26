using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Interfaces {
    public interface IAIAutoResponderFactory {        
        AIAutoResponder CreateWithRtp(IAudioBridge audioBridge, MediaProfile profile, SIPSorcery.Net.RTPSession rtpSession);
    }
}