/**
 * 前端UI优化集成测试
 * 测试HangupHandler与Index.cshtml的集成功能
 */

class IntegrationTests {
    constructor() {
        this.testResults = [];
        this.mockSignalRConnection = null;
        this.mockStateManager = null;
        this.hangupHandler = null;
    }

    /**
     * 运行所有集成测试
     */
    async runAllTests() {
        console.log('开始运行集成测试...');
        
        this.setupMocks();
        
        // 测试用例
        await this.testHangupHandlerInitialization();
        await this.testUIElementsIntegration();
        await this.testStateManagerIntegration();
        await this.testKeyboardShortcuts();
        await this.testHangupFlows();
        await this.testErrorHandling();
        
        this.printTestResults();
        return this.testResults;
    }

    /**
     * 设置模拟对象
     */
    setupMocks() {
        // 模拟SignalR连接
        this.mockSignalRConnection = {
            on: (event, handler) => {
                console.log(`Mock SignalR: 注册事件 ${event}`);
            },
            off: (event) => {
                console.log(`Mock SignalR: 取消事件 ${event}`);
            },
            invoke: async (method, ...args) => {
                console.log(`Mock SignalR: 调用方法 ${method}`, args);
                return true;
            },
            onclose: (handler) => {
                console.log('Mock SignalR: 注册连接关闭事件');
            },
            onreconnected: (handler) => {
                console.log('Mock SignalR: 注册重连事件');
            }
        };

        // 模拟状态管理器
        this.mockStateManager = {
            currentState: 'IDLE',
            setState: (state) => {
                console.log(`Mock StateManager: 设置状态为 ${state}`);
                this.currentState = state;
                return true;
            },
            getCurrentState: () => {
                return this.currentState;
            },
            resetToIdle: () => {
                console.log('Mock StateManager: 重置到空闲状态');
                this.currentState = 'IDLE';
            }
        };
    }

    /**
     * 测试HangupHandler初始化
     */
    async testHangupHandlerInitialization() {
        const testName = 'HangupHandler初始化测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            // 获取真实的UI元素
            const uiElements = {
                hangupButton: document.getElementById('hangupButton'),
                statusAlert: document.getElementById('statusAlert'),
                callInfo: document.getElementById('callInfo'),
                updateStatus: window.updateStatus,
                showCallInfo: window.showCallInfo,
                clearCallUI: window.clearCallUI
            };

            // 创建HangupHandler实例
            this.hangupHandler = new HangupHandler(
                this.mockSignalRConnection, 
                uiElements, 
                this.mockStateManager
            );

            // 验证初始化
            const passed = this.hangupHandler !== null && 
                          this.hangupHandler.connection === this.mockSignalRConnection &&
                          this.hangupHandler.stateManager === this.mockStateManager;

            this.addTestResult(testName, passed, passed ? '初始化成功' : '初始化失败');
        } catch (error) {
            this.addTestResult(testName, false, `初始化异常: ${error.message}`);
        }
    }

    /**
     * 测试UI元素集成
     */
    async testUIElementsIntegration() {
        const testName = 'UI元素集成测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            const hangupButton = document.getElementById('hangupButton');
            const statusAlert = document.getElementById('statusAlert');
            
            // 测试按钮状态更新
            if (this.hangupHandler) {
                this.hangupHandler.enableHangupButton();
                const buttonEnabled = !hangupButton.disabled && hangupButton.classList.contains('btn-danger');
                
                this.hangupHandler.disableHangupButton();
                const buttonDisabled = hangupButton.disabled && hangupButton.classList.contains('btn-secondary');
                
                const passed = buttonEnabled && buttonDisabled;
                this.addTestResult(testName, passed, passed ? 'UI元素操作正常' : 'UI元素操作异常');
            } else {
                this.addTestResult(testName, false, 'HangupHandler未初始化');
            }
        } catch (error) {
            this.addTestResult(testName, false, `UI集成测试异常: ${error.message}`);
        }
    }

    /**
     * 测试状态管理器集成
     */
    async testStateManagerIntegration() {
        const testName = '状态管理器集成测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            if (!this.hangupHandler) {
                this.addTestResult(testName, false, 'HangupHandler未初始化');
                return;
            }

            // 测试状态同步
            this.mockStateManager.setState('CONNECTED');
            this.hangupHandler.syncWithStateManager();
            
            const hangupButton = document.getElementById('hangupButton');
            const buttonEnabledForConnected = !hangupButton.disabled;

            this.mockStateManager.setState('IDLE');
            this.hangupHandler.syncWithStateManager();
            
            const buttonDisabledForIdle = hangupButton.disabled;

            const passed = buttonEnabledForConnected && buttonDisabledForIdle;
            this.addTestResult(testName, passed, passed ? '状态同步正常' : '状态同步异常');
        } catch (error) {
            this.addTestResult(testName, false, `状态管理集成测试异常: ${error.message}`);
        }
    }

    /**
     * 测试键盘快捷键
     */
    async testKeyboardShortcuts() {
        const testName = '键盘快捷键测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            if (!this.hangupHandler) {
                this.addTestResult(testName, false, 'HangupHandler未初始化');
                return;
            }

            // 设置为CONNECTED状态以允许挂断
            this.mockStateManager.setState('CONNECTED');
            
            // 模拟Ctrl+H按键事件
            const keyEvent = new KeyboardEvent('keydown', {
                key: 'h',
                ctrlKey: true,
                bubbles: true
            });

            // 监听挂断调用
            let hangupCalled = false;
            const originalInvoke = this.mockSignalRConnection.invoke;
            this.mockSignalRConnection.invoke = async (method, ...args) => {
                if (method === 'HangupCallAsync') {
                    hangupCalled = true;
                }
                return originalInvoke.call(this.mockSignalRConnection, method, ...args);
            };

            // 触发键盘事件
            document.dispatchEvent(keyEvent);
            
            // 等待异步操作
            await new Promise(resolve => setTimeout(resolve, 100));

            this.addTestResult(testName, hangupCalled, hangupCalled ? '键盘快捷键正常' : '键盘快捷键未响应');
        } catch (error) {
            this.addTestResult(testName, false, `键盘快捷键测试异常: ${error.message}`);
        }
    }

    /**
     * 测试挂断流程
     */
    async testHangupFlows() {
        const testName = '挂断流程测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            if (!this.hangupHandler) {
                this.addTestResult(testName, false, 'HangupHandler未初始化');
                return;
            }

            let testsPassed = 0;
            const totalTests = 4;

            // 测试1: 用户主动挂断
            this.mockStateManager.setState('CONNECTED');
            await this.hangupHandler.initiateHangup('用户主动挂断');
            if (this.hangupHandler.isCurrentlyHangingUp()) {
                testsPassed++;
            }

            // 测试2: 挂断完成处理
            this.hangupHandler.handleCallEnded({
                timestamp: new Date().toISOString(),
                reason: '正常结束'
            });
            if (this.mockStateManager.getCurrentState() === 'IDLE') {
                testsPassed++;
            }

            // 测试3: 挂断失败处理
            this.mockStateManager.setState('CONNECTED');
            this.hangupHandler.handleHangupFailed({
                message: '网络错误',
                timestamp: new Date().toISOString()
            });
            if (this.mockStateManager.getCurrentState() === 'CONNECTED') {
                testsPassed++;
            }

            // 测试4: 对方挂断处理
            this.hangupHandler.handleRemoteHangup({
                reason: '对方结束通话',
                timestamp: new Date().toISOString()
            });
            testsPassed++; // 这个测试主要验证不会抛出异常

            const passed = testsPassed === totalTests;
            this.addTestResult(testName, passed, `挂断流程测试: ${testsPassed}/${totalTests} 通过`);
        } catch (error) {
            this.addTestResult(testName, false, `挂断流程测试异常: ${error.message}`);
        }
    }

    /**
     * 测试错误处理
     */
    async testErrorHandling() {
        const testName = '错误处理测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            let testsPassed = 0;
            const totalTests = 3;

            // 测试1: UI错误处理
            if (window.ErrorRecovery) {
                const result1 = window.ErrorRecovery.handleUIError(new Error('测试UI错误'), 'test');
                if (result1) testsPassed++;
            }

            // 测试2: 网络错误处理
            if (window.ErrorRecovery) {
                const result2 = window.ErrorRecovery.handleNetworkError(new Error('测试网络错误'), 'test');
                if (result2) testsPassed++;
            }

            // 测试3: 业务逻辑错误处理
            if (window.ErrorRecovery) {
                const result3 = window.ErrorRecovery.handleBusinessError(new Error('测试业务错误'), 'test');
                if (result3) testsPassed++;
            }

            const passed = testsPassed === totalTests;
            this.addTestResult(testName, passed, `错误处理测试: ${testsPassed}/${totalTests} 通过`);
        } catch (error) {
            this.addTestResult(testName, false, `错误处理测试异常: ${error.message}`);
        }
    }

    /**
     * 添加测试结果
     */
    addTestResult(testName, passed, message) {
        const result = {
            name: testName,
            passed: passed,
            message: message,
            timestamp: new Date().toISOString()
        };
        
        this.testResults.push(result);
        console.log(`测试结果: ${testName} - ${passed ? '通过' : '失败'} - ${message}`);
    }

    /**
     * 打印测试结果摘要
     */
    printTestResults() {
        console.log('\n=== 集成测试结果摘要 ===');
        
        const totalTests = this.testResults.length;
        const passedTests = this.testResults.filter(r => r.passed).length;
        const failedTests = totalTests - passedTests;
        
        console.log(`总测试数: ${totalTests}`);
        console.log(`通过: ${passedTests}`);
        console.log(`失败: ${failedTests}`);
        console.log(`成功率: ${((passedTests / totalTests) * 100).toFixed(1)}%`);
        
        if (failedTests > 0) {
            console.log('\n失败的测试:');
            this.testResults.filter(r => !r.passed).forEach(result => {
                console.log(`- ${result.name}: ${result.message}`);
            });
        }
        
        console.log('\n=== 测试完成 ===\n');
    }

    /**
     * 获取测试结果
     */
    getTestResults() {
        return {
            total: this.testResults.length,
            passed: this.testResults.filter(r => r.passed).length,
            failed: this.testResults.filter(r => !r.passed).length,
            results: this.testResults
        };
    }
}

// 导出到全局作用域以便在控制台中使用
window.IntegrationTests = IntegrationTests;

// 提供便捷的测试运行函数
window.runIntegrationTests = async function() {
    const tests = new IntegrationTests();
    return await tests.runAllTests();
};

console.log('集成测试模块已加载。使用 runIntegrationTests() 运行所有测试。');