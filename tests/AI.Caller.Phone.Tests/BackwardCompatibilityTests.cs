using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using AI.Caller.Core.Models;
using AI.Caller.Core;
using SIPSorcery.Net;

namespace AI.Caller.Phone.Tests;

public class BackwardCompatibilityTests : IDisposable {
    private readonly AppDbContext _context;
    private readonly SipService _sipService;
    private readonly ApplicationContext _applicationContext;
    private readonly string _testDbPath;
    private readonly Mock<IHubContext<WebRtcHub>> _mockHubContext;
    private readonly Mock<SIPTransportManager> _mockSipTransportManager;

    public BackwardCompatibilityTests() {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_compatibility_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;

        var configuration = new ConfigurationBuilder().Build();
        _context = new AppDbContext(options, configuration);
        _context.Database.EnsureCreated();

        _mockHubContext = new Mock<IHubContext<WebRtcHub>>();
        _mockSipTransportManager = new Mock<SIPTransportManager>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<AppDbContext>(_ => _context);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        _applicationContext = new ApplicationContext(serviceProvider);

        var webRtcSettings = new WebRTCSettings();
        var webRtcOptions = Options.Create(webRtcSettings);

        var logger = new Mock<ILogger<SipService>>().Object;

        var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(serviceProvider);
        mockServiceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _sipService = new SipService(
            logger,
            _context,
            _mockHubContext.Object,
            _applicationContext,
            _mockSipTransportManager.Object,
            webRtcOptions,
            mockServiceScopeFactory.Object
        );
    }

