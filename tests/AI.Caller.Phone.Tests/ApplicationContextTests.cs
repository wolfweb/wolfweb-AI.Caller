using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Phone.Tests;

public class ApplicationContextTests : IDisposable {
    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _context;
    private readonly ApplicationContext _applicationContext;
    private readonly string _testDbPath;

    public ApplicationContextTests() {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_appcontext_{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={_testDbPath}"));
        services.AddLogging(builder => builder.AddConsole());

        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<AppDbContext>();
        _context.Database.EnsureCreated();

        _applicationContext = new ApplicationContext(_serviceProvider);
    }

    public void Dispose() {
        _context?.Dispose();
        _serviceProvider?.Dispose();

        if (File.Exists(_testDbPath)) {
            try {
                File.Delete(_testDbPath);
            } catch {
                
            }
        }
    }

    [Fact]
    public async Task AddSipClient_WithValidSipUsername_ShouldStoreByUserId() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "sippass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "testuser",
            Password = "testpass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var mockSipClient = new Mock<SIPClient>();
        _applicationContext.AddSipClient(user.Id, mockSipClient.Object);

        var storedClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(storedClient);
        Assert.Equal(mockSipClient.Object, storedClient);

        Assert.True(_applicationContext.UserSessions.ContainsKey(user.Id));
        var session = _applicationContext.UserSessions[user.Id];
        Assert.Equal(user.Id, session.UserId);
        Assert.True(session.IsOnline);
    }

    [Fact]
    public async Task AddSipClientByUserId_ShouldStoreCorrectly() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "sippass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "testuser",
            Password = "testpass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var mockSipClient = new Mock<SIPClient>();

        _applicationContext.AddSipClientByUserId(user.Id, mockSipClient.Object);

        var storedClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(storedClient);
        Assert.Equal(mockSipClient.Object, storedClient);

        Assert.True(_applicationContext.UserSessions.ContainsKey(user.Id));
    }

    [Fact]
    public async Task RemoveSipClient_WithValidSipUsername_ShouldRemoveByUserId() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "sippass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "testuser",
            Password = "testpass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var mockSipClient = new Mock<SIPClient>();
        _applicationContext.AddSipClient(user.Id, mockSipClient.Object);

        var result = _applicationContext.RemoveSipClient(user.Id);

        Assert.True(result);
        var storedClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.Null(storedClient);

        if (_applicationContext.UserSessions.TryGetValue(user.Id, out var session)) {
            Assert.False(session.IsOnline);
        }

        mockSipClient.Verify(x => x.Shutdown(), Times.Once);
    }

    [Fact]
    public async Task RemoveSipClientByUserId_ShouldRemoveCorrectly() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "sippass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "testuser",
            Password = "testpass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var mockSipClient = new Mock<SIPClient>();
        _applicationContext.AddSipClientByUserId(user.Id, mockSipClient.Object);

        var result = _applicationContext.RemoveSipClientByUserId(user.Id);

        Assert.True(result);
        var storedClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.Null(storedClient);

        mockSipClient.Verify(x => x.Shutdown(), Times.Once);
    }

    [Fact]
    public async Task UpdateUserActivity_WithValidSipUsername_ShouldUpdateByUserId() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "sippass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "testuser",
            Password = "testpass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var mockSipClient = new Mock<SIPClient>();
        _applicationContext.AddSipClient(user.Id, mockSipClient.Object);

        var initialActivity = _applicationContext.UserSessions[user.Id].LastActivity;
        await Task.Delay(10); 

        _applicationContext.UpdateUserActivityByUserId(user.Id);

        var updatedActivity = _applicationContext.UserSessions[user.Id].LastActivity;
        Assert.True(updatedActivity > initialActivity);
    }

    [Fact]
    public async Task UpdateUserActivityByUserId_ShouldUpdateCorrectly() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "sippass",
            SipServer = "sip.server.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "testuser",
            Password = "testpass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }
}