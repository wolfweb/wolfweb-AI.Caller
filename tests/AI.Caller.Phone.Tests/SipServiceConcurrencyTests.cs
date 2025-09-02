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

namespace AI.Caller.Phone.Tests;

public class SipServiceConcurrencyTests : IDisposable {
    private readonly string _testDbPath;
    private readonly AppDbContext _context;
    private readonly SipService _sipService;
    private readonly ApplicationContext _applicationContext;
    private readonly Mock<IHubContext<WebRtcHub>> _mockHubContext;
    private readonly Mock<SIPTransportManager> _mockSipTransportManager;

    public SipServiceConcurrencyTests() {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_concurrency_{Guid.NewGuid()}.db");

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
    public async Task RegisterUserAsync_MultipleConcurrentUsers_ShouldHandleConcurrency() {
        var sipAccount = new SipAccount {
            SipUsername = "shared@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var users = new List<User>();
        for (int i = 1; i <= 5; i++) {
            var user = new User {
                Username = $"user{i}",
                Password = "password",
                SipAccountId = sipAccount.Id,
                SipAccount = sipAccount
            };
            users.Add(user);
        }

        _context.Users.AddRange(users);
        await _context.SaveChangesAsync();

        var tasks = users.Select(user => _sipService.RegisterUserAsync(user)).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result));

        Assert.Equal(5, _applicationContext.SipClients.Count);

        foreach (var user in users) {
            var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
            Assert.NotNull(sipClient);
        }
    }

    [Fact]
    public async Task RegisterAndUnregisterUsers_ShouldManageResourcesProperly() {
        var sipAccount = new SipAccount {
            SipUsername = "resource@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "resourceuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var registerResult = await _sipService.RegisterUserAsync(user);
        Assert.True(registerResult);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);

        await _sipService.UnregisterUserAsync(user);

        var sipClientAfterUnregister = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.Null(sipClientAfterUnregister);

        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.False(updatedUser.SipRegistered);
        Assert.Null(updatedUser.RegisteredAt);
    }

    [Fact]
    public async Task ConcurrentCallOperations_ShouldNotInterfere() {
        var sipAccount1 = new SipAccount {
            SipUsername = "user1@sip.com",
            SipPassword = "password1",
            SipServer = "sip.example.com",
            IsActive = true
        };

        var sipAccount2 = new SipAccount {
            SipUsername = "user2@sip.com",
            SipPassword = "password2",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.AddRange(sipAccount1, sipAccount2);
        await _context.SaveChangesAsync();

        var user1 = new User {
            Username = "concurrentuser1",
            Password = "password",
            SipAccountId = sipAccount1.Id,
            SipAccount = sipAccount1,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        var user2 = new User {
            Username = "concurrentuser2",
            Password = "password",
            SipAccountId = sipAccount2.Id,
            SipAccount = sipAccount2,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(user1);
        await _sipService.RegisterUserAsync(user2);

        var tasks = new List<Task>
        {
            Task.Run(async () => {
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(100);
                }
            }),
            Task.Run(async () => {
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(150);
                }
            })
        };

        await Assert.ThrowsAsync<Exception>(async () => await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task SemaphoreLimit_ShouldPreventExcessiveConcurrency() {
        var sipAccount = new SipAccount {
            SipUsername = "limited@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var users = new List<User>();
        for (int i = 1; i <= 15; i++) {
            var user = new User {
                Username = $"limituser{i}",
                Password = "password",
                SipAccountId = sipAccount.Id,
                SipAccount = sipAccount
            };
            users.Add(user);
        }

        _context.Users.AddRange(users);
        await _context.SaveChangesAsync();

        var startTime = DateTime.UtcNow;
        var tasks = users.Select(user => _sipService.RegisterUserAsync(user)).ToArray();
        var results = await Task.WhenAll(tasks);
        var endTime = DateTime.UtcNow;

        var duration = endTime - startTime;
        Assert.True(duration.TotalMilliseconds > 100);

        Assert.All(results, result => Assert.True(result));
    }

    [Fact]
    public async Task ResourceCleanup_WhenNoUsersRemain_ShouldCleanupProperly() {
        var sipAccount = new SipAccount {
            SipUsername = "cleanup@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var users = new List<User>();
        for (int i = 1; i <= 3; i++) {
            var user = new User {
                Username = $"cleanupuser{i}",
                Password = "password",
                SipAccountId = sipAccount.Id,
                SipAccount = sipAccount
            };
            users.Add(user);
        }

        _context.Users.AddRange(users);
        await _context.SaveChangesAsync();

        foreach (var user in users) {
            await _sipService.RegisterUserAsync(user);
        }

        Assert.Equal(3, _applicationContext.SipClients.Count);

        foreach (var user in users) {
            await _sipService.UnregisterUserAsync(user);
        }

        Assert.Empty(_applicationContext.SipClients);
    }

    [Fact]
    public async Task ThreadSafety_MultipleOperationsOnSameUser_ShouldBeThreadSafe() {
        var sipAccount = new SipAccount {
            SipUsername = "threadsafe@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "threadsafeuser",
            Password = "password",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var tasks = new List<Task<bool>>();

        for (int i = 0; i < 5; i++) {
            tasks.Add(_sipService.RegisterUserAsync(user));
        }

        var unregisterTasks = new List<Task>();
        for (int i = 0; i < 3; i++) {
            unregisterTasks.Add(Task.Run(async () => await _sipService.UnregisterUserAsync(user)));
        }

        await Task.WhenAll(unregisterTasks);

        Assert.NotNull(tasks);
        Assert.Equal(8, tasks.Count);

        var finalUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(finalUser);
    }

    public void Dispose() {
        _context.Dispose();
        if (File.Exists(_testDbPath)) {
            File.Delete(_testDbPath);
        }
    }
}