    [Fact]
    public async Task LegacyUser_WithOnlySipUsernamePassword_ShouldStillWork() {
        var sipAccount = new SipAccount {
            SipUsername = "legacy@sip.com",
            SipPassword = "legacypass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var legacyUser = new User {
            Id = 1,
            Username = "legacyuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(legacyUser);
        await _context.SaveChangesAsync();

        var result = await _sipService.RegisterUserAsync(legacyUser);

        Assert.True(result);

        var sipClient = _applicationContext.GetSipClientByUserId(legacyUser.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task MakeCallAsync_WithLegacySipUsername_ShouldWork() {
        var sipAccount = new SipAccount {
            SipUsername = "caller@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var legacyUser = new User {
            Id = 1,
            Username = "caller",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        _context.Users.Add(legacyUser);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(legacyUser);

        var sdpOffer = new RTCSessionDescriptionInit {
            type = RTCSdpType.offer,
            sdp = "v=0\r\no=- 123456 654321 IN IP4 192.168.1.100\r\n"
        };

        var result = await _sipService.MakeCallAsync("1234567890", legacyUser, sdpOffer);

        Assert.True(result.Success);
        Assert.Equal("呼叫已发起", result.Message);
    }

    [Fact]
    public async Task AnswerAsync_WithLegacyUsername_ShouldWork() {
        var sipAccount = new SipAccount {
            SipUsername = "answerer@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var legacyUser = new User {
            Id = 1,
            Username = "answerer",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        _context.Users.Add(legacyUser);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(legacyUser);

        var answerSdp = new RTCSessionDescriptionInit {
            type = RTCSdpType.answer,
            sdp = "v=0\r\no=- 123456 654321 IN IP4 192.168.1.100\r\n"
        };

        var result = await _sipService.AnswerAsync(legacyUser, answerSdp);

        Assert.IsType<bool>(result);
    }

    [Fact]
    public async Task HangupCallAsync_WithLegacySipUsername_ShouldWork() {
        var sipAccount = new SipAccount {
            SipUsername = "hangup@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var legacyUser = new User {
            Id = 1,
            Username = "hangupuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        _context.Users.Add(legacyUser);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(legacyUser);

        var result = await _sipService.HangupCallAsync(legacyUser, "User requested hangup");

        Assert.IsType<bool>(result);
    }

    [Fact]
    public async Task ApplicationContext_LegacyMethods_ShouldStillWork() {
        var sipAccount = new SipAccount {
            SipUsername = "context@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var legacyUser = new User {
            Id = 1,
            Username = "contextuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(legacyUser);
        await _context.SaveChangesAsync();

        var mockSipClient = new Mock<SIPClient>();

        _applicationContext.AddSipClient(legacyUser.Id, mockSipClient.Object);

        var retrievedClient = _applicationContext.GetSipClientByUserId(legacyUser.Id);
        Assert.NotNull(retrievedClient);

        var removed = _applicationContext.RemoveSipClient(legacyUser.Id);

        Assert.True(removed);

        var clientAfterRemoval = _applicationContext.GetSipClientByUserId(legacyUser.Id);
        Assert.Null(clientAfterRemoval);
    }

    [Fact]
    public async Task MixedUsers_LegacyAndNew_ShouldCoexist() {
        var sipAccount = new SipAccount {
            SipUsername = "shared@sip.com",
            SipPassword = "sharedpass",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var legacyUser = new User {
            Id = 1,
            Username = "legacyuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        var newUser = new User {
            Id = 2,
            Username = "newuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.AddRange(legacyUser, newUser);
        await _context.SaveChangesAsync();

        var legacyResult = await _sipService.RegisterUserAsync(legacyUser);
        var newResult = await _sipService.RegisterUserAsync(newUser);

        Assert.True(legacyResult);
        Assert.True(newResult);

        var legacySipClient = _applicationContext.GetSipClientByUserId(legacyUser.Id);
        var newSipClient = _applicationContext.GetSipClientByUserId(newUser.Id);

        Assert.NotNull(legacySipClient);
        Assert.NotNull(newSipClient);
        Assert.NotSame(legacySipClient, newSipClient);
    }

    [Fact]
    public async Task DataMigration_ShouldPreserveLegacyData() {
        // Arrange - 创建一些用户和SIP账户数据
        var sipAccounts = new List<SipAccount>
        {
            new SipAccount
            {
                SipUsername = "user1@sip.com",
                SipPassword = "pass1",
                SipServer = "sip.server.com",
                IsActive = true
            },
            new SipAccount
            {
                SipUsername = "user2@sip.com",
                SipPassword = "pass2",
                SipServer = "sip.server.com",
                IsActive = true
            },
            new SipAccount
            {
                SipUsername = "user3@sip.com",
                SipPassword = "pass3",
                SipServer = "sip.server.com",
                IsActive = true
            }
        };

        _context.SipAccounts.AddRange(sipAccounts);
        await _context.SaveChangesAsync();

        var legacyUsers = new List<User>
        {
            new User
            {
                Id = 1,
                Username = "user1",
                SipAccountId = sipAccounts[0].Id
            },
            new User
            {
                Id = 2,
                Username = "user2",
                SipAccountId = sipAccounts[1].Id
            },
            new User
            {
                Id = 3,
                Username = "user3",
                SipAccountId = sipAccounts[0].Id
            }
        };

        _context.Users.AddRange(legacyUsers);
        await _context.SaveChangesAsync();

        using var scope = new ServiceCollection()
            .AddScoped<AppDbContext>(_ => _context)
            .BuildServiceProvider()
            .CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DataMigrationService>>();
        var serviceScopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
        var migrationService = new DataMigrationService(logger, serviceScopeFactory);
        await migrationService.MigrateUserSipDataToSipAccountAsync();

        var migratedUsers = await _context.Users
            .Include(u => u.SipAccount)
            .ToListAsync();

        Assert.All(migratedUsers, user => Assert.NotNull(user.SipAccount));

        var user1 = migratedUsers.First(u => u.Username == "user1");
        var user3 = migratedUsers.First(u => u.Username == "user3");
        Assert.Equal(user1.SipAccountId, user3.SipAccountId);

        Assert.All(migratedUsers, user => {
            Assert.NotNull(user.SipAccount);
            Assert.NotNull(user.SipAccount.SipUsername);
            Assert.NotNull(user.SipAccount.SipPassword);
        });
    }

    [Fact]
    public async Task APIResponseFormat_ShouldRemainConsistent() {
        var sipAccount = new SipAccount {
            SipUsername = "api@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Id = 1,
            Username = "apiuser",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(user);

        var sdpOffer = new RTCSessionDescriptionInit {
            type = RTCSdpType.offer,
            sdp = "v=0\r\no=- 123456 654321 IN IP4 192.168.1.100\r\n"
        };

        var callResult = await _sipService.MakeCallAsync("1234567890", user, sdpOffer);

        Assert.IsType<(bool Success, string Message)>(callResult);
        Assert.True(callResult.Success);
        Assert.IsType<string>(callResult.Message);
        Assert.NotEmpty(callResult.Message);
    }

    [Fact]
    public async Task UserSession_ShouldMaintainCompatibility() {
        var sipAccount = new SipAccount {
            SipUsername = "session@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Id = 1,
            Username = "sessionuser",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _applicationContext.UpdateUserActivityByUserId(user.Id);

        var session = _applicationContext.UserSessions.Values.FirstOrDefault();
        Assert.NotNull(session);
        Assert.Equal(user.Id, session.UserId);
        Assert.True(session.IsOnline);
    }

    public void Dispose() {
        _context.Dispose();
        if (File.Exists(_testDbPath)) {
            File.Delete(_testDbPath);
        }
    }
}