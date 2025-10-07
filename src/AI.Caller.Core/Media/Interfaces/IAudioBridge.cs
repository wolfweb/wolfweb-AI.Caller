using AI.Caller.Core.Models;
using System;

namespace AI.Caller.Core {
    public interface IAudioBridge : IDisposable {
        event Action<byte[]>? IncomingAudioReceived;
        
        void Initialize(MediaProfile profile);
        
        void ProcessIncomingAudio(byte[] audioData, int sampleRate, int payloadType);
        
        void Start();
        
        void Stop();
    }
}