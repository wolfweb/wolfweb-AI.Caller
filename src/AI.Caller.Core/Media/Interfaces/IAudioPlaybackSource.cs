using System;
using System.Threading;
using System.Threading.Tasks;
using AI.Caller.Core.Media;

namespace AI.Caller.Core {
    public interface IAudioPlaybackSource : IAsyncDisposable {
        void Init(MediaProfile profile);
        Task StartAsync(CancellationToken ct);
        byte[] ReadNextPcmFrame();
        Task StopAsync();
    }
}