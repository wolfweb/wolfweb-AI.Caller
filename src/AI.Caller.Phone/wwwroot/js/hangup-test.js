/**
 * 挂断功能测试工具
 * 专门测试挂断后是否能正常再次发起呼叫
 */

class HangupTest {
    constructor() {
        this.testResults = [];
    }

    /**
     * 运行挂断后重新拨号测试
     */
    async runHangupRecoveryTest() {
        console.log('开始运行挂断恢复测试...');
        
        try {
            // 测试1: 检查初始状态
            await this.testInitialState();
            
            // 测试2: 模拟挂断操作
            await this.testHangupOperation();
            
            // 测试3: 检查挂断后状态
            await this.testPostHangupState();
            
            // 测试4: 测试重新拨号能力
            await this.testRedialCapability();
            
            this.printTestResults();
            return this.testResults;
        } catch (error) {
            console.error('测试过程中发生错误:', error);
            this.addTestResult('挂断恢复测试', false, `测试异常: ${error.message}`);
            return this.testResults;
        }
    }

    /**
     * 测试初始状态
     */
    async testInitialState() {
        const testName = '初始状态检查';
        console.log(`运行测试: ${testName}`);
        
        try {
            const callButton = document.getElementById('callButton');
            const hangupButton = document.getElementById('hangupButton');
            const destinationInput = document.getElementById('destination');
            
            const callButtonEnabled = !callButton.disabled && !callButton.classList.contains('d-none');
            const hangupButtonHidden = hangupButton.classList.contains('d-none');
            const inputEnabled = !destinationInput.disabled;
            const stateManagerIdle = window.callStateManager && window.callStateManager.getCurrentState() === 'IDLE';
            
            const passed = callButtonEnabled && hangupButtonHidden && inputEnabled && stateManagerIdle;
            
            this.addTestResult(testName, passed, passed ? 
                '初始状态正常' : 
                `初始状态异常: 拨号按钮=${callButtonEnabled}, 挂断按钮隐藏=${hangupButtonHidden}, 输入框=${inputEnabled}, 状态=${stateManagerIdle}`
            );
        } catch (error) {
            this.addTestResult(testName, false, `初始状态检查异常: ${error.message}`);
        }
    }

