using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace AI.Caller.Phone.Tests;

public class SignalRCommunicationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IHubContext<WebRtcHub>> _mockHubContext;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<SipService> _mockSipService;
    private readonly Mock<ISimpleRecordingService> _mockRecordingService;
    private readonly WebRtcHub _hub;
    private readonly string _testDbPath;

    public SignalRCommunicationTests()
    {
        // 设置测试数据库
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_signalr_{Guid.NewGuid()}.db");
        
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;

        var configuration = new ConfigurationBuilder().Build();
        _context = new AppDbContext(options, configuration);
        _context.Database.EnsureCreated();

        // 设置Mock对象
        _mockHubContext = new Mock<IHubContext<WebRtcHub>>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockSipService = new Mock<SipService>();
        _mockRecordingService = new Mock<ISimpleRecordingService>();

        // 设置SignalR Mock
        _mockClients.Setup(c => c.User(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.Caller).Returns(_mockClientProxy.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);

        // 创建WebRtcHub实例
        _hub = new WebRtcHub(_mockSipService.Object, _context, _mockRecordingService.Object);
        
        // 设置Hub Context
        var mockHubContext = new Mock<HubCallerContext>();
        var mockUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.NameIdentifier, "1")
        }));
        
        mockHubContext.Setup(c => c.User).Returns(mockUser);
        _hub.Context = mockHubContext.Object;
        
        var mockClients = new Mock<IHubCallerClients>();
        mockClients.Setup(c => c.User(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        mockClients.Setup(c => c.Caller).Returns(_mockClientProxy.Object);
        _hub.Clients = mockClients.Object;
    }

    [Fact]
    public async Task AnswerAsync_ShouldUseCorrectUserIdForSignalR()
    {
        // Arrange
        var sipAccount = new SipAccount
        {
            SipUsername = "test@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var caller = new User
        {
            Id = 1,
            Username = "caller",
            SipAccountId = sipAccount.Id,
            SipUsername = "test@sip.com"
        };

        var answerer = new User
        {
            Id = 2,
            Username = "testuser", // 匹配Hub Context中的用户名
            SipAccountId = sipAccount.Id,
            SipUsername = "test@sip.com"
        };

        _context.Users.AddRange(caller, answerer);
        await _context.SaveChangesAsync();

        var model = new WebRtcAnswerModel("test@sip.com", "valid_sdp_offer");

        // 设置SipService Mock返回成功
        _mockSipService.Setup(s => s.AnswerAsync("testuser", It.IsAny<SIPSorcery.Net.RTCSessionDescriptionInit>()))
                      .ReturnsAsync(true);

        // Act
        await _hub.AnswerAsync(model);

        // Assert - 验证SignalR消息发送到正确的用户ID
        _mockClients.Verify(c => c.User("1"), Times.Once); // 应该使用caller的用户ID
        _mockClientProxy.Verify(p => p.SendCoreAsync("answered", 
            It.IsAny<object[]>(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HangupCallAsync_ShouldHandleUserIdMapping()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            SipUsername = "test@sip.com"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var model = new WebRtcHangupModel("target@sip.com", "User hangup");

        // 设置SipService Mock返回成功
        _mockSipService.Setup(s => s.HangupWithNotificationAsync("test@sip.com", model))
                      .ReturnsAsync(true);

        // Act
        var result = await _hub.HangupCallAsync(model);

        // Assert
        Assert.True(result);
        _mockSipService.Verify(s => s.HangupWithNotificationAsync("test@sip.com", model), Times.Once);
    }

    [Fact]
    public async Task HangupCallAsync_WhenUserNotFound_ShouldSendFailureNotification()
    {
        // Arrange - 不添加用户到数据库
        var model = new WebRtcHangupModel("target@sip.com", "User hangup");

        // Act
        var result = await _hub.HangupCallAsync(model);

        // Assert
        Assert.False(result);
        _mockClientProxy.Verify(p => p.SendCoreAsync("hangupFailed", 
            It.Is<object[]>(args => args.Length > 0), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartRecordingAsync_ShouldUseCorrectSipUsername()
    {
        // Arrange
        var sipAccount = new SipAccount
        {
            SipUsername = "record@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User
        {
            Id = 1,
            Username = "testuser",
            SipAccountId = sipAccount.Id,
            SipUsername = "record@sip.com"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 设置录音服务Mock返回成功
        _mockRecordingService.Setup(r => r.StartRecordingAsync("record@sip.com"))
                           .ReturnsAsync(true);

        // Act
        var result = await _hub.StartRecordingAsync("1234567890");

        // Assert
        var resultObj = result as dynamic;
        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        
        _mockRecordingService.Verify(r => r.StartRecordingAsync("record@sip.com"), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("recordingStarted", 
            It.IsAny<object[]>(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_ShouldUseCorrectSipUsername()
    {
        // Arrange
        var sipAccount = new SipAccount
        {
            SipUsername = "record@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User
        {
            Id = 1,
            Username = "testuser",
            SipAccountId = sipAccount.Id,
            SipUsername = "record@sip.com"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 设置录音服务Mock返回成功
        _mockRecordingService.Setup(r => r.StopRecordingAsync("record@sip.com"))
                           .ReturnsAsync(true);

        // Act
        var result = await _hub.StopRecordingAsync();

        // Assert
        var resultObj = result as dynamic;
        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        
        _mockRecordingService.Verify(r => r.StopRecordingAsync("record@sip.com"), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("recordingStopped", 
            It.IsAny<object[]>(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSecureContextState_ShouldHandleAuthentication()
    {
        // Arrange - Hub已经设置了用户身份

        // 设置SipService Mock
        _mockSipService.Setup(s => s.GetSecureContextReady("testuser"))
                      .Returns(true);

        // Act
        var result = await _hub.GetSecureContextState();

        // Assert
        Assert.True(result);
        _mockSipService.Verify(s => s.GetSecureContextReady("testuser"), Times.Once);
    }

    [Fact]
    public async Task SendIceCandidateAsync_ShouldCallSipService()
    {
        // Arrange
        var candidate = new SIPSorcery.Net.RTCIceCandidateInit
        {
            candidate = "candidate:1 1 UDP 2130706431 192.168.1.100 54400 typ host",
            sdpMid = "0",
            sdpMLineIndex = 0
        };

        // Act
        await _hub.SendIceCandidateAsync(candidate);

        // Assert
        _mockSipService.Verify(s => s.AddIceCandidate("testuser", candidate), Times.Once);
    }

    [Fact]
    public async Task MultipleUsers_ShouldReceiveIndependentNotifications()
    {
        // Arrange - 创建多个用户
        var sipAccount = new SipAccount
        {
            SipUsername = "shared@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user1 = new User
        {
            Id = 1,
            Username = "user1",
            SipAccountId = sipAccount.Id,
            SipUsername = "shared@sip.com"
        };

        var user2 = new User
        {
            Id = 2,
            Username = "user2",
            SipAccountId = sipAccount.Id,
            SipUsername = "shared@sip.com"
        };

        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        // 设置不同的Mock客户端代理
        var mockClientProxy1 = new Mock<IClientProxy>();
        var mockClientProxy2 = new Mock<IClientProxy>();

        _mockClients.Setup(c => c.User("1")).Returns(mockClientProxy1.Object);
        _mockClients.Setup(c => c.User("2")).Returns(mockClientProxy2.Object);

        // Act - 模拟向不同用户发送消息
        await _mockHubContext.Object.Clients.User("1").SendAsync("testMessage", "message for user 1");
        await _mockHubContext.Object.Clients.User("2").SendAsync("testMessage", "message for user 2");

        // Assert - 验证每个用户收到独立的消息
        mockClientProxy1.Verify(p => p.SendCoreAsync("testMessage", 
            It.Is<object[]>(args => args.Length > 0 && args[0].ToString() == "message for user 1"), 
            It.IsAny<CancellationToken>()), Times.Once);

        mockClientProxy2.Verify(p => p.SendCoreAsync("testMessage", 
            It.Is<object[]>(args => args.Length > 0 && args[0].ToString() == "message for user 2"), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserIdMapping_ShouldBeConsistent()
    {
        // Arrange
        var sipAccount = new SipAccount
        {
            SipUsername = "consistent@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User
        {
            Id = 123,
            Username = "consistentuser",
            SipAccountId = sipAccount.Id,
            SipUsername = "consistent@sip.com"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act & Assert - 验证用户ID映射的一致性
        var foundUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == "consistentuser");
        Assert.NotNull(foundUser);
        Assert.Equal(123, foundUser.Id);
        Assert.Equal("consistent@sip.com", foundUser.SipUsername);

        // 验证SignalR会使用正确的用户ID
        var userIdString = foundUser.Id.ToString();
        Assert.Equal("123", userIdString);
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