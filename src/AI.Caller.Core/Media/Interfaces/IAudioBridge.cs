using AI.Caller.Core.Models;
using System;

namespace AI.Caller.Core {
    public interface IAudioBridge : IDisposable {
        event Action<short[]>? IncomingAudioReceived;
        
        event Action<short[]>? OutgoingAudioRequested;
        
        void Initialize(MediaProfile profile);
        
        void ProcessIncomingAudio(byte[] audioData, int sampleRate);
        
        void InjectOutgoingAudio(short[] audioData);
        
        short[] GetNextOutgoingFrame();
        
        void Start();
        
        void Stop();
    }
}