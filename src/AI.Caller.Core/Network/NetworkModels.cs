namespace AI.Caller.Core.Network
{
    /// <summary>
    /// 网络状态
    /// </summary>
    public class NetworkStatus
    {
        /// <summary>
        /// 是否连接到网络
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 网络类型
        /// </summary>
        public NetworkType NetworkType { get; set; }

        /// <summary>
        /// 网络质量
        /// </summary>
        public NetworkQuality Quality { get; set; }

        /// <summary>
        /// 延迟（毫秒）
        /// </summary>
        public int LatencyMs { get; set; }

        /// <summary>
        /// 丢包率（百分比）
        /// </summary>
        public double PacketLossRate { get; set; }

        /// <summary>
        /// 带宽（Kbps）
        /// </summary>
        public int BandwidthKbps { get; set; }

        /// <summary>
        /// 最后检查时间
        /// </summary>
        public DateTime LastChecked { get; set; }

        /// <summary>
        /// 网络问题列表
        /// </summary>
        public List<NetworkIssue> Issues { get; set; } = new();

        /// <summary>
        /// 是否健康
        /// </summary>
        public bool IsHealthy => IsConnected && Quality != NetworkQuality.Poor && Issues.Count == 0;

        public override string ToString()
        {
            return $"Connected: {IsConnected}, Type: {NetworkType}, Quality: {Quality}, " +
                   $"Latency: {LatencyMs}ms, Loss: {PacketLossRate:F1}%, Bandwidth: {BandwidthKbps}Kbps";
        }
    }

    /// <summary>
    /// 客户端网络状态
    /// </summary>
    public class ClientNetworkStatus
    {
        /// <summary>
        /// 客户端ID
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// 客户端类型
        /// </summary>
        public string ClientType { get; set; } = string.Empty;

        /// <summary>
        /// 是否在线
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionStatus ConnectionStatus { get; set; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivity { get; set; }

        /// <summary>
        /// 连接建立时间
        /// </summary>
        public DateTime? ConnectedAt { get; set; }

        /// <summary>
        /// 连接持续时间
        /// </summary>
        public TimeSpan? ConnectionDuration => ConnectedAt.HasValue ? DateTime.UtcNow - ConnectedAt.Value : null;

        /// <summary>
        /// 远程端点
        /// </summary>
        public string? RemoteEndpoint { get; set; }

        /// <summary>
        /// 本地端点
        /// </summary>
        public string? LocalEndpoint { get; set; }

        /// <summary>
        /// 网络统计
        /// </summary>
        public ClientNetworkStats Stats { get; set; } = new();

        /// <summary>
        /// 客户端特定的网络问题
        /// </summary>
        public List<NetworkIssue> Issues { get; set; } = new();

        public override string ToString()
        {
            return $"Client: {ClientId}, Status: {ConnectionStatus}, Online: {IsOnline}, " +
                   $"Duration: {ConnectionDuration?.ToString(@"hh\:mm\:ss") ?? "N/A"}";
        }
    }

    /// <summary>
    /// 客户端网络统计
    /// </summary>
    public class ClientNetworkStats
    {
        /// <summary>
        /// 发送的数据包数量
        /// </summary>
        public long PacketsSent { get; set; }

        /// <summary>
        /// 接收的数据包数量
        /// </summary>
        public long PacketsReceived { get; set; }

        /// <summary>
        /// 发送的字节数
        /// </summary>
        public long BytesSent { get; set; }

        /// <summary>
        /// 接收的字节数
        /// </summary>
        public long BytesReceived { get; set; }

        /// <summary>
        /// 丢失的数据包数量
        /// </summary>
        public long PacketsLost { get; set; }

        /// <summary>
        /// 重传的数据包数量
        /// </summary>
        public long PacketsRetransmitted { get; set; }

        /// <summary>
        /// 平均往返时间（毫秒）
        /// </summary>
        public double AverageRttMs { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// 丢包率
        /// </summary>
        public double PacketLossRate => PacketsSent > 0 ? (double)PacketsLost / PacketsSent * 100 : 0;

        public override string ToString()
        {
            return $"Sent: {PacketsSent} packets ({BytesSent} bytes), " +
                   $"Received: {PacketsReceived} packets ({BytesReceived} bytes), " +
                   $"Loss: {PacketLossRate:F1}%, RTT: {AverageRttMs:F1}ms";
        }
    }

    /// <summary>
    /// 网络问题
    /// </summary>
    public class NetworkIssue
    {
        /// <summary>
        /// 问题类型
        /// </summary>
        public NetworkIssueType Type { get; set; }

        /// <summary>
        /// 问题描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 严重程度
        /// </summary>
        public NetworkIssueSeverity Severity { get; set; }

        /// <summary>
        /// 发生时间
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// 是否已解决
        /// </summary>
        public bool IsResolved { get; set; }

        /// <summary>
        /// 解决时间
        /// </summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>
        /// 持续时间
        /// </summary>
        public TimeSpan Duration => IsResolved && ResolvedAt.HasValue ? 
            ResolvedAt.Value - OccurredAt : 
            DateTime.UtcNow - OccurredAt;

        public override string ToString()
        {
            return $"{Type}: {Description} ({Severity}) - {Duration:mm\\:ss}";
        }
    }

    /// <summary>
    /// 网络监控统计
    /// </summary>
    public class NetworkMonitoringStats
    {
        /// <summary>
        /// 监控开始时间
        /// </summary>
        public DateTime MonitoringStarted { get; set; }

        /// <summary>
        /// 总检查次数
        /// </summary>
        public long TotalChecks { get; set; }

        /// <summary>
        /// 成功检查次数
        /// </summary>
        public long SuccessfulChecks { get; set; }

        /// <summary>
        /// 失败检查次数
        /// </summary>
        public long FailedChecks { get; set; }

        /// <summary>
        /// 已注册客户端数量
        /// </summary>
        public int RegisteredClientsCount { get; set; }

        /// <summary>
        /// 在线客户端数量
        /// </summary>
        public int OnlineClientsCount { get; set; }

        /// <summary>
        /// 检测到的网络问题总数
        /// </summary>
        public int TotalIssuesDetected { get; set; }

        /// <summary>
        /// 已解决的网络问题数量
        /// </summary>
        public int ResolvedIssuesCount { get; set; }

        /// <summary>
        /// 平均检查间隔（毫秒）
        /// </summary>
        public double AverageCheckIntervalMs { get; set; }

        /// <summary>
        /// 最后检查时间
        /// </summary>
        public DateTime LastCheckTime { get; set; }

        /// <summary>
        /// 成功率
        /// </summary>
        public double SuccessRate => TotalChecks > 0 ? (double)SuccessfulChecks / TotalChecks * 100 : 0;

        /// <summary>
        /// 监控运行时间
        /// </summary>
        public TimeSpan MonitoringDuration => DateTime.UtcNow - MonitoringStarted;

        public override string ToString()
        {
            return $"Monitoring: {MonitoringDuration:dd\\:hh\\:mm\\:ss}, " +
                   $"Checks: {TotalChecks} ({SuccessRate:F1}% success), " +
                   $"Clients: {OnlineClientsCount}/{RegisteredClientsCount}, " +
                   $"Issues: {ResolvedIssuesCount}/{TotalIssuesDetected} resolved";
        }
    }

    /// <summary>
    /// 网络类型枚举
    /// </summary>
    public enum NetworkType
    {
        Unknown,
        Ethernet,
        WiFi,
        Cellular,
        VPN,
        Loopback
    }

    /// <summary>
    /// 网络质量枚举
    /// </summary>
    public enum NetworkQuality
    {
        Unknown,
        Excellent,
        Good,
        Fair,
        Poor,
        Disconnected
    }

    /// <summary>
    /// 连接状态枚举
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Failed,
        Timeout
    }

    /// <summary>
    /// 网络问题类型枚举
    /// </summary>
    public enum NetworkIssueType
    {
        ConnectionLost,
        HighLatency,
        PacketLoss,
        LowBandwidth,
        DNSResolutionFailure,
        TimeoutError,
        AuthenticationFailure,
        ServerUnreachable,
        PortBlocked,
        FirewallBlocked
    }

    /// <summary>
    /// 网络问题严重程度枚举
    /// </summary>
    public enum NetworkIssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
}