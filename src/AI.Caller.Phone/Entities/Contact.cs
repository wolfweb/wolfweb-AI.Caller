using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    public class Contact {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string PhoneNumber { get; set; }

        // 外键属性
        public int? UserId { get; set; }

        // 导航属性
        public virtual User? User { get; set; }
    }
}