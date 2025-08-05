namespace AI.Caller.Core.Network {
    /// <summary>
    /// 网络状态变化事件参数
    /// </summary>
    public class NetworkStatusEventArgs : EventArgs {
        /// <summary>
        /// 当前网络状态
        /// </summary>
        public NetworkStatus CurrentStatus { get; }

        /// <summary>
        /// 之前的网络状态
        /// </summary>
        public NetworkStatus? PreviousStatus { get; }

        /// <summary>
        /// 状态变化时间
        /// </summary>
        public DateTime ChangedAt { get; }

        /// <summary>
        /// 是否为连接状态改善
        /// </summary>
        public bool IsImprovement { get; }

        /// <summary>
        /// 是否为连接状态恶化
        /// </summary>
        public bool IsDegradation { get; }

        public NetworkStatusEventArgs(NetworkStatus currentStatus, NetworkStatus? previousStatus = null) {
            CurrentStatus = currentStatus ?? throw new ArgumentNullException(nameof(currentStatus));
            PreviousStatus = previousStatus;
            ChangedAt = DateTime.UtcNow;

            if (previousStatus != null) {
                IsImprovement = DetermineImprovement(currentStatus, previousStatus);
                IsDegradation = DetermineDegradation(currentStatus, previousStatus);
            }
        }

        private static bool DetermineImprovement(NetworkStatus current, NetworkStatus previous) {
            // 连接状态改善
            if (!previous.IsConnected && current.IsConnected)
                return true;

            // 网络质量改善
            if (current.IsConnected && previous.IsConnected && current.Quality > previous.Quality)
                return true;

            // 延迟改善
            if (current.IsConnected && previous.IsConnected && current.LatencyMs < previous.LatencyMs - 50)
                return true;

            // 丢包率改善
            if (current.IsConnected && previous.IsConnected && current.PacketLossRate < previous.PacketLossRate - 1.0)
                return true;

            return false;
        }

        private static bool DetermineDegradation(NetworkStatus current, NetworkStatus previous) {
            // 连接丢失
            if (previous.IsConnected && !current.IsConnected)
                return true;

            // 网络质量恶化
            if (current.IsConnected && previous.IsConnected && current.Quality < previous.Quality)
                return true;

            // 延迟恶化
            if (current.IsConnected && previous.IsConnected && current.LatencyMs > previous.LatencyMs + 100)
                return true;

            // 丢包率恶化
            if (current.IsConnected && previous.IsConnected && current.PacketLossRate > previous.PacketLossRate + 2.0)
                return true;

            return false;
        }

        public override string ToString() {
            var change = IsImprovement ? "Improved" : IsDegradation ? "Degraded" : "Changed";
            return $"Network status {change}: {CurrentStatus}";
        }
    }

    /// <summary>
    /// 网络连接丢失事件参数
    /// </summary>
    public class NetworkConnectionLostEventArgs : EventArgs {
        /// <summary>
        /// 连接丢失时间
        /// </summary>
        public DateTime LostAt { get; }

        /// <summary>
        /// 丢失前的网络状态
        /// </summary>
        public NetworkStatus LastKnownStatus { get; }

        /// <summary>
        /// 连接丢失原因
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// 受影响的客户端ID列表
        /// </summary>
        public IReadOnlyList<string> AffectedClientIds { get; }

        public NetworkConnectionLostEventArgs(
            NetworkStatus lastKnownStatus,
            string reason,
            IEnumerable<string>? affectedClientIds = null) {
            LostAt = DateTime.UtcNow;
            LastKnownStatus = lastKnownStatus ?? throw new ArgumentNullException(nameof(lastKnownStatus));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            AffectedClientIds = affectedClientIds?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
        }

        public override string ToString() {
            return $"Network connection lost at {LostAt:HH:mm:ss}: {Reason} " +
                   $"(Affected clients: {AffectedClientIds.Count})";
        }
    }

    /// <summary>
    /// 网络连接恢复事件参数
    /// </summary>
    public class NetworkConnectionRestoredEventArgs : EventArgs {
        /// <summary>
        /// 连接恢复时间
        /// </summary>
        public DateTime RestoredAt { get; }

        /// <summary>
        /// 当前网络状态
        /// </summary>
        public NetworkStatus CurrentStatus { get; }

        /// <summary>
        /// 连接中断持续时间
        /// </summary>
        public TimeSpan OutageDuration { get; }

        /// <summary>
        /// 恢复的客户端ID列表
        /// </summary>
        public IReadOnlyList<string> RestoredClientIds { get; }

        public NetworkConnectionRestoredEventArgs(
            NetworkStatus currentStatus,
            TimeSpan outageDuration,
            IEnumerable<string>? restoredClientIds = null) {
            RestoredAt = DateTime.UtcNow;
            CurrentStatus = currentStatus ?? throw new ArgumentNullException(nameof(currentStatus));
            OutageDuration = outageDuration;
            RestoredClientIds = restoredClientIds?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
        }

        public override string ToString() {
            return $"Network connection restored at {RestoredAt:HH:mm:ss} " +
                   $"after {OutageDuration:mm\\:ss} outage " +
                   $"(Restored clients: {RestoredClientIds.Count})";
        }
    }

    /// <summary>
    /// 网络质量变化事件参数
    /// </summary>
    public class NetworkQualityChangedEventArgs : EventArgs {
        /// <summary>
        /// 当前网络质量
        /// </summary>
        public NetworkQuality CurrentQuality { get; }

        /// <summary>
        /// 之前的网络质量
        /// </summary>
        public NetworkQuality PreviousQuality { get; }

        /// <summary>
        /// 质量变化时间
        /// </summary>
        public DateTime ChangedAt { get; }

        /// <summary>
        /// 当前网络状态
        /// </summary>
        public NetworkStatus CurrentStatus { get; }

        /// <summary>
        /// 是否为质量改善
        /// </summary>
        public bool IsImprovement => CurrentQuality > PreviousQuality;

        /// <summary>
        /// 是否为质量恶化
        /// </summary>
        public bool IsDegradation => CurrentQuality < PreviousQuality;

        public NetworkQualityChangedEventArgs(
            NetworkQuality currentQuality,
            NetworkQuality previousQuality,
            NetworkStatus currentStatus) {
            CurrentQuality = currentQuality;
            PreviousQuality = previousQuality;
            ChangedAt = DateTime.UtcNow;
            CurrentStatus = currentStatus ?? throw new ArgumentNullException(nameof(currentStatus));
        }

        public override string ToString() {
            var change = IsImprovement ? "improved" : IsDegradation ? "degraded" : "changed";
            return $"Network quality {change} from {PreviousQuality} to {CurrentQuality} at {ChangedAt:HH:mm:ss}";
        }
    }

    /// <summary>
    /// 客户端网络状态变化事件参数
    /// </summary>
    public class ClientNetworkStatusChangedEventArgs : EventArgs {
        /// <summary>
        /// 客户端ID
        /// </summary>
        public string ClientId { get; }

        /// <summary>
        /// 当前客户端网络状态
        /// </summary>
        public ClientNetworkStatus CurrentStatus { get; }

        /// <summary>
        /// 之前的客户端网络状态
        /// </summary>
        public ClientNetworkStatus? PreviousStatus { get; }

        /// <summary>
        /// 状态变化时间
        /// </summary>
        public DateTime ChangedAt { get; }

        /// <summary>
        /// 是否为连接建立
        /// </summary>
        public bool IsConnectionEstablished { get; }

        /// <summary>
        /// 是否为连接丢失
        /// </summary>
        public bool IsConnectionLost { get; }

        public ClientNetworkStatusChangedEventArgs(
            string clientId,
            ClientNetworkStatus currentStatus,
            ClientNetworkStatus? previousStatus = null) {
            ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            CurrentStatus = currentStatus ?? throw new ArgumentNullException(nameof(currentStatus));
            PreviousStatus = previousStatus;
            ChangedAt = DateTime.UtcNow;

            if (previousStatus != null) {
                IsConnectionEstablished = !previousStatus.IsOnline && currentStatus.IsOnline;
                IsConnectionLost = previousStatus.IsOnline && !currentStatus.IsOnline;
            }
        }

        public override string ToString() {
            var status = IsConnectionEstablished ? "Connected" :
                        IsConnectionLost ? "Disconnected" :
                        "Status Changed";
            return $"Client {ClientId} {status}: {CurrentStatus.ConnectionStatus}";
        }
    }
}