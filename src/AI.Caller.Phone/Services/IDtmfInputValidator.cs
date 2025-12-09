using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

/// <summary>
/// DTMF输入验证器接口
/// </summary>
public interface IDtmfInputValidator {
    /// <summary>
    /// 验证输入
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="template">输入模板</param>
    /// <returns>验证结果和错误消息</returns>
    (bool IsValid, string? ErrorMessage) Validate(string input, DtmfInputTemplate template);
}

/// <summary>
/// 数字验证器
/// </summary>
public class NumericValidator : IDtmfInputValidator {
    public (bool IsValid, string? ErrorMessage) Validate(string input, DtmfInputTemplate template) {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "输入不能为空");

        if (input.Length < template.MinLength)
            return (false, $"输入长度不能少于{template.MinLength}位");

        if (input.Length > template.MaxLength)
            return (false, $"输入长度不能超过{template.MaxLength}位");

        if (!input.All(char.IsDigit))
            return (false, "只能输入数字");

        return (true, null);
    }
}

/// <summary>
/// 电话号码验证器
/// </summary>
public class PhoneNumberValidator : IDtmfInputValidator {
    public (bool IsValid, string? ErrorMessage) Validate(string input, DtmfInputTemplate template) {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "电话号码不能为空");

        // 移除可能的分隔符
        var cleaned = new string(input.Where(char.IsDigit).ToArray());

        if (cleaned.Length < 7 || cleaned.Length > 15)
            return (false, "电话号码长度必须在7-15位之间");

        return (true, null);
    }
}

/// <summary>
/// 身份证号验证器
/// </summary>
public class IdCardValidator : IDtmfInputValidator {
    public (bool IsValid, string? ErrorMessage) Validate(string input, DtmfInputTemplate template) {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "身份证号不能为空");

        if (input.Length != 18)
            return (false, "身份证号必须是18位");

        if (!input.Take(17).All(char.IsDigit))
            return (false, "身份证号前17位必须是数字");

        var lastChar = input[17];
        if (!char.IsDigit(lastChar) && lastChar != 'X' && lastChar != 'x')
            return (false, "身份证号最后一位必须是数字或X");

        return (true, null);
    }
}

/// <summary>
/// 日期验证器（YYYYMMDD格式）
/// </summary>
public class DateValidator : IDtmfInputValidator {
    public (bool IsValid, string? ErrorMessage) Validate(string input, DtmfInputTemplate template) {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "日期不能为空");

        if (input.Length != 8)
            return (false, "日期格式必须是YYYYMMDD（8位数字）");

        if (!input.All(char.IsDigit))
            return (false, "日期只能包含数字");

        if (!int.TryParse(input.Substring(0, 4), out var year) || year < 1900 || year > 2100)
            return (false, "年份无效");

        if (!int.TryParse(input.Substring(4, 2), out var month) || month < 1 || month > 12)
            return (false, "月份无效");

        if (!int.TryParse(input.Substring(6, 2), out var day) || day < 1 || day > 31)
            return (false, "日期无效");

        return (true, null);
    }
}

/// <summary>
/// 菜单选项验证器
/// </summary>
public class MenuOptionValidator : IDtmfInputValidator {
    public (bool IsValid, string? ErrorMessage) Validate(string input, DtmfInputTemplate template) {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "请选择一个选项");

        if (input.Length != 1)
            return (false, "只能选择一个选项");

        if (!char.IsDigit(input[0]))
            return (false, "选项必须是数字");

        return (true, null);
    }
}
