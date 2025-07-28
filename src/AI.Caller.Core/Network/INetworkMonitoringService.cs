namespace AI.Caller.Core.Network
{
    /// <summary>
    /// 网络监控服务接口
    /// </summary>
    public interface INetworkMonitoringService : IDisposable
    {
        /// <summary>
        /// 网络状态变化事件
        /// </summary>
        event EventHandler<NetworkStatusEventArgs> NetworkStatusChanged;

        /// <summary>
        /// 网络连接丢失事件
        /// </summary>
        event EventHandler<NetworkConnectionLostEventArgs> NetworkConnectionLost;

        /// <summary>
        /// 网络连接恢复事件
        /// </summary>
        event EventHandler<NetworkConnectionRestoredEventArgs> NetworkConnectionRestored;

        /// <summary>
        /// 网络质量变化事件
        /// </summary>
        event EventHandler<NetworkQualityChangedEventArgs> NetworkQualityChanged;

        /// <summary>
        /// 注册SIP客户端进行监控
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="sipClient">SIP客户端实例</param>
        void RegisterSipClient(string clientId, object sipClient);

        /// <summary>
        /// 注销SIP客户端监控
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        void UnregisterSipClient(string clientId);

        /// <summary>
        /// 获取当前网络状态
        /// </summary>
        /// <returns>网络状态</returns>
        NetworkStatus GetCurrentNetworkStatus();

        /// <summary>
        /// 获取指定客户端的网络状态
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <returns>客户端网络状态</returns>
        ClientNetworkStatus? GetClientNetworkStatus(string clientId);

        /// <summary>
        /// 获取所有已注册客户端的网络状态
        /// </summary>
        /// <returns>客户端网络状态列表</returns>
        IReadOnlyDictionary<string, ClientNetworkStatus> GetAllClientNetworkStatus();

        /// <summary>
        /// 开始网络监控
        /// </summary>
        Task StartMonitoringAsync();

        /// <summary>
        /// 停止网络监控
        /// </summary>
        Task StopMonitoringAsync();

        /// <summary>
        /// 是否正在监控
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// 手动触发网络状态检查
        /// </summary>
        /// <returns>检查任务</returns>
        Task<NetworkStatus> CheckNetworkStatusAsync();

        /// <summary>
        /// 获取网络监控统计信息
        /// </summary>
        /// <returns>监控统计</returns>
        NetworkMonitoringStats GetMonitoringStats();
    }
}