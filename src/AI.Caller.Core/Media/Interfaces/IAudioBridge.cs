using AI.Caller.Core.Models;
using System;

namespace AI.Caller.Core {
    public interface IAudioBridge : IDisposable {
        event Action<byte[]>? IncomingAudioReceived;

        event Action<byte>? OnDtmfToneReceived;

        void Initialize(MediaProfile profile);

        void SetMediaSessionManager(MediaSessionManager manager);

        void ProcessIncomingAudio(byte[] audioData, int sampleRate, int payloadType);
        
        void Start();
        
        void Stop();
    }
}