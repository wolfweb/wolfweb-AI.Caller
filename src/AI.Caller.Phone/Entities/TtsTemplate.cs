using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Entities;

public class TtsTemplate {
    public int Id { get; set; }

    [Display(Name = "模板名称")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "内容模板")]
    [Comment("包含占位符的内容模板, e.g., '您好{CustomerName}，欢迎使用我们的服务。'")]
    [Required]
    public string Content { get; set; } = string.Empty;

    [Display(Name = "是否激活")]
    [DefaultValue(true)]
    public bool IsActive { get; set; } = true;

    [Display(Name = "播放次数")]
    [DefaultValue(1)]
    public int PlayCount { get; set; } = 1;

    [Display(Name = "播放后挂断")]
    [DefaultValue(true)]
    public bool HangupAfterPlay { get; set; } = true;

    public virtual ICollection<TtsVariable> Variables { get; set; } = new List<TtsVariable>();
}