    /**
     * 测试挂断操作
     */
    async testHangupOperation() {
        const testName = '挂断操作测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            // 模拟进入通话状态
            if (window.callStateManager) {
                window.callStateManager.setState('CONNECTED');
            }
            
            // 等待状态更新
            await this.sleep(100);
            
            // 检查挂断按钮是否可用
            const hangupButton = document.getElementById('hangupButton');
            const hangupButtonVisible = !hangupButton.classList.contains('d-none');
            
            if (!hangupButtonVisible) {
                this.addTestResult(testName, false, '挂断按钮未显示');
                return;
            }
            
            // 模拟挂断操作
            if (window.hangupHandler) {
                // 模拟通话结束事件
                window.hangupHandler.handleCallEnded({
                    timestamp: new Date().toISOString(),
                    reason: '测试挂断'
                });
            } else {
                // 直接调用clearCallUI
                if (typeof window.clearCallUI === 'function') {
                    window.clearCallUI();
                }
            }
            
            // 等待处理完成
            await this.sleep(200);
            
            this.addTestResult(testName, true, '挂断操作执行完成');
        } catch (error) {
            this.addTestResult(testName, false, `挂断操作测试异常: ${error.message}`);
        }
    }

    /**
     * 测试挂断后状态
     */
    async testPostHangupState() {
        const testName = '挂断后状态检查';
        console.log(`运行测试: ${testName}`);
        
        try {
            const callButton = document.getElementById('callButton');
            const hangupButton = document.getElementById('hangupButton');
            const destinationInput = document.getElementById('destination');
            
            const callButtonEnabled = !callButton.disabled && !callButton.classList.contains('d-none');
            const hangupButtonHidden = hangupButton.classList.contains('d-none');
            const inputEnabled = !destinationInput.disabled;
            const stateManagerIdle = window.callStateManager && window.callStateManager.getCurrentState() === 'IDLE';
            
            // 检查拨号盘是否可用
            const dialpadButtons = document.querySelectorAll('.dialpad-btn');
            const dialpadEnabled = Array.from(dialpadButtons).every(btn => !btn.disabled);
            
            // 检查联系人列表是否可用
            const contactItems = document.querySelectorAll('.contact-item');
            const contactsEnabled = Array.from(contactItems).every(item => !item.disabled);
            
            const allChecks = [
                { name: '拨号按钮可用', value: callButtonEnabled },
                { name: '挂断按钮隐藏', value: hangupButtonHidden },
                { name: '输入框可用', value: inputEnabled },
                { name: '状态管理器IDLE', value: stateManagerIdle },
                { name: '拨号盘可用', value: dialpadEnabled },
                { name: '联系人列表可用', value: contactsEnabled }
            ];
            
            const passed = allChecks.every(check => check.value);
            const failedChecks = allChecks.filter(check => !check.value).map(check => check.name);
            
            this.addTestResult(testName, passed, passed ? 
                '挂断后状态正常' : 
                `挂断后状态异常: ${failedChecks.join(', ')}`
            );
        } catch (error) {
            this.addTestResult(testName, false, `挂断后状态检查异常: ${error.message}`);
        }
    }

    /**
     * 测试重新拨号能力
     */
    async testRedialCapability() {
        const testName = '重新拨号能力测试';
        console.log(`运行测试: ${testName}`);
        
        try {
            const callButton = document.getElementById('callButton');
            const destinationInput = document.getElementById('destination');
            
            // 设置测试号码
            destinationInput.value = '12345678901';
            
            // 检查是否可以点击拨号按钮
            const canClick = !callButton.disabled && !callButton.classList.contains('d-none');
            
            if (!canClick) {
                this.addTestResult(testName, false, '拨号按钮不可点击');
                return;
            }
            
            // 模拟点击拨号按钮（不实际发起呼叫）
            let clickHandled = false;
            const originalClick = callButton.onclick;
            
            // 临时替换点击处理器
            callButton.onclick = () => {
                clickHandled = true;
                console.log('模拟拨号按钮点击成功');
            };
            
            // 触发点击事件
            callButton.click();
            
            // 恢复原始处理器
            callButton.onclick = originalClick;
            
            // 等待处理
            await this.sleep(100);
            
            this.addTestResult(testName, clickHandled, clickHandled ? 
                '重新拨号功能正常' : 
                '重新拨号功能异常'
            );
        } catch (error) {
            this.addTestResult(testName, false, `重新拨号能力测试异常: ${error.message}`);
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
     * 打印测试结果
     */
    printTestResults() {
        console.log('\n=== 挂断恢复测试结果 ===');
        
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
        
        // 提供修复建议
        if (failedTests > 0) {
            console.log('修复建议:');
            console.log('1. 检查 clearCallUI() 函数是否正确重置所有UI状态');
            console.log('2. 确认 CallStateManager.resetToIdle() 被正确调用');
            console.log('3. 验证事件监听器没有阻止状态重置');
            console.log('4. 使用 debugCallState() 函数检查详细状态');
        }
    }

    /**
     * 睡眠函数
     */
    sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    /**
     * 快速诊断当前状态
     */
    quickDiagnosis() {
        console.log('\n=== 快速诊断 ===');
        
        const callButton = document.getElementById('callButton');
        const hangupButton = document.getElementById('hangupButton');
        const destinationInput = document.getElementById('destination');
        
        console.log('拨号按钮:', {
            disabled: callButton.disabled,
            hidden: callButton.classList.contains('d-none'),
            canCall: !callButton.disabled && !callButton.classList.contains('d-none')
        });
        
        console.log('挂断按钮:', {
            disabled: hangupButton.disabled,
            hidden: hangupButton.classList.contains('d-none')
        });
        
        console.log('输入框:', {
            disabled: destinationInput.disabled,
            value: destinationInput.value
        });
        
        if (window.callStateManager) {
            console.log('状态管理器:', window.callStateManager.getCurrentState());
        }
        
        if (window.hangupHandler) {
            console.log('挂断处理器:', {
                isHangingUp: window.hangupHandler.isCurrentlyHangingUp()
            });
        }
        
        console.log('=== 诊断完成 ===\n');
    }
}

// 导出到全局作用域
window.HangupTest = HangupTest;

// 提供便捷的测试函数
window.testHangupRecovery = async function() {
    const test = new HangupTest();
    return await test.runHangupRecoveryTest();
};

window.diagnoseCallState = function() {
    const test = new HangupTest();
    test.quickDiagnosis();
};

console.log('挂断测试工具已加载。使用 testHangupRecovery() 运行测试，使用 diagnoseCallState() 快速诊断。');