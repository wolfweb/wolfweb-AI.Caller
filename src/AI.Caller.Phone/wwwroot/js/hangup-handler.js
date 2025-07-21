/**
 * 挂断处理JavaScript模块
 * 处理通话挂断相关的前端逻辑和SignalR通信
 * 优化版本：使用现有UI元素而不是创建新元素
 */

// 配置对象
const HangupConfig = {
    // UI配置
    ui: {
        autoHideDelay: 3000,
        loadingAnimation: true,
        confirmBeforeHangup: false
    },
    
    // 网络配置
    network: {
        retryAttempts: 3,
        retryDelay: 1000,
        timeoutMs: 10000
    },
    
    // 调试配置
    debug: {
        enableLogging: true,
        logLevel: 'info',
        enableRemoteLogging: false
    }
};

// 操作日志记录器
class HangupLogger {
    static logOperation(operation, details) {
        if (!HangupConfig.debug.enableLogging) return;
        
        const logEntry = {
            timestamp: new Date().toISOString(),
            operation: operation,
            details: details,
            userAgent: navigator.userAgent,
            url: window.location.href
        };
        
        console.log('挂断操作日志:', logEntry);
        
        // 可选：发送到服务器进行分析
        if (HangupConfig.debug.enableRemoteLogging) {
            this.sendToServer(logEntry);
        }
    }
    
    static sendToServer(logEntry) {
        // 实现发送日志到服务器的逻辑
        fetch('/api/logs/hangup', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(logEntry)
        }).catch(error => {
            console.warn('发送日志到服务器失败:', error);
        });
    }
}

class HangupHandler {
    constructor(signalRConnection, uiElements, stateManager) {
        this.connection = signalRConnection;
        this.isHangingUp = false;
        
        // 接收现有UI元素引用
        this.uiElements = uiElements || {};
        this.stateManager = stateManager;
        
        // 初始化UI元素引用
        this.initializeElements();
        this.setupSignalRHandlers();
        this.setupEventListeners();
    }

    /**
     * 初始化DOM元素
     */
    initializeElements() {
        // 使用传入的UI元素引用，或者查找现有元素
        this.hangupButton = this.uiElements.hangupButton || document.getElementById('hangupButton');
        this.statusAlert = this.uiElements.statusAlert || document.getElementById('statusAlert');
        
        // 获取全局UI函数引用
        this.updateStatusFunc = this.uiElements.updateStatus || window.updateStatus;
        this.showCallInfo = this.uiElements.showCallInfo || window.showCallInfo;
        this.clearCallUI = this.uiElements.clearCallUI || window.clearCallUI;
        
        // 验证必要的UI元素是否存在
        if (!this.hangupButton) {
            console.warn('挂断按钮未找到，挂断功能可能无法正常工作');
        }
        
        if (!this.updateStatusFunc) {
            console.warn('updateStatus函数未找到，状态更新功能可能无法正常工作');
        }
    }

    /**
     * 设置SignalR事件处理器
     */
    setupSignalRHandlers() {
        // 挂断开始通知
        this.connection.on('hangupInitiated', (data) => {
            this.handleHangupInitiated(data);
        });

        // 通话结束通知
        this.connection.on('callEnded', (data) => {
            this.handleCallEnded(data);
        });

        // 挂断失败通知
        this.connection.on('hangupFailed', (data) => {
            this.handleHangupFailed(data);
        });

        // 对方挂断通知
        this.connection.on('remoteHangup', (data) => {
            this.handleRemoteHangup(data);
        });

        // 连接状态变化
        this.connection.onclose(() => {
            this.updateStatus('连接已断开', 'danger');
            this.disableHangupButton();
        });

        this.connection.onreconnected(() => {
            this.updateStatus('连接已恢复', 'success');
            this.hideStatus(3000);
        });
    }

    /**
     * 设置事件监听器
     */
    setupEventListeners() {
        // 存储事件处理器引用以便后续清理
        this.clickHandler = () => {
            if (this.canInitiateHangup()) {
                this.initiateHangup();
            }
        };

        this.keydownHandler = (event) => {
            if (event.ctrlKey && event.key === 'h' && this.canInitiateHangup()) {
                event.preventDefault();
                this.initiateHangup();
            }
        };

        if (this.hangupButton) {
            this.hangupButton.addEventListener('click', this.clickHandler);
        }

        // 键盘快捷键 (Ctrl+H)
        document.addEventListener('keydown', this.keydownHandler);
    }

