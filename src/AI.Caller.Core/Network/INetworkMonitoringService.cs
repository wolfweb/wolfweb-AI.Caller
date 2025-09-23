namespace AI.Caller.Core.Network {
    public interface INetworkMonitoringService : IDisposable {        
        event EventHandler<NetworkStatusEventArgs> NetworkStatusChanged;

        event EventHandler<NetworkConnectionLostEventArgs> NetworkConnectionLost;

        event EventHandler<NetworkConnectionRestoredEventArgs> NetworkConnectionRestored;

        event EventHandler<NetworkQualityChangedEventArgs> NetworkQualityChanged;

        void RegisterSipClient(string clientId, object sipClient);

        void UnregisterSipClient(string clientId);

        NetworkStatus GetCurrentNetworkStatus();

        ClientNetworkStatus? GetClientNetworkStatus(string clientId);

        IReadOnlyDictionary<string, ClientNetworkStatus> GetAllClientNetworkStatus();

        Task StartMonitoringAsync();

        Task StopMonitoringAsync();

        bool IsMonitoring { get; }

        Task<NetworkStatus> CheckNetworkStatusAsync();

        NetworkMonitoringStats GetMonitoringStats();
    }
}