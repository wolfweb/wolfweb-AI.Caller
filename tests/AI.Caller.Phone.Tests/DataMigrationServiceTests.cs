using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AI.Caller.Phone.Tests;

public class DataMigrationServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly DataMigrationService _migrationService;
    private readonly ILogger<DataMigrationService> _logger;

    public DataMigrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var configuration = new ConfigurationBuilder().Build();
        _context = new AppDbContext(options, configuration);
        _context.Database.EnsureCreated();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DataMigrationService>();
        _migrationService = new DataMigrationService(_context, _logger);
    }

    [Fact]
    public async Task MigrateUserSipDataAsync_WithUsersHavingSipInfo_ShouldCreateSipAccountsAndAssociate()
    {
        // Arrange
        var user1 = new User
        {
            Username = "user1",
            Password = "pass1",
            SipUsername = "sip1@example.com",
            SipPassword = "sippass1"
        };

        var user2 = new User
        {
            Username = "user2", 
            Password = "pass2",
            SipUsername = "sip1@example.com", // 相同的SIP账户
            SipPassword = "sippass1"
        };

        var user3 = new User
        {
            Username = "user3",
            Password = "pass3",
            SipUsername = "sip2@example.com", // 不同的SIP账户
            SipPassword = "sippass2"
        };

        _context.Users.AddRange(user1, user2, user3);
        await _context.SaveChangesAsync();

        // Act
        await _migrationService.MigrateUserSipDataAsync();

        // Assert
        var sipAccounts = await _context.SipAccounts.Include(s => s.Users).ToListAsync();
        Assert.Equal(2, sipAccounts.Count); // 应该创建2个SipAccount

        var sipAccount1 = sipAccounts.First(s => s.SipUsername == "sip1@example.com");
        Assert.Equal(2, sipAccount1.Users.Count); // user1和user2应该关联到同一个SipAccount

        var sipAccount2 = sipAccounts.First(s => s.SipUsername == "sip2@example.com");
        Assert.Equal(1, sipAccount2.Users.Count); // user3应该关联到另一个SipAccount

        // 验证用户的SipAccountId已设置
        var updatedUsers = await _context.Users.ToListAsync();
        Assert.All(updatedUsers, u => Assert.NotNull(u.SipAccountId));
    }

    [Fact]
    public async Task MigrateUserSipDataAsync_WithNoSipInfo_ShouldNotCreateSipAccounts()
    {
        // Arrange
        var user = new User
        {
            Username = "user1",
            Password = "pass1"
            // 没有SIP信息
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        await _migrationService.MigrateUserSipDataAsync();

        // Assert
        var sipAccounts = await _context.SipAccounts.ToListAsync();
        Assert.Empty(sipAccounts);

        var updatedUser = await _context.Users.FirstAsync();
        Assert.Null(updatedUser.SipAccountId);
    }

    [Fact]
    public async Task ValidateMigrationAsync_AfterSuccessfulMigration_ShouldReturnTrue()
    {
        // Arrange
        var user = new User
        {
            Username = "user1",
            Password = "pass1",
            SipUsername = "sip1@example.com",
            SipPassword = "sippass1"
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
    public async Task RollbackMigrationAsync_ShouldRemoveAllSipAccountsAndAssociations()
    {
        // Arrange
        var user = new User
        {
            Username = "user1",
            Password = "pass1",
            SipUsername = "sip1@example.com",
            SipPassword = "sippass1"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _migrationService.MigrateUserSipDataAsync();

        // Verify migration worked
        Assert.NotEmpty(await _context.SipAccounts.ToListAsync());
        Assert.NotNull((await _context.Users.FirstAsync()).SipAccountId);

        // Act
        await _migrationService.RollbackMigrationAsync();

        // Assert
        var sipAccounts = await _context.SipAccounts.ToListAsync();
        Assert.Empty(sipAccounts);

        var updatedUser = await _context.Users.FirstAsync();
        Assert.Null(updatedUser.SipAccountId);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}