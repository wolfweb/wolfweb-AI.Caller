using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// DTMF输入类型
    /// </summary>
    public enum DtmfInputType {
        /// <summary>
        /// 纯数字
        /// </summary>
        [Display(Name = "纯数字")]
        Numeric = 1,

        /// <summary>
        /// 身份证号
        /// </summary>
        [Display(Name = "身份证号")]
        IdCard = 2,

        /// <summary>
        /// 手机号
        /// </summary>
        [Display(Name = "手机号")]
        PhoneNumber = 3,

        /// <summary>
        /// 自定义
        /// </summary>
        [Display(Name = "自定义")]
        Custom = 4
    }
}
