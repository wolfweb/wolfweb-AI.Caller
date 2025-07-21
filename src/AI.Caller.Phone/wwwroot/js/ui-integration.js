/**
 * UI集成辅助模块
 * 提供UI操作的辅助函数和标准化的事件数据结构
 */

// 标准化的事件数据结构
const HangupEvents = {
    HANGUP_INITIATED: 'hangupInitiated',
    HANGUP_COMPLETED: 'hangupCompleted', 
    HANGUP_FAILED: 'hangupFailed',
    REMOTE_HANGUP: 'remoteHangup'
};

// UI状态验证器
class UIValidator {
    /**
     * 验证UI元素是否存在
     */
    static validateElements(elements) {
        const missing = [];
        const required = ['hangupButton', 'statusAlert'];
        
        for (const elementName of required) {
            if (!elements[elementName]) {
                missing.push(elementName);
            }
        }
        
        if (missing.length > 0) {
            console.warn('缺少必要的UI元素:', missing);
            return false;
        }
        
        return true;
    }
    
    /**
     * 验证函数引用是否存在
     */
    static validateFunctions(functions) {
        const missing = [];
        const required = ['updateStatus'];
        
        for (const funcName of required) {
            if (!functions[funcName] || typeof functions[funcName] !== 'function') {
                missing.push(funcName);
            }
        }
        
        if (missing.length > 0) {
            console.warn('缺少必要的UI函数:', missing);
            return false;
        }
        
        return true;
    }
}

// UI操作辅助函数
class UIHelper {
    /**
     * 安全地更新按钮状态
     */
    static updateButtonState(button, state) {
        if (!button) return false;
        
        try {
            switch (state) {
                case 'enabled':
                    button.disabled = false;
                    button.classList.remove('btn-secondary');
                    button.classList.add('btn-danger');
                    break;
                case 'disabled':
                    button.disabled = true;
                    button.classList.remove('btn-danger');
                    button.classList.add('btn-secondary');
                    break;
                case 'loading':
                    button.disabled = true;
                    button.classList.remove('btn-danger');
                    button.classList.add('btn-warning');
                    break;
                default:
                    console.warn('未知的按钮状态:', state);
                    return false;
            }
            return true;
        } catch (error) {
            console.error('更新按钮状态时出错:', error);
            return false;
        }
    }
    
    /**
     * 安全地更新状态显示
     */
    static updateStatusDisplay(statusElement, message, type = 'info') {
        if (!statusElement) return false;
        
        try {
            const statusDiv = statusElement.querySelector('#status') || statusElement;
            statusDiv.textContent = message;
            statusElement.className = `alert mb-4 alert-${type}`;
            return true;
        } catch (error) {
            console.error('更新状态显示时出错:', error);
            return false;
        }
    }
    
    /**
     * 创建标准化的事件数据
     */
    static createEventData(type, details = {}) {
        return {
            type: type,
            timestamp: new Date().toISOString(),
            ...details
        };
    }
}

// 导出到全局作用域
window.HangupEvents = HangupEvents;
window.UIValidator = UIValidator;
window.UIHelper = UIHelper;