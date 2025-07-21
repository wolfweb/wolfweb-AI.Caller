/**
 * 错误恢复机制模块
 * 处理网络错误、UI错误和业务逻辑错误，提供自动恢复功能
 */

// 错误类型定义
const ErrorTypes = {
    NETWORK: 'network',
    UI: 'ui',
    BUSINESS: 'business',
    UNKNOWN: 'unknown'
};

// 错误恢复策略
class ErrorRecovery {
    /**
     * 处理UI错误
     */
    static handleUIError(error, context) {
        console.error(`UI错误 [${context}]:`, error);
        
        try {
            // 尝试恢复到安全状态
            if (window.callStateManager && typeof window.callStateManager.resetToIdle === 'function') {
                window.callStateManager.resetToIdle();
            }
            
            // 显示用户友好的错误消息
            if (window.updateStatus && typeof window.updateStatus === 'function') {
                window.updateStatus('操作失败，已重置到初始状态', 'warning');
            }
            
            // 记录错误
            this.logError(ErrorTypes.UI, error, context);
            
            return true;
        } catch (recoveryError) {
            console.error('UI错误恢复失败:', recoveryError);
            return false;
        }
    }
    
    /**
     * 处理网络错误
     */
    static handleNetworkError(error, context = 'unknown') {
        console.error(`网络错误 [${context}]:`, error);
        
        try {
            // 更新UI状态
            if (window.updateStatus && typeof window.updateStatus === 'function') {
                window.updateStatus('网络连接异常，请检查网络设置', 'danger');
            }
            
            // 禁用相关功能
            const hangupButton = document.getElementById('hangupButton');
            if (hangupButton) {
                hangupButton.disabled = true;
                hangupButton.classList.add('btn-secondary');
                hangupButton.classList.remove('btn-danger');
            }
            
            // 重置状态管理器
            if (window.callStateManager && typeof window.callStateManager.resetToIdle === 'function') {
                window.callStateManager.resetToIdle();
            }
            
            // 记录错误
            this.logError(ErrorTypes.NETWORK, error, context);
            
            return true;
        } catch (recoveryError) {
            console.error('网络错误恢复失败:', recoveryError);
            return false;
        }
    }
    
    /**
     * 处理业务逻辑错误
     */
    static handleBusinessError(error, context = 'unknown') {
        console.error(`业务逻辑错误 [${context}]:`, error);
        
        try {
            // 根据错误类型决定恢复策略
            if (error.message && error.message.includes('重复操作')) {
                // 忽略重复操作，不需要特殊处理
                console.log('忽略重复操作错误');
                return true;
            }
            
            if (error.message && error.message.includes('状态转换')) {
                // 状态转换错误，重置到安全状态
                if (window.callStateManager && typeof window.callStateManager.resetToIdle === 'function') {
                    window.callStateManager.resetToIdle();
                }
                
                if (window.updateStatus && typeof window.updateStatus === 'function') {
                    window.updateStatus('状态异常，已重置', 'warning');
                }
            }
            
            // 记录错误
            this.logError(ErrorTypes.BUSINESS, error, context);
            
            return true;
        } catch (recoveryError) {
            console.error('业务逻辑错误恢复失败:', recoveryError);
            return false;
        }
    }
    
    /**
     * 通用错误处理器
     */
    static handleError(error, context = 'unknown', type = ErrorTypes.UNKNOWN) {
        console.error(`通用错误处理 [${type}] [${context}]:`, error);
        
        switch (type) {
            case ErrorTypes.NETWORK:
                return this.handleNetworkError(error, context);
            case ErrorTypes.UI:
                return this.handleUIError(error, context);
            case ErrorTypes.BUSINESS:
                return this.handleBusinessError(error, context);
            default:
                return this.handleUnknownError(error, context);
        }
    }
    
    /**
     * 处理未知错误
     */
    static handleUnknownError(error, context) {
        console.error(`未知错误 [${context}]:`, error);
        
        try {
            // 采用最保守的恢复策略
            if (window.callStateManager && typeof window.callStateManager.resetToIdle === 'function') {
                window.callStateManager.resetToIdle();
            }
            
            if (window.updateStatus && typeof window.updateStatus === 'function') {
                window.updateStatus('发生未知错误，已重置系统状态', 'danger');
            }
            
            // 记录错误
            this.logError(ErrorTypes.UNKNOWN, error, context);
            
            return true;
        } catch (recoveryError) {
            console.error('未知错误恢复失败:', recoveryError);
            return false;
        }
    }
    
    /**
     * 记录错误日志
     */
    static logError(type, error, context) {
        const logEntry = {
            timestamp: new Date().toISOString(),
            type: type,
            context: context,
            message: error.message || error.toString(),
            stack: error.stack,
            userAgent: navigator.userAgent,
            url: window.location.href
        };
        
        // 异步存储到本地存储以避免阻塞UI
        setTimeout(() => {
            try {
                const errorLog = JSON.parse(localStorage.getItem('hangup_error_log') || '[]');
                errorLog.push(logEntry);
                
                // 只保留最近的50条错误记录
                if (errorLog.length > 50) {
                    errorLog.splice(0, errorLog.length - 50);
                }
                
                localStorage.setItem('hangup_error_log', JSON.stringify(errorLog));
            } catch (storageError) {
                console.warn('无法保存错误日志到本地存储:', storageError);
            }
        }, 0);
        
        console.log('错误已记录:', logEntry);
    }
    
    /**
     * 获取错误日志
     */
    static getErrorLog() {
        try {
            return JSON.parse(localStorage.getItem('hangup_error_log') || '[]');
        } catch (error) {
            console.error('获取错误日志失败:', error);
            return [];
        }
    }
    
    /**
     * 清除错误日志
     */
    static clearErrorLog() {
        try {
            localStorage.removeItem('hangup_error_log');
            console.log('错误日志已清除');
            return true;
        } catch (error) {
            console.error('清除错误日志失败:', error);
            return false;
        }
    }
    
    /**
     * 自动恢复机制
     */
    static setupAutoRecovery() {
        // 监听未捕获的错误
        window.addEventListener('error', (event) => {
            this.handleError(event.error || new Error(event.message), 'global_error', ErrorTypes.UNKNOWN);
        });
        
        // 监听未处理的Promise拒绝
        window.addEventListener('unhandledrejection', (event) => {
            this.handleError(event.reason, 'unhandled_promise', ErrorTypes.UNKNOWN);
        });
        
        // 监听网络状态变化
        window.addEventListener('offline', () => {
            this.handleNetworkError(new Error('网络连接断开'), 'network_offline');
        });
        
        window.addEventListener('online', () => {
            if (window.updateStatus && typeof window.updateStatus === 'function') {
                window.updateStatus('网络连接已恢复', 'success');
            }
        });
        
        console.log('自动错误恢复机制已启用');
    }
}

// 导出到全局作用域
window.ErrorTypes = ErrorTypes;
window.ErrorRecovery = ErrorRecovery;