using AI.Caller.Core.Models;
using System;

namespace AI.Caller.Core {
    public interface IAudioBridge : IDisposable {
        event Action<byte[]>? IncomingAudioReceived;
        
        event Action<byte[]>? OutgoingAudioRequested;
        
        void Initialize(MediaProfile profile);
        
        void ProcessIncomingAudio(byte[] audioData, int sampleRate);
        
        void InjectOutgoingAudio(byte[] audioData);
        
        byte[] GetNextOutgoingFrame();
        
        void Start();
        
        void Stop();
    }
}