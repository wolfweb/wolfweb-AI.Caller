using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Entities;

public class TtsVariable {
    public int Id { get; set; }

    [Display(Name = "变量名")]
    [Comment("在模板中使用的占位符，不含大括号, e.g., 'CustomerName'")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "描述")]
    public string? Description { get; set; }

    public virtual ICollection<TtsTemplate> TtsTemplates { get; set; } = new List<TtsTemplate>();
}