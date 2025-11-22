using AI.Caller.Core.Models;

namespace AI.Caller.Core.Media {
    public interface ITTSEngine {
        IAsyncEnumerable<AudioData> SynthesizeStreamAsync(string text, int speakerId, float speed = 1.0f);
    }
}