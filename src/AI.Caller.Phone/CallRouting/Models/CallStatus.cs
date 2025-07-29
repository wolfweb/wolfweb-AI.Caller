namespace AI.Caller.Phone.CallRouting.Models
{
    /// <summary>
    /// 通话状态枚举
    /// </summary>
    public enum CallStatus
    {
        /// <summary>
        /// 已发起
        /// </summary>
        Initiated,

        /// <summary>
        /// 振铃中
        /// </summary>
        Ringing,

        /// <summary>
        /// 已应答
        /// </summary>
        Answered,

        /// <summary>
        /// 已结束
        /// </summary>
        Ended,

        /// <summary>
        /// 失败
        /// </summary>
        Failed
    }
}