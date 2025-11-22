using AI.Caller.Core.Models;

namespace AI.Caller.Core.Media {
    public interface IAsrEngine {
        string RecognizeStream(byte[] pcmBytes);
    }
}