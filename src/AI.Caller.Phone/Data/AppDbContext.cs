using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AI.Caller.Phone;

public class AppDbContext : DbContext
{
    private readonly IConfiguration _configuration;

    public DbSet<User>            Users            { get; set; }
    public DbSet<Contact>         Contacts         { get; set; } // New property for contact management
    public DbSet<SipSetting>      SipSettings      { get; set; }
    public DbSet<CallRecording>   CallRecordings   { get; set; }
    public DbSet<RecordingSetting> RecordingSettings { get; set; }

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
        
        // Configure CallRecording entity
        modelBuilder.Entity<CallRecording>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CallId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CallerNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CalleeNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.AudioFormat).HasMaxLength(10);
            
            // Configure relationships
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            
            // Configure indexes
            entity.HasIndex(e => e.UserId).HasDatabaseName("IX_CallRecordings_UserId");
            entity.HasIndex(e => e.StartTime).HasDatabaseName("IX_CallRecordings_StartTime");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_CallRecordings_Status");
            entity.HasIndex(e => e.CallId).HasDatabaseName("IX_CallRecordings_CallId");
        });
        
        // Configure RecordingSetting entity
        modelBuilder.Entity<RecordingSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.AudioFormat).HasMaxLength(10);
            
            // Configure relationships
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            
            // Ensure one setting per user
            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("IX_RecordingSettings_UserId");
        });
    }
}