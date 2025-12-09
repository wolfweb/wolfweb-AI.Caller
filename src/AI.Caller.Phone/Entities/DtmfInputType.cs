namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// DTMF输入类型
    /// </summary>
    public enum DtmfInputType {
        /// <summary>
        /// 纯数字
        /// </summary>
        Numeric = 1,

        /// <summary>
        /// 身份证号
        /// </summary>
        IdCard = 2,

        /// <summary>
        /// 手机号
        /// </summary>
        PhoneNumber = 3,

        /// <summary>
        /// 自定义
        /// </summary>
        Custom = 4
    }
}
