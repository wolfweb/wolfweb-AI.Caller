using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone;

public class AppDbContext : DbContext {
    private readonly IConfiguration _configuration;

    public DbSet<User> Users { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<SipAccount> SipAccounts { get; set; }
    public DbSet<Models.Recording> Recordings { get; set; }
    public DbSet<TtsTemplate> TtsTemplates { get; set; }
    public DbSet<TtsVariable> TtsVariables { get; set; }
    public DbSet<AICustomerServiceSettings> AICustomerServiceSettings { get; set; }
    public DbSet<CallLog> CallLogs { get; set; }
    public DbSet<BatchCallJob> BatchCallJobs { get; set; }
    public DbSet<Ringtone> Ringtones { get; set; }
    public DbSet<UserRingtoneSettings> UserRingtoneSettings { get; set; }
    public DbSet<SystemRingtoneSettings> SystemRingtoneSettings { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSqlite("Data Source=app.db");
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, IConfiguration configuration) : base(options) {
        _configuration = configuration;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SipAccount>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SipUsername).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SipPassword).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SipServer).IsRequired().HasMaxLength(200);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");

            entity.HasIndex(e => e.SipUsername).IsUnique().HasDatabaseName("IX_SipAccounts_SipUsername");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_SipAccounts_IsActive");
        });

        modelBuilder.Entity<User>(entity => {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.SipAccount)
                  .WithMany(s => s.Users)
                  .HasForeignKey(e => e.SipAccountId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.Password).HasMaxLength(255);

            entity.Property(e => e.AutoRecording).HasDefaultValue(false);

            entity.HasIndex(e => e.Username).HasDatabaseName("IX_Users_Username");

            entity.HasIndex(e => e.SipAccountId).HasDatabaseName("IX_Users_SipAccountId");
            entity.HasIndex(e => e.SipRegistered).HasDatabaseName("IX_Users_SipRegistered");
        });

        modelBuilder.Entity<Contact>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(50);

            entity.HasOne<User>()
                  .WithMany(u => u.Contacts)
                  .HasForeignKey(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId).HasDatabaseName("IX_Contacts_UserId");
            entity.HasIndex(e => e.PhoneNumber).HasDatabaseName("IX_Contacts_PhoneNumber");
        });

        var defaultUserSection = _configuration.GetSection("UserSettings:DefaultUser");
        if (defaultUserSection.Exists()) {
            var defaultUser = new User {
                Id = 1,
                Username = defaultUserSection["Username"],
                Password = defaultUserSection["Password"]
            };

            modelBuilder.Entity<User>().HasData(defaultUser);
        }

        modelBuilder.Entity<Models.Recording>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SipUsername).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);

            entity.HasIndex(e => e.UserId).HasDatabaseName("IX_Recordings_UserId");
            entity.HasIndex(e => e.StartTime).HasDatabaseName("IX_Recordings_StartTime");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_Recordings_Status");
        });

        modelBuilder.Entity<TtsTemplate>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Content).IsRequired();

            entity.HasMany(p => p.Variables)
                  .WithMany(p => p.TtsTemplates)
                  .UsingEntity("TtsTemplateVariable");
        });

        modelBuilder.Entity<TtsVariable>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<BatchCallJob>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobName).IsRequired();
            entity.Property(e => e.OriginalFileName).IsRequired();
            entity.Property(e => e.StoredFilePath).IsRequired();

            entity.HasOne(e => e.CreatedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.CreatedByUserId);
        });

        modelBuilder.Entity<CallLog>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PhoneNumber).IsRequired();

            entity.HasOne(e => e.BatchCallJob)
                  .WithMany(j => j.CallLogs)
                  .HasForeignKey(e => e.BatchCallJobId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Ringtone>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(20);
            entity.Property(e => e.IsSystem).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.Uploader)
                  .WithMany()
                  .HasForeignKey(e => e.UploadedBy)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UploadedBy).HasDatabaseName("IX_Ringtones_UploadedBy");
            entity.HasIndex(e => e.Type).HasDatabaseName("IX_Ringtones_Type");
            entity.HasIndex(e => e.IsSystem).HasDatabaseName("IX_Ringtones_IsSystem");
        });

        modelBuilder.Entity<UserRingtoneSettings>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.IncomingRingtone)
                  .WithMany()
                  .HasForeignKey(e => e.IncomingRingtoneId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RingbackTone)
                  .WithMany()
                  .HasForeignKey(e => e.RingbackToneId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("IX_UserRingtoneSettings_UserId");
        });

        modelBuilder.Entity<SystemRingtoneSettings>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now')");

            entity.HasOne(e => e.DefaultIncomingRingtone)
                  .WithMany()
                  .HasForeignKey(e => e.DefaultIncomingRingtoneId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DefaultRingbackTone)
                  .WithMany()
                  .HasForeignKey(e => e.DefaultRingbackToneId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.UpdatedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.UpdatedBy)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}