    /**
     * 发起挂断请求
     */
    async initiateHangup(reason = null) {
        if (this.isHangingUp) {
            console.log('挂断操作正在进行中，忽略重复请求');
            return;
        }

        try {
            this.isHangingUp = true;
            
            // 通过StateManager设置挂断状态
            if (this.stateManager && typeof this.stateManager.setState === 'function') {
                this.stateManager.setState('ENDING');
            }
            
            this.disableHangupButton();
            this.updateStatus('正在挂断...', 'warning');

            // 调用SignalR Hub方法
            const result = await this.connection.invoke('HangupCallAsync', reason);
            
            if (!result) {
                // 如果服务端返回false，等待hangupFailed事件处理
                console.log('挂断请求返回false，等待错误处理');
            }
        } catch (error) {
            console.error('发起挂断请求时发生错误:', error);
            this.handleHangupFailed({
                message: `挂断请求失败: ${error.message}`,
                timestamp: new Date().toISOString()
            });
        }
    }

    /**
     * 处理挂断开始通知
     */
    handleHangupInitiated(data) {
        console.log('收到挂断开始通知:', data);
        this.updateStatus('正在挂断通话...', 'warning');
        this.disableHangupButton();
        
        // 显示进度指示器
        this.showHangupProgress();
    }

    /**
     * 处理通话结束通知
     */
    handleCallEnded(data) {
        console.log('收到通话结束通知:', data);
        this.isHangingUp = false;
        this.hideHangupProgress();
        
        // 使用现有的clearCallUI函数清理UI（包含状态重置）
        if (this.clearCallUI && typeof this.clearCallUI === 'function') {
            this.clearCallUI();
        } else if (typeof window.clearCallUI === 'function') {
            window.clearCallUI();
        }
        
        // 确保状态管理器重置到空闲状态（clearCallUI中已处理，但双重保险）
        if (this.stateManager && typeof this.stateManager.resetToIdle === 'function') {
            this.stateManager.resetToIdle();
        }
        
        // 触发自定义事件
        this.dispatchCallEndedEvent(data);
        
        console.log('通话结束处理完成，系统已重置到就绪状态');
    }

    /**
     * 处理挂断失败通知
     */
    handleHangupFailed(data) {
        console.error('收到挂断失败通知:', data);
        this.isHangingUp = false;
        this.updateStatus(`挂断失败: ${data.message}`, 'danger');
        this.enableHangupButton();
        this.hideHangupProgress();
        
        // 通过StateManager恢复到之前的状态
        if (this.stateManager && typeof this.stateManager.setState === 'function') {
            // 如果挂断失败，通常应该恢复到CONNECTED状态
            this.stateManager.setState('CONNECTED');
        }
        
        // 5秒后隐藏错误状态
        this.hideStatus(5000);
    }

    /**
     * 处理对方挂断通知
     */
    handleRemoteHangup(data) {
        console.log('收到对方挂断通知:', data);
        this.isHangingUp = false;
        
        // 使用现有的clearCallUI函数清理UI（包含状态重置）
        if (this.clearCallUI && typeof this.clearCallUI === 'function') {
            this.clearCallUI();
        } else if (typeof window.clearCallUI === 'function') {
            window.clearCallUI();
        }
        
        // 确保状态管理器重置到空闲状态
        if (this.stateManager && typeof this.stateManager.resetToIdle === 'function') {
            this.stateManager.resetToIdle();
        }
        
        // 显示对方挂断的消息
        setTimeout(() => {
            this.updateStatus(`对方已挂断: ${data.reason || '未知原因'}`, 'info');
            // 3秒后隐藏状态，显示就绪状态
            setTimeout(() => {
                this.updateStatus('就绪', 'success');
            }, 3000);
        }, 200);
        
        // 触发自定义事件
        this.dispatchRemoteHangupEvent(data);
        
        console.log('对方挂断处理完成，系统已重置到就绪状态');
    }

    /**
     * 更新状态显示 - 使用全局updateStatus函数或回退到本地实现
     */
    updateStatus(message, type = 'info') {
        // 防抖处理，避免频繁更新
        if (this.lastStatusUpdate && 
            this.lastStatusUpdate.message === message && 
            this.lastStatusUpdate.type === type &&
            Date.now() - this.lastStatusUpdate.timestamp < 100) {
            return;
        }
        
        this.lastStatusUpdate = {
            message: message,
            type: type,
            timestamp: Date.now()
        };
        
        // 优先使用传入的updateStatus函数
        if (this.uiElements.updateStatus && typeof this.uiElements.updateStatus === 'function') {
            this.uiElements.updateStatus(message, type);
            return;
        }
        
        // 其次使用全局updateStatus函数
        if (typeof window.updateStatus === 'function') {
            window.updateStatus(message, type);
            return;
        }
        
        // 回退到本地实现
        if (!this.statusAlert) return;
        
        const statusDiv = this.statusAlert.querySelector('#status') || this.statusAlert;
        statusDiv.textContent = message;
        this.statusAlert.className = `alert mb-4 alert-${type}`;
    }

