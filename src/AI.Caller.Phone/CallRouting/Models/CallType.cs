namespace AI.Caller.Phone.CallRouting.Models
{
    /// <summary>
    /// 来电类型枚举
    /// </summary>
    public enum CallType
    {
        /// <summary>
        /// 呼出应答 - 对之前发起的呼叫的应答
        /// </summary>
        OutboundResponse,

        /// <summary>
        /// 新呼入 - 全新的来电
        /// </summary>
        InboundCall
    }
}