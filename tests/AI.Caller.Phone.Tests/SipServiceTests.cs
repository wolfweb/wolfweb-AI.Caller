using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SIPSorcery.Net;
using System.Threading.Tasks;
using Xunit;

namespace AI.Caller.Phone.Tests;

public class SipServiceTests : IDisposable {
    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _context;
    private readonly SipService _sipService;
    private readonly ApplicationContext _applicationContext;
    private readonly Mock<IHubContext<WebRtcHub>> _mockHubContext;
    private readonly Mock<SIPTransportManager> _mockSipTransportManager;
    private readonly string _testDbPath;

    public SipServiceTests() {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_sipservice_{Guid.NewGuid()}.db");

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

        _mockHubContext = new Mock<IHubContext<WebRtcHub>>();
        _mockSipTransportManager = new Mock<SIPTransportManager>();

        var webRTCSettings = Options.Create(new WebRTCSettings());
        var logger = _serviceProvider.GetRequiredService<ILogger<SipService>>();
        var serviceScopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _sipService = new SipService(
            logger,
            _context,
            _mockHubContext.Object,
            _applicationContext,
            _mockSipTransportManager.Object,
            webRTCSettings,
            serviceScopeFactory
        );
    }

    [Fact]
    public async Task RegisterUserAsync_WithSipAccount_ShouldCreateSipClient() {
        var sipAccount = new SipAccount {
            SipUsername = "newaccount@sip.com",
            SipPassword = "newpassword",
            SipServer = "sip.newserver.com",
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

        var result = await _sipService.RegisterUserAsync(user);

        Assert.True(result);
        Assert.True(user.SipRegistered);
        Assert.NotNull(user.RegisteredAt);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task RegisterUserAsync_WithoutSipAccount_ShouldFail() {
        var user = new User {
            Username = "testuser",
            Password = "testpass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var result = await _sipService.RegisterUserAsync(user);

        Assert.False(result);
    }

    [Fact]
    public async Task MakeCallAsync_WithValidUser_ShouldUseUserIdInternally() {
        var sipAccount = new SipAccount {
            SipUsername = "caller@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "caller",
            Password = "pass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount,
            SipRegistered = true,
            RegisteredAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(user);

        var mockSdpOffer = new RTCSessionDescriptionInit {
            type = RTCSdpType.offer,
            sdp = "mock-sdp-offer"
        };

        var result = await _sipService.MakeCallAsync("1234567890", user, mockSdpOffer);

        Assert.True(result.Success);
        Assert.Equal("呼叫已发起", result.Message);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task AnswerAsync_WithValidUser_ShouldUseUserIdInternally() {
        var user = new User {
            Username = "answerer",
            Password = "pass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(user);

        var mockAnswerSdp = new RTCSessionDescriptionInit {
            type = RTCSdpType.answer,
            sdp = "mock-sdp-answer"
        };

        var result = await _sipService.AnswerAsync(user, mockAnswerSdp);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task HangupCallAsync_WithValidUser_ShouldUseUserIdInternally() {
        var user = new User {
            Username = "hangupuser",
            Password = "pass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(user);

        var result = await _sipService.HangupCallAsync(user, "Test hangup");

        Assert.True(result);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task MultipleUsersWithSameSipAccount_ShouldHaveIndependentSipClients() {
        var sharedSipAccount = new SipAccount {
            SipUsername = "shared@sip.com",
            SipPassword = "sharedpass",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sharedSipAccount);
        await _context.SaveChangesAsync();

        var user1 = new User {
            Username = "agent1",
            Password = "pass1",
            SipAccountId = sharedSipAccount.Id,
            SipAccount = sharedSipAccount
        };

        var user2 = new User {
            Username = "agent2",
            Password = "pass2",
            SipAccountId = sharedSipAccount.Id,
            SipAccount = sharedSipAccount
        };

        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        var result1 = await _sipService.RegisterUserAsync(user1);
        var result2 = await _sipService.RegisterUserAsync(user2);

        Assert.True(result1);
        Assert.True(result2);

        var sipClient1 = _applicationContext.GetSipClientByUserId(user1.Id);
        var sipClient2 = _applicationContext.GetSipClientByUserId(user2.Id);

        Assert.NotNull(sipClient1);
        Assert.NotNull(sipClient2);
        Assert.NotEqual(sipClient1, sipClient2);

        Assert.True(_applicationContext.UserSessions.ContainsKey(user1.Id));
        Assert.True(_applicationContext.UserSessions.ContainsKey(user2.Id));
    }

    [Fact]
    public async Task SendDtmfAsync_WithValidUser_ShouldUseUserIdLookup() {
        var user = new User {
            Username = "dtmfuser",
            Password = "pass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _sipService.RegisterUserAsync(user);

        var result = await _sipService.SendDtmfAsync(1, user);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task GetSecureContextReady_WithValidUser_ShouldUseUserIdLookup() {
        var user = new User {
            Username = "secureuser",
            Password = "pass"
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        await _sipService.RegisterUserAsync(user);

        var result = _sipService.GetSecureContextReady(user);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    [Fact]
    public async Task AddIceCandidate_WithValidUser_ShouldUseUserIdLookup() {
        var user = new User {
            Username = "iceuser",
            Password = "pass"
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        await _sipService.RegisterUserAsync(user);

        var mockCandidate = new RTCIceCandidateInit {
            candidate = "candidate:mock",
            sdpMid = "0",
            sdpMLineIndex = 0
        };

        _sipService.AddIceCandidate(user, mockCandidate);

        var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
        Assert.NotNull(sipClient);
    }

    public void Dispose() {
        _context.Dispose();
        _serviceProvider.Dispose();
        if (File.Exists(_testDbPath)) {
            File.Delete(_testDbPath);
        }
    }
}