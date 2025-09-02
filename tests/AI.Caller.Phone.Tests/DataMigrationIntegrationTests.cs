using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AI.Caller.Phone.Tests;

public class DataMigrationIntegrationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly DataMigrationService _migrationService;
    private readonly ILogger<DataMigrationService> _logger;
    private readonly string _testDbPath;

    public DataMigrationIntegrationTests()
    {
        // 使用真实的SQLite数据库进行集成测试
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_migration_{Guid.NewGuid()}.db");
        
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;

        var configuration = new ConfigurationBuilder().Build();
        _context = new AppDbContext(options, configuration);
        _context.Database.EnsureCreated();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DataMigrationService>();
        _migrationService = new DataMigrationService(_context, _logger);
    }

    [Fact]
    public async Task FullMigrationWorkflow_WithRealDatabase_ShouldSucceed()
    {
        // Arrange - 创建模拟现有生产数据的场景
        var existingUsers = new[]
        {
            new User
            {
                Username = "agent1",
                Password = "pass1",
                SipUsername = "1001@sip.company.com",
                SipPassword = "sippass1",
                SipRegistered = true,
                RegisteredAt = DateTime.UtcNow.AddDays(-1)
            },
            new User
            {
                Username = "agent2", 
                Password = "pass2",
                SipUsername = "1001@sip.company.com", // 相同SIP账户
                SipPassword = "sippass1",
                SipRegistered = false
            },
            new User
            {
                Username = "agent3",
                Password = "pass3",
                SipUsername = "1002@sip.company.com", // 不同SIP账户
                SipPassword = "sippass2",
                SipRegistered = true,
                RegisteredAt = DateTime.UtcNow.AddHours(-2)
            },
            new User
            {
                Username = "agent4",
                Password = "pass4",
                // 没有SIP信息的用户
                SipRegistered = false
            }
        };

        _context.Users.AddRange(existingUsers);
        await _context.SaveChangesAsync();

        // 验证迁移前状态
        var usersBeforeMigration = await _context.Users.ToListAsync();
        var sipAccountsBeforeMigration = await _context.SipAccounts.ToListAsync();
        
        Assert.Equal(4, usersBeforeMigration.Count);
        Assert.Empty(sipAccountsBeforeMigration);
        Assert.All(usersBeforeMigration, u => Assert.Null(u.SipAccountId));

        // Act - 执行迁移
        await _migrationService.MigrateUserSipDataAsync();

        // Assert - 验证迁移结果
        var usersAfterMigration = await _context.Users.Include(u => u.SipAccount).ToListAsync();
        var sipAccountsAfterMigration = await _context.SipAccounts.Include(s => s.Users).ToListAsync();

        // 应该创建2个SipAccount（1001和1002）
        Assert.Equal(2, sipAccountsAfterMigration.Count);

        var sipAccount1001 = sipAccountsAfterMigration.First(s => s.SipUsername == "1001@sip.company.com");
        var sipAccount1002 = sipAccountsAfterMigration.First(s => s.SipUsername == "1002@sip.company.com");

        // 验证SipAccount属性
        Assert.Equal("sippass1", sipAccount1001.SipPassword);
        Assert.Equal("localhost", sipAccount1001.SipServer);
        Assert.True(sipAccount1001.IsActive);
        Assert.Equal(2, sipAccount1001.Users.Count); // agent1和agent2

        Assert.Equal("sippass2", sipAccount1002.SipPassword);
        Assert.Equal(1, sipAccount1002.Users.Count); // agent3

        // 验证用户关联
        var agent1 = usersAfterMigration.First(u => u.Username == "agent1");
        var agent2 = usersAfterMigration.First(u => u.Username == "agent2");
        var agent3 = usersAfterMigration.First(u => u.Username == "agent3");
        var agent4 = usersAfterMigration.First(u => u.Username == "agent4");

        Assert.Equal(sipAccount1001.Id, agent1.SipAccountId);
        Assert.Equal(sipAccount1001.Id, agent2.SipAccountId);
        Assert.Equal(sipAccount1002.Id, agent3.SipAccountId);
        Assert.Null(agent4.SipAccountId); // 没有SIP信息的用户不应该关联

        // 验证原始字段保持不变（向后兼容）
        Assert.Equal("1001@sip.company.com", agent1.SipUsername);
        Assert.Equal("sippass1", agent1.SipPassword);
    }

    [Fact]
    public async Task MigrationValidation_AfterSuccessfulMigration_ShouldPass()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Password = "testpass",
            SipUsername = "test@sip.com",
            SipPassword = "testpass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _migrationService.MigrateUserSipDataAsync();

        // Act
        var isValid = await _migrationService.ValidateMigrationAsync();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task MigrationRollback_ShouldRestoreOriginalState()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Password = "testpass", 
            SipUsername = "test@sip.com",
            SipPassword = "testpass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 执行迁移
        await _migrationService.MigrateUserSipDataAsync();
        
        // 验证迁移成功
        var sipAccountsAfterMigration = await _context.SipAccounts.ToListAsync();
        var userAfterMigration = await _context.Users.FirstAsync();
        Assert.NotEmpty(sipAccountsAfterMigration);
        Assert.NotNull(userAfterMigration.SipAccountId);

        // Act - 执行回滚
        await _migrationService.RollbackMigrationAsync();

        // Assert - 验证回滚结果
        var sipAccountsAfterRollback = await _context.SipAccounts.ToListAsync();
        var userAfterRollback = await _context.Users.FirstAsync();
        
        Assert.Empty(sipAccountsAfterRollback);
        Assert.Null(userAfterRollback.SipAccountId);
        
        // 原始SIP字段应该保持不变
        Assert.Equal("test@sip.com", userAfterRollback.SipUsername);
        Assert.Equal("testpass", userAfterRollback.SipPassword);
    }

    [Fact]
    public async Task RepeatedMigration_ShouldBeIdempotent()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Password = "testpass",
            SipUsername = "test@sip.com", 
            SipPassword = "testpass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act - 执行多次迁移
        await _migrationService.MigrateUserSipDataAsync();
        await _migrationService.MigrateUserSipDataAsync();
        await _migrationService.MigrateUserSipDataAsync();

        // Assert - 结果应该相同
        var sipAccounts = await _context.SipAccounts.ToListAsync();
        var users = await _context.Users.ToListAsync();

        Assert.Single(sipAccounts); // 只应该有一个SipAccount
        Assert.Single(users);
        Assert.NotNull(users.First().SipAccountId);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }
}