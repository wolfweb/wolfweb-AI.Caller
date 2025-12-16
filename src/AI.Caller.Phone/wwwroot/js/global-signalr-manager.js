/**
 * 全局SignalR管理器
 * 提供全局SignalR连接，供所有页面使用
 */
class GlobalSignalRManager {
    constructor() {
        this.connection = null;
        this.isConnected = false;
        this.eventHandlers = new Map(); // 存储各页面的事件处理器
        this.heartbeatInterval = null;
        this.heartbeatIntervalMs = 5000;
        
        // 自动初始化
        this.initialize();
    }

    /**
     * 初始化全局SignalR连接
     */
    async initialize() {
        console.log('初始化全局SignalR连接...');
        
        try {
            this.createConnection();
            this.setupConnectionEvents();
            await this.startConnection();
            this.startHeartbeat();
            
            console.log('全局SignalR连接已建立');
        } catch (error) {
            console.error('全局SignalR连接失败:', error);
        }
    }

    /**
     * 创建SignalR连接
     */
    createConnection() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/webrtc")
            .configureLogging(signalR.LogLevel.Information)
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: retryContext => {
                    const attempt = retryContext.previousRetryCount;
                    const delay = Math.min(1000 * Math.pow(2, attempt), 30000);
                    console.log(`全局SignalR重连尝试 #${attempt + 1}，等待 ${delay}ms`);
                    return delay;
                }
            })
            .build();
    }

    /**
     * 设置连接事件
     */
    setupConnectionEvents() {
        this.connection.onreconnecting((error) => {
            this.isConnected = false;
            console.log('全局SignalR连接断开，正在重连...', error);
            this.notifyAllHandlers('connectionStateChanged', { state: 'reconnecting', error });
        });

        this.connection.onreconnected((connectionId) => {
            this.isConnected = true;
            console.log(`全局SignalR重连成功，连接ID: ${connectionId}`);
            this.notifyAllHandlers('connectionStateChanged', { state: 'connected', connectionId });
            this.startHeartbeat();
        });

        this.connection.onclose((error) => {
            this.isConnected = false;
            console.error('全局SignalR连接已关闭:', error);
            this.notifyAllHandlers('connectionStateChanged', { state: 'disconnected', error });
            this.stopHeartbeat();
        });

        // 设置通用事件监听
        this.setupCallEvents();
        this.setupRecordingEvents();
    }

    /**
     * 设置通话事件监听
     */
    setupCallEvents() {
        // 来电事件
        this.connection.on("inCalling", (callData) => {
            console.log('全局SignalR收到来电:', callData);
            this.notifyAllHandlers('inCalling', callData);
        });

        // 通话状态事件
        this.connection.on("callTrying", (data) => {
            this.notifyAllHandlers('callTrying', data);
        });

        this.connection.on("callRinging", (data) => {
            this.notifyAllHandlers('callRinging', data);
        });

        this.connection.on("callAnswered", (data) => {
            this.notifyAllHandlers('callAnswered', data);
        });

        this.connection.on("answered", (data) => {
            this.notifyAllHandlers('answered', data);
        });

        this.connection.on("callTimeout", (data) => {
            this.notifyAllHandlers('callTimeout', data);
        });

        this.connection.on("sdpAnswered", (answerDesc) => {
            this.notifyAllHandlers('sdpAnswered', answerDesc);
        });

        this.connection.on("receiveIceCandidate", (candidate) => {
            this.notifyAllHandlers('receiveIceCandidate', candidate);
        });
    }

    /**
     * 设置录音事件监听
     */
    setupRecordingEvents() {
        this.connection.on("recordingStarted", (data) => {
            this.notifyAllHandlers('recordingStarted', data);
        });

        this.connection.on("recordingStopped", (data) => {
            this.notifyAllHandlers('recordingStopped', data);
        });

        this.connection.on("recordingError", (data) => {
            this.notifyAllHandlers('recordingError', data);
        });

        this.connection.on("recordingStatusUpdate", (data) => {
            this.notifyAllHandlers('recordingStatusUpdate', data);
        });
    }

    /**
     * 启动连接
     */
    async startConnection() {
        try {
            await this.connection.start();
            this.isConnected = true;
            console.log('全局SignalR连接已启动');
        } catch (error) {
            console.error('全局SignalR连接启动失败:', error);
            throw error;
        }
    }

    /**
     * 开始心跳
     */
    startHeartbeat() {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
        }

        this.heartbeatInterval = setInterval(async () => {
            if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
                try {
                    await this.connection.invoke("Heartbeat");
                    console.log('全局SignalR心跳发送成功');
                } catch (error) {
                    console.warn('全局SignalR心跳发送失败:', error);
                }
            }
        }, this.heartbeatIntervalMs);

        console.log(`全局SignalR心跳定时器已启动，间隔: ${this.heartbeatIntervalMs}ms`);
    }

    /**
     * 停止心跳
     */
    stopHeartbeat() {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
            console.log('全局SignalR心跳定时器已停止');
        }
    }

    /**
     * 注册事件处理器
     */
    registerEventHandler(handlerId, eventType, callback) {
        if (!this.eventHandlers.has(handlerId)) {
            this.eventHandlers.set(handlerId, new Map());
        }
        
        const handlerEvents = this.eventHandlers.get(handlerId);
        if (!handlerEvents.has(eventType)) {
            handlerEvents.set(eventType, []);
        }
        
        handlerEvents.get(eventType).push(callback);
        console.log(`已注册事件处理器: ${handlerId} -> ${eventType}`);
    }

    /**
     * 注销事件处理器
     */
    unregisterEventHandler(handlerId) {
        if (this.eventHandlers.has(handlerId)) {
            this.eventHandlers.delete(handlerId);
            console.log(`已注销事件处理器: ${handlerId}`);
        }
    }

    /**
     * 通知所有处理器
     */
    notifyAllHandlers(eventType, data) {
        this.eventHandlers.forEach((handlerEvents, handlerId) => {
            if (handlerEvents.has(eventType)) {
                const callbacks = handlerEvents.get(eventType);
                callbacks.forEach(callback => {
                    try {
                        callback(data);
                    } catch (error) {
                        console.error(`事件处理器 ${handlerId} 处理 ${eventType} 事件时出错:`, error);
                    }
                });
            }
        });
    }

    /**
     * 调用Hub方法
     */
    async invoke(methodName, ...args) {
        if (!this.isConnected) {
            throw new Error('SignalR连接未建立');
        }
        
        try {
            return await this.connection.invoke(methodName, ...args);
        } catch (error) {
            console.error(`调用Hub方法 ${methodName} 失败:`, error);
            throw error;
        }
    }

    /**
     * 获取连接状态
     */
    getConnectionState() {
        return {
            isConnected: this.isConnected,
            state: this.connection?.state || 'Not initialized',
            handlersCount: this.eventHandlers.size
        };
    }

    /**
     * 清理资源
     */
    cleanup() {
        console.log('清理全局SignalR资源...');
        
        this.stopHeartbeat();
        
        if (this.connection) {
            this.connection.stop();
        }
        
        this.eventHandlers.clear();
        this.isConnected = false;
        
        console.log('全局SignalR资源清理完成');
    }
}

// 创建全局实例
window.GlobalSignalRManager = GlobalSignalRManager;

// 页面加载完成后自动创建全局SignalR连接
document.addEventListener('DOMContentLoaded', () => {
    if (!window.globalSignalRManager) {
        window.globalSignalRManager = new GlobalSignalRManager();
        console.log('全局SignalR管理器已创建');
    }
});

// 页面卸载时清理资源
window.addEventListener('beforeunload', () => {
    if (window.globalSignalRManager) {
        window.globalSignalRManager.cleanup();
    }
});