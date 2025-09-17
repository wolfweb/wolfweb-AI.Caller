using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Interfaces {
    public interface IAIAutoResponderFactory {
        AIAutoResponder CreateAutoResponder(IAudioBridge audioBridge, MediaProfile profile);
    }
}