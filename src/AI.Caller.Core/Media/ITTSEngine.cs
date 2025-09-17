using AI.Caller.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI.Caller.Core.Media {
    public interface ITTSEngine {
        IAsyncEnumerable<AudioData> SynthesizeStreamAsync(string text, int speakerId, float speed = 1.0f);
    }
}