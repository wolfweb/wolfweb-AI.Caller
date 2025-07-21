using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AI.Caller.Phone;

public class AppDbContext : DbContext
{
    private readonly IConfiguration _configuration;

    public DbSet<User>       Users       { get; set; }
    public DbSet<Contact>    Contacts    { get; set; } // New property for contact management
    public DbSet<SipSetting> SipSettings { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSqlite("Data Source=app.db");
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, IConfiguration configuration) : base(options)
    {
        _configuration = configuration;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure User entity
        var defaultUserSection = _configuration.GetSection("UserSettings:DefaultUser");
        if (defaultUserSection.Exists())
        {
            var defaultUser = new User
            {
                Id = 1,
                Username = defaultUserSection["Username"],
                Password = defaultUserSection["Password"]
            };
            
            modelBuilder.Entity<User>().HasData(defaultUser);
        }
    }
}