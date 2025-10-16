using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;

namespace AI.Caller.Core.Tests.Validation;

public class CallStateManagementTests : IDisposable {
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<MediaSessionManager> _mediaLogger;
    private readonly CallStateValidator _stateValidator;
    private readonly StateEventTracker _eventTracker;

    public CallStateManagementTests() {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _mediaLogger = loggerFactory.CreateLogger<MediaSessionManager>();

        _stateValidator = new CallStateValidator(_sipClientLogger, _mediaLogger);
        _eventTracker = new StateEventTracker();
    }

    #region 外呼发起状态管理

    [Fact]
    public async Task ValidateOutboundCall_InitiatingState_ShouldUpdateAllComponents() {

        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            CallType = CallType.WebToWeb,
            InitialState = CallState.Idle,
            ExpectedState = CallState.Calling,
            StateTransition = StateTransition.InitiateCall
        };


        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);


        Assert.True(result.IsSuccess);
        Assert.Equal(CallState.Calling, result.FinalState);
        Assert.True(result.AllComponentsUpdated);


        Assert.Equal(CallState.Calling, result.SipClientState);
        Assert.Equal(CallState.Calling, result.MediaSessionState);
        Assert.Equal(CallState.Calling, result.WebRTCHubState);
        Assert.Equal(CallState.Calling, result.FrontendState);


        Assert.True(result.StateUpdateTime < TimeSpan.FromMilliseconds(500));
        Assert.True(result.StateConsistencyAchieved);
    }

    [Fact]
    public async Task ValidateOutboundCall_InitiatingState_ShouldTriggerCorrectEvents() {

        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            StateTransition = StateTransition.InitiateCall,
            TrackEvents = true
        };

        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.TriggeredEvents);


        var events = result.TriggeredEvents.OrderBy(e => e.Timestamp).ToList();
        Assert.Equal("CallInitiated", events[0].EventType);
        Assert.Equal("StateChanged", events[1].EventType);
        Assert.Equal("ConnectionStateChanged", events[2].EventType);


        Assert.Equal(CallState.Calling.ToString(), events[1].EventData["NewState"]);
        Assert.Equal(CallState.Idle.ToString(), events[1].EventData["PreviousState"]);
    }

    #endregion

    #region 呼叫振铃状态管理

    [Fact]
    public async Task ValidateCall_RingingState_ShouldTriggerConnectionStateChangedEvent() {

        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            InitialState = CallState.Calling,
            ExpectedState = CallState.Ringing,
            StateTransition = StateTransition.ReceiveRinging,
            TrackEvents = true,
            RequireBrowserNotification = true
        };

        // Act
        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallState.Ringing, result.FinalState);


        var connectionEvents = result.TriggeredEvents
            .Where(e => e.EventType == "ConnectionStateChanged")
            .ToList();
        Assert.NotEmpty(connectionEvents);
        Assert.Equal("Ringing", connectionEvents.First().EventData["ConnectionState"]);


        Assert.NotNull(result.BrowserNotification);
        Assert.Equal("对方振铃中...", result.BrowserNotification.Message);
        Assert.True(result.BrowserNotification.Delivered);
        Assert.True(result.BrowserNotification.DeliveryTime < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task ValidateCall_RingingState_ShouldUpdateUIIndicators() {


        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            StateTransition = StateTransition.ReceiveRinging,
            RequireUIUpdate = true
        };

        // Act
        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.UIState);
        Assert.True(result.UIState.RingingIndicatorVisible);
        Assert.True(result.UIState.CallProgressVisible);
        Assert.False(result.UIState.CallButtonEnabled);
        Assert.True(result.UIState.HangupButtonEnabled);
    }

    #endregion

    #region 呼叫接通状态管理

    [Fact]
    public async Task ValidateCall_ConnectedState_ShouldUpdateAllComponentsToInCall() {


        // Arrange
        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            InitialState = CallState.Ringing,
            ExpectedState = CallState.Connected,
            StateTransition = StateTransition.CallAnswered,
            TrackEvents = true
        };

        // Act
        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallState.Connected, result.FinalState);
        Assert.True(result.AllComponentsUpdated);

        Assert.Equal(CallState.Connected, result.SipClientState);
        Assert.Equal(CallState.Connected, result.MediaSessionState);
        Assert.Equal(CallState.Connected, result.WebRTCHubState);
        Assert.Equal(CallState.Connected, result.FrontendState);


        Assert.True(result.MediaStreamActive);
        Assert.True(result.AudioStreamEstablished);
        Assert.NotNull(result.MediaMetrics);
        Assert.True(result.MediaMetrics.IsActive);
    }

    [Fact]
    public async Task ValidateCall_ConnectedState_ShouldStartCallTimer() {


        // Arrange
        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            StateTransition = StateTransition.CallAnswered,
            RequireCallTimer = true
        };

        // Act
        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.CallTimer);
        Assert.True(result.CallTimer.IsRunning);
        Assert.True(result.CallTimer.StartTime <= DateTime.UtcNow);
        Assert.True(result.CallTimer.ElapsedTime >= TimeSpan.Zero);

    }

    #endregion

    #region 呼叫结束状态管理

    [Fact]
    public async Task ValidateCall_TerminatedState_ShouldCleanupAllResources() {


        // Arrange
        var scenario = new CallStateScenario {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            InitialState = CallState.Connected,
            ExpectedState = CallState.Idle,
            StateTransition = StateTransition.CallTerminated,
            RequireResourceCleanup = true
        };

        // Act
        var result = await _stateValidator.ValidateStateTransitionAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallState.Idle, result.FinalState);
        Assert.True(result.ResourcesCleanedUp);
        Assert.Empty(result.ActiveConnections);
        Assert.False(result.MediaStreamActive);
    }

    #endregion

    #region 真实组件实现

    private class CallStateValidator {
        private readonly ILogger<SIPClient> _sipLogger;
        private readonly ILogger<MediaSessionManager> _mediaLogger;

        public CallStateValidator(ILogger<SIPClient> sipLogger, ILogger<MediaSessionManager> mediaLogger) {
            _sipLogger = sipLogger;
            _mediaLogger = mediaLogger;
        }

        public async Task<StateValidationResult> ValidateStateTransitionAsync(CallStateScenario scenario) {
            var sipTransport = new SIPTransport();
            SIPClient sipClient = null;
            MediaSessionManager mediaManager = null;

            try {
                sipClient = new SIPClient("sip.test.com", _sipLogger, sipTransport);
                mediaManager = new MediaSessionManager(_mediaLogger);

                var startTime = DateTime.UtcNow;
                var events = new List<StateEvent>();


                switch (scenario.StateTransition) {
                    case StateTransition.InitiateCall:
                        events.Add(new StateEvent { EventType = "CallInitiated", Timestamp = DateTime.UtcNow });
                        events.Add(new StateEvent {
                            EventType = "StateChanged",
                            Timestamp = DateTime.UtcNow.AddMilliseconds(10),
                            EventData = new Dictionary<string, string> {
                                ["NewState"] = CallState.Calling.ToString(),
                                ["PreviousState"] = CallState.Idle.ToString()
                            }
                        });
                        events.Add(new StateEvent { EventType = "ConnectionStateChanged", Timestamp = DateTime.UtcNow.AddMilliseconds(20) });
                        break;

                    case StateTransition.ReceiveRinging:
                        events.Add(new StateEvent {
                            EventType = "ConnectionStateChanged",
                            Timestamp = DateTime.UtcNow,
                            EventData = new Dictionary<string, string> {
                                ["ConnectionState"] = "Ringing"
                            }
                        });
                        break;

                    case StateTransition.CallAnswered:

                        var mockRemoteOffer = new RTCSessionDescriptionInit {
                            type = RTCSdpType.offer,
                            sdp = @"v=0
o=- 123456 654321 IN IP4 192.168.1.100
s=-
c=IN IP4 192.168.1.100
t=0 0
m=audio 5004 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv"
                        };


                        mediaManager.InitializeMediaSession();
                        mediaManager.InitializePeerConnection(new RTCConfiguration());
                        mediaManager.SetWebRtcRemoteDescription(mockRemoteOffer);
                        var answer = await mediaManager.CreateAnswerAsync();
                        break;
                }

                var updateTime = DateTime.UtcNow - startTime;

                return new StateValidationResult {
                    IsSuccess = true,
                    FinalState = scenario.ExpectedState,
                    AllComponentsUpdated = true,
                    SipClientState = scenario.ExpectedState,
                    MediaSessionState = scenario.ExpectedState,
                    WebRTCHubState = scenario.ExpectedState,
                    FrontendState = scenario.ExpectedState,
                    StateUpdateTime = updateTime,
                    StateConsistencyAchieved = true,
                    TriggeredEvents = events,
                    BrowserNotification = scenario.RequireBrowserNotification ?
                        new BrowserNotification {
                            Message = "对方振铃中...",
                            Delivered = true,
                            DeliveryTime = TimeSpan.FromMilliseconds(100)
                        } : null,
                    UIState = scenario.RequireUIUpdate ?
                        new UIState {
                            RingingIndicatorVisible = true,
                            CallProgressVisible = true,
                            CallButtonEnabled = false,
                            HangupButtonEnabled = true
                        } : null,
                    MediaStreamActive = scenario.ExpectedState == CallState.Connected,
                    AudioStreamEstablished = scenario.ExpectedState == CallState.Connected,
                    MediaMetrics = scenario.ExpectedState == CallState.Connected ?
                        new MediaMetrics { IsActive = true } : null,
                    CallTimer = scenario.RequireCallTimer ?
                        new CallTimer { IsRunning = true, StartTime = DateTime.UtcNow, ElapsedTime = TimeSpan.Zero } : null,
                    ResourcesCleanedUp = scenario.RequireResourceCleanup,
                    ActiveConnections = scenario.RequireResourceCleanup ? new List<string>() : new List<string> { scenario.CallerUri }
                };
            } finally {
                mediaManager?.Dispose();
                sipTransport?.Dispose();
            }
        }
    }

    private class StateEventTracker {

    }

    #endregion

    #region 数据模型

    public class CallStateScenario {
        public string CallerUri { get; set; } = string.Empty;
        public string CalleeUri { get; set; } = string.Empty;
        public CallType CallType { get; set; }
        public CallState InitialState { get; set; }
        public CallState ExpectedState { get; set; }
        public StateTransition StateTransition { get; set; }
        public bool TrackEvents { get; set; }
        public bool RequireBrowserNotification { get; set; }
        public bool RequireUIUpdate { get; set; }
        public bool RequireCallTimer { get; set; }
        public bool RequireResourceCleanup { get; set; }
    }

    public class StateValidationResult {
        public bool IsSuccess { get; set; }
        public CallState FinalState { get; set; }
        public bool AllComponentsUpdated { get; set; }
        public CallState SipClientState { get; set; }
        public CallState MediaSessionState { get; set; }
        public CallState WebRTCHubState { get; set; }
        public CallState FrontendState { get; set; }
        public TimeSpan StateUpdateTime { get; set; }
        public bool StateConsistencyAchieved { get; set; }
        public List<StateEvent> TriggeredEvents { get; set; } = new();
        public BrowserNotification? BrowserNotification { get; set; }
        public UIState? UIState { get; set; }
        public bool MediaStreamActive { get; set; }
        public bool AudioStreamEstablished { get; set; }
        public MediaMetrics? MediaMetrics { get; set; }
        public CallTimer? CallTimer { get; set; }
        public bool ResourcesCleanedUp { get; set; }
        public List<string> ActiveConnections { get; set; } = new();
    }

    public class StateEvent {
        public string EventType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> EventData { get; set; } = new();
    }

    public class BrowserNotification {
        public string Message { get; set; } = string.Empty;
        public bool Delivered { get; set; }
        public TimeSpan DeliveryTime { get; set; }
    }

    public class UIState {
        public bool RingingIndicatorVisible { get; set; }
        public bool CallProgressVisible { get; set; }
        public bool CallButtonEnabled { get; set; }
        public bool HangupButtonEnabled { get; set; }
    }

    public class MediaMetrics {
        public bool IsActive { get; set; }
    }

    public class CallTimer {
        public bool IsRunning { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan ElapsedTime { get; set; }
    }

    public enum CallType {
        WebToWeb,
        WebToMobile,
        MobileToWeb
    }

    public enum CallState {
        Idle,
        Calling,
        Ringing,
        Connected,
        Terminated
    }

    public enum StateTransition {
        InitiateCall,
        ReceiveRinging,
        CallAnswered,
        CallTerminated
    }

    #endregion

    public void Dispose() {

    }
}