using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    public class User {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; }

        [Required]
        public string Password { get; set; }

        public string? Email { get; set; }

        public string? PhoneNumber { get; set; }

        public string? DisplayName { get; set; }

        public string? Bio { get; set; }

        public bool AutoRecording { get; set; } = false;

        public DateTime? RegisteredAt { get; set; }

        public bool IsAdmin { get; set; } = false;

        public bool EnableAI { get; set; } = false;

        public virtual ICollection<Contact> Contacts { get; set; } = new List<Contact>();

        public int? SipAccountId { get; set; }
        public virtual SipAccount? SipAccount { get; set; }

        public bool SipRegistered { get; set; }
    }
}