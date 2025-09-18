using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AI.Caller.Phone;

public class AppDbContext : DbContext {
    private readonly IConfiguration _configuration;

    public DbSet<User> Users { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<SipSetting> SipSettings { get; set; }
    public DbSet<SipAccount> SipAccounts { get; set; }
    public DbSet<Models.Recording> Recordings { get; set; }
    public DbSet<TtsCallDocument> TtsCallDocuments { get; set; }
    public DbSet<TtsCallRecord> TtsCallRecords { get; set; }
    public DbSet<InboundTemplate> InboundTemplates { get; set; }

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

        // TTS外呼文档配置
        modelBuilder.Entity<TtsCallDocument>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<int>();

            entity.HasIndex(e => e.UserId).HasDatabaseName("IX_TtsCallDocuments_UserId");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_TtsCallDocuments_Status");
            entity.HasIndex(e => e.UploadTime).HasDatabaseName("IX_TtsCallDocuments_UploadTime");
        });

        // TTS外呼记录配置
        modelBuilder.Entity<TtsCallRecord>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Gender).HasMaxLength(10);
            entity.Property(e => e.AddressTemplate).HasMaxLength(50);
            entity.Property(e => e.TtsContent).IsRequired();
            entity.Property(e => e.CallStatus).HasConversion<int>();
            entity.Property(e => e.FailureReason).HasMaxLength(500);

            entity.HasOne(e => e.Document)
                  .WithMany(d => d.CallRecords)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DocumentId).HasDatabaseName("IX_TtsCallRecords_DocumentId");
            entity.HasIndex(e => e.PhoneNumber).HasDatabaseName("IX_TtsCallRecords_PhoneNumber");
            entity.HasIndex(e => e.CallStatus).HasDatabaseName("IX_TtsCallRecords_CallStatus");
        });

        // 呼入模板配置
        modelBuilder.Entity<InboundTemplate>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.WelcomeScript).IsRequired();
            entity.Property(e => e.ResponseRules);

            entity.HasIndex(e => e.UserId).HasDatabaseName("IX_InboundTemplates_UserId");
            entity.HasIndex(e => e.IsDefault).HasDatabaseName("IX_InboundTemplates_IsDefault");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_InboundTemplates_IsActive");
        });
    }
}