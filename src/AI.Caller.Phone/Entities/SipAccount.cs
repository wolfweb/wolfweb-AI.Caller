using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI.Caller.Phone.Entities {
    public class SipAccount {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string SipUsername { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string SipPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string SipServer { get; set; } = string.Empty;

        public int? DefaultLineId { get; set; }
        public virtual SipLine? DefaultLine { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<User> Users { get; set; } = new List<User>();
        
        public virtual ICollection<SipLine> AvailableLines { get; set; } = new List<SipLine>();
    }
}