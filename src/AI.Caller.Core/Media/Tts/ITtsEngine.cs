using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI.Caller.Core.Media.Tts {
    public interface ITtsEngine {
        IAsyncEnumerable<float[]> SynthesizeStreamAsync(string text, int speakerId, float speed = 1.0f);
    }
}