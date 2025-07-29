namespace AI.Caller.Phone.CallRouting.Models
{
    /// <summary>
    /// 通话处理策略枚举
    /// </summary>
    public enum CallHandlingStrategy
    {
        /// <summary>
        /// Web到Web通话
        /// </summary>
        WebToWeb,

        /// <summary>
        /// Web到非Web通话
        /// </summary>
        WebToNonWeb,

        /// <summary>
        /// 非Web到Web通话
        /// </summary>
        NonWebToWeb,

        /// <summary>
        /// 非Web到非Web通话
        /// </summary>
        NonWebToNonWeb,

        /// <summary>
        /// 备用处理
        /// </summary>
        Fallback,

        /// <summary>
        /// 拒绝通话
        /// </summary>
        Reject
    }
}