    /**
     * 隐藏状态显示
     */
    hideStatus(delay = 0) {
        if (delay > 0) {
            setTimeout(() => {
                if (this.statusAlert) {
                    this.statusAlert.style.display = 'none';
                }
            }, delay);
        } else if (this.statusAlert) {
            this.statusAlert.style.display = 'none';
        }
    }

    /**
     * 启用挂断按钮
     */
    enableHangupButton() {
        if (this.hangupButton) {
            this.hangupButton.disabled = false;
            this.hangupButton.innerHTML = '<i class="bi bi-telephone-x-fill"></i> 挂断';
            // 添加平滑的视觉反馈
            this.hangupButton.classList.remove('btn-secondary');
            this.hangupButton.classList.add('btn-danger');
        }
    }

    /**
     * 禁用挂断按钮
     */
    disableHangupButton() {
        if (this.hangupButton) {
            this.hangupButton.disabled = true;
            // 添加视觉反馈表示按钮已禁用
            this.hangupButton.classList.remove('btn-danger');
            this.hangupButton.classList.add('btn-secondary');
        }
    }

    /**
     * 显示挂断进度指示器
     */
    showHangupProgress() {
        if (this.hangupButton) {
            this.hangupButton.innerHTML = '<i class="spinner-border spinner-border-sm" role="status"></i> 挂断中...';
            this.hangupButton.classList.remove('btn-danger');
            this.hangupButton.classList.add('btn-warning');
        }
    }

    /**
     * 隐藏挂断进度指示器
     */
    hideHangupProgress() {
        if (this.hangupButton) {
            this.hangupButton.innerHTML = '<i class="bi bi-telephone-x-fill"></i> 挂断';
            this.hangupButton.classList.remove('btn-warning');
            if (!this.hangupButton.disabled) {
                this.hangupButton.classList.add('btn-danger');
            }
        }
    }

    /**
     * 触发通话结束自定义事件
     */
    dispatchCallEndedEvent(data) {
        const event = new CustomEvent('callEnded', {
            detail: data
        });
        document.dispatchEvent(event);
    }

    /**
     * 触发对方挂断自定义事件
     */
    dispatchRemoteHangupEvent(data) {
        const event = new CustomEvent('remoteHangup', {
            detail: data
        });
        document.dispatchEvent(event);
    }

    /**
     * 设置通话状态（启用/禁用挂断按钮）
     */
    setCallActive(isActive) {
        if (isActive) {
            this.enableHangupButton();
            this.updateStatus('通话进行中', 'success');
        } else {
            this.disableHangupButton();
            this.hideStatus();
        }
    }

    /**
     * 获取当前挂断状态
     */
    isCurrentlyHangingUp() {
        return this.isHangingUp;
    }

    /**
     * 检查是否可以执行挂断操作
     */
    canInitiateHangup() {
        if (this.isHangingUp) {
            return false;
        }
        
        // 检查StateManager状态
        if (this.stateManager && typeof this.stateManager.getCurrentState === 'function') {
            const currentState = this.stateManager.getCurrentState();
            return currentState === 'CONNECTED' || currentState === 'OUTGOING';
        }
        
        return true;
    }

    /**
     * 同步状态管理器状态
     */
    syncWithStateManager() {
        if (!this.stateManager || typeof this.stateManager.getCurrentState !== 'function') {
            return;
        }
        
        const currentState = this.stateManager.getCurrentState();
        
        switch (currentState) {
            case 'IDLE':
                this.disableHangupButton();
                break;
            case 'OUTGOING':
            case 'CONNECTED':
                this.enableHangupButton();
                break;
            case 'ENDING':
                this.disableHangupButton();
                this.showHangupProgress();
                break;
            default:
                console.warn('未知的状态:', currentState);
        }
    }

    /**
     * 清理资源
     */
    dispose() {
        // 移除事件监听器
        if (this.hangupButton && this.clickHandler) {
            this.hangupButton.removeEventListener('click', this.clickHandler);
        }
        
        if (this.keydownHandler) {
            document.removeEventListener('keydown', this.keydownHandler);
        }
        
        // 清理SignalR处理器
        if (this.connection) {
            this.connection.off('hangupInitiated');
            this.connection.off('callEnded');
            this.connection.off('hangupFailed');
            this.connection.off('remoteHangup');
        }
        
        // 清理引用以防止内存泄漏
        this.connection = null;
        this.uiElements = null;
        this.stateManager = null;
        this.hangupButton = null;
        this.statusAlert = null;
        this.updateStatusFunc = null;
        this.showCallInfo = null;
        this.clearCallUI = null;
        this.clickHandler = null;
        this.keydownHandler = null;
        this.lastStatusUpdate = null;
    }
}

// 导出类以供其他模块使用
window.HangupHandler = HangupHandler;