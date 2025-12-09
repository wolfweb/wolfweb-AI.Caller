/**
 * PhoneApp 命名空间初始化
 * 统一管理所有电话应用相关的类和实例
 * 
 * 使用方式:
 * - 类定义: window.PhoneApp.ClassName = ClassName;
 * - 实例: window.PhoneApp.instanceName = new ClassName();
 * - 工具函数: window.PhoneApp.Utils.functionName = function() {};
 */

// 创建全局命名空间
window.PhoneApp = window.PhoneApp || {
    // 核心类
    CallStateManager: null,
    ErrorRecovery: null,
    PerformanceMonitor: null,
    
    // 网络通信
    SignalRManager: null,
    WebRTCManager: null,
    
    // UI管理
    UIManager: null,
    UIHelper: null,
    UIValidator: null,
    
    // 通话控制
    HangupHandler: null,
    HangupEvents: null,
    LineSelector: null,
    
    // 媒体管理
    RingtoneManager: null,
    SimpleRecordingManager: null,
    AudioRecorder: null, // 新增：在线录音
    
    // 应用实例
    phoneApp: null,
    
    // 全局实例
    ringtoneManager: null,
    performanceMonitor: null,
    
    // 工具函数
    Utils: {},
    
    // 错误类型
    ErrorTypes: null,
    
    // 全局状态（向后兼容）
    isRecording: false,
    isAutoRecording: true,
    callStartTime: null,
    callTimerInterval: null,
    recordingStartTime: null,
    recordingTimerInterval: null
};

console.log('PhoneApp 命名空间已初始化');
