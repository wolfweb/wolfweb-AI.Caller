namespace AI.Caller.Phone.CallRouting.Models
{
    /// <summary>
    /// 通话处理策略枚举
    /// </summary>
    public enum CallHandlingStrategy
    {
        /// <summary>
        /// 拒绝呼叫
        /// </summary>
        Reject,

        /// <summary>
        /// Web客户端到Web客户端
        /// </summary>
        WebToWeb,

        /// <summary>
        /// Web客户端到非Web客户端（如PSTN）
        /// </summary>
        WebToNonWeb,

        /// <summary>
        /// 非Web客户端到Web客户端
        /// </summary>
        NonWebToWeb,

        /// <summary>
        /// 非Web客户端到非Web客户端
        /// </summary>
        NonWebToNonWeb,

        /// <summary>
        /// 备用处理策略
        /// </summary>
        Fallback
    }
}