namespace AI.Caller.Core.Media.Interfaces {
    public interface IPlaybackMeter {
        float PlaybackRms { get; }
    }
    public interface IPausablePlayback {
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }
}