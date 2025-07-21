using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Models;
public class RegisterModel {
    [Required(ErrorMessage = "用户名不能为空")]
    [Display(Name = "用户名")]
    public string Username { get; set; }

    [Required(ErrorMessage = "密码不能为空")]
    [StringLength(100, ErrorMessage = "{0}必须至少包含{2}个字符。", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "密码")]
    public string Password { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "确认密码")]
    [Compare("Password", ErrorMessage = "密码和确认密码不匹配。")]
    public string ConfirmPassword { get; set; }
}
