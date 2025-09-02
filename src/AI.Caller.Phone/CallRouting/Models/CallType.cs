namespace AI.Caller.Phone.CallRouting.Models {
    /// <summary>
    /// 来电类型枚举
    /// </summary>
    public enum CallType {
        /// <summary>
        /// 呼出应答 - 对之前发起的呼叫的应答
        /// </summary>
        OutboundResponse,

        /// <summary>
        /// 新呼入 - 全新的来电
        /// </summary>
        InboundCall,

        /// <summary>
        /// Web到Web呼叫
        /// </summary>
        WebToWeb,

        /// <summary>
        /// Web到手机呼叫
        /// </summary>
        WebToMobile,

        /// <summary>
        /// 手机到Web呼叫
        /// </summary>
        MobileToWeb,

        /// <summary>
        /// 手机到手机呼叫
        /// </summary>
        MobileToMobile
    }
}