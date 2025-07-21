/**
 * HangupHandler单元测试
 * 验证重构后的HangupHandler类的基本功能
 */

// 测试工具函数
const TestUtils = {
    // 创建模拟的UI元素
    createMockUIElements() {
        const mockButton = document.createElement('button');
        mockButton.id = 'mockHangupButton';
        
        const mockAlert = document.createElement('div');
        mockAlert.id = 'mockStatusAlert';
        
        return {
            hangupButton: mockButton,
            statusAlert: mockAlert,
            updateStatus: function(message, type) {
                console.log(`Mock updateStatus: ${message} (${type})`);
            },
            showCallInfo: function(show) {
                console.log(`Mock showCallInfo: ${show}`);
            },
            clearCallUI: function() {
                console.log('Mock clearCallUI called');
            }
        };
    },
    
    // 创建模拟的StateManager
    createMockStateManager() {
        return {
            currentState: 'IDLE',
            setState(newState) {
                this.currentState = newState;
                console.log(`Mock setState: ${newState}`);
                return true;
            },
            getCurrentState() {
                return this.currentState;
            },
            resetToIdle() {
                this.currentState = 'IDLE';
                console.log('Mock resetToIdle called');
            }
        };
    },
    
    // 创建模拟的SignalR连接
    createMockSignalRConnection() {
        const handlers = {};
        return {
            on(event, handler) {
                if (!handlers[event]) {
                    handlers[event] = [];
                }
                handlers[event].push(handler);
            },
            off(event) {
                delete handlers[event];
            },
            onclose(handler) {
                this.closeHandler = handler;
            },
            onreconnected(handler) {
                this.reconnectedHandler = handler;
            },
            invoke(method, ...args) {
                console.log(`Mock SignalR invoke: ${method}`, args);
                return Promise.resolve(true);
            },
            // 模拟触发事件
            triggerEvent(event, data) {
                if (handlers[event]) {
                    handlers[event].forEach(handler => handler(data));
                }
            }
        };
    }
};

// 测试套件
const HangupHandlerTests = {
    // 测试构造函数
    testConstructor() {
        console.log('🧪 测试构造函数...');
        
        const mockConnection = TestUtils.createMockSignalRConnection();
        const mockUIElements = TestUtils.createMockUIElements();
        const mockStateManager = TestUtils.createMockStateManager();
        
        try {
            const handler = new HangupHandler(mockConnection, mockUIElements, mockStateManager);
            
            if (handler.connection === mockConnection &&
                handler.uiElements === mockUIElements &&
                handler.stateManager === mockStateManager &&
                handler.isHangingUp === false) {
                console.log('✅ 构造函数测试通过');
                return true;
            } else {
                console.log('❌ 构造函数测试失败');
                return false;
            }
        } catch (error) {
            console.error('❌ 构造函数测试异常:', error);
            return false;
        }
    },
    
    // 测试UI元素初始化
    testUIElementsInitialization() {
        console.log('🧪 测试UI元素初始化...');
        
        const mockConnection = TestUtils.createMockSignalRConnection();
        const mockUIElements = TestUtils.createMockUIElements();
        const mockStateManager = TestUtils.createMockStateManager();
        
        try {
            const handler = new HangupHandler(mockConnection, mockUIElements, mockStateManager);
            
            if (handler.hangupButton === mockUIElements.hangupButton &&
                handler.statusAlert === mockUIElements.statusAlert &&
                typeof handler.updateStatus === 'function') {
                console.log('✅ UI元素初始化测试通过');
                return true;
            } else {
                console.log('❌ UI元素初始化测试失败');
                return false;
            }
        } catch (error) {
            console.error('❌ UI元素初始化测试异常:', error);
            return false;
        }
    },
    
    // 测试状态检查功能
    testCanInitiateHangup() {
        console.log('🧪 测试状态检查功能...');
        
        const mockConnection = TestUtils.createMockSignalRConnection();
        const mockUIElements = TestUtils.createMockUIElements();
        const mockStateManager = TestUtils.createMockStateManager();
        
        try {
            const handler = new HangupHandler(mockConnection, mockUIElements, mockStateManager);
            
            // 测试IDLE状态 - 不应该允许挂断
            mockStateManager.setState('IDLE');
            if (handler.canInitiateHangup() === false) {
                console.log('✅ IDLE状态检查通过');
            } else {
                console.log('❌ IDLE状态检查失败');
                return false;
            }
            
            // 测试CONNECTED状态 - 应该允许挂断
            mockStateManager.setState('CONNECTED');
            if (handler.canInitiateHangup() === true) {
                console.log('✅ CONNECTED状态检查通过');
            } else {
                console.log('❌ CONNECTED状态检查失败');
                return false;
            }
            
            // 测试挂断进行中 - 不应该允许重复挂断
            handler.isHangingUp = true;
            if (handler.canInitiateHangup() === false) {
                console.log('✅ 挂断进行中状态检查通过');
                return true;
            } else {
                console.log('❌ 挂断进行中状态检查失败');
                return false;
            }
        } catch (error) {
            console.error('❌ 状态检查测试异常:', error);
            return false;
        }
    },
    
    // 测试事件处理
    testEventHandling() {
        console.log('🧪 测试事件处理...');
        
        const mockConnection = TestUtils.createMockSignalRConnection();
        const mockUIElements = TestUtils.createMockUIElements();
        const mockStateManager = TestUtils.createMockStateManager();
        
        try {
            const handler = new HangupHandler(mockConnection, mockUIElements, mockStateManager);
            
            // 测试通话结束事件
            mockConnection.triggerEvent('callEnded', { reason: 'test' });
            
            if (handler.isHangingUp === false && mockStateManager.currentState === 'IDLE') {
                console.log('✅ 通话结束事件处理通过');
                return true;
            } else {
                console.log('❌ 通话结束事件处理失败');
                return false;
            }
        } catch (error) {
            console.error('❌ 事件处理测试异常:', error);
            return false;
        }
    },
    
    // 运行所有测试
    runAllTests() {
        console.log('🚀 开始运行HangupHandler单元测试...\n');
        
        const tests = [
            this.testConstructor,
            this.testUIElementsInitialization,
            this.testCanInitiateHangup,
            this.testEventHandling
        ];
        
        let passed = 0;
        let failed = 0;
        
        tests.forEach((test, index) => {
            try {
                if (test.call(this)) {
                    passed++;
                } else {
                    failed++;
                }
            } catch (error) {
                console.error(`测试 ${index + 1} 执行异常:`, error);
                failed++;
            }
            console.log(''); // 空行分隔
        });
        
        console.log('📊 测试结果汇总:');
        console.log(`✅ 通过: ${passed}`);
        console.log(`❌ 失败: ${failed}`);
        console.log(`📈 成功率: ${((passed / (passed + failed)) * 100).toFixed(1)}%`);
        
        return failed === 0;
    }
};

// 导出测试套件
window.HangupHandlerTests = HangupHandlerTests;

// 如果在浏览器控制台中，可以运行: HangupHandlerTests.runAllTests()
console.log('HangupHandler测试套件已加载。运行 HangupHandlerTests.runAllTests() 来执行所有测试。');