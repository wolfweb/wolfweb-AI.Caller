/**
 * 通话状态管理器
 * 负责管理通话状态和按钮显示逻辑
 */

const CallState = {
    IDLE: 'IDLE',
    OUTGOING: 'OUTGOING',
    INCOMING: 'INCOMING',
    CONNECTED: 'CONNECTED',
    ENDING: 'ENDING'
};

const BUTTON_STATES = {
    IDLE: { call: true, answer: false, hangup: false },
    OUTGOING: { call: false, answer: false, hangup: true },
    INCOMING: { call: false, answer: true, hangup: false },
    CONNECTED: { call: false, answer: false, hangup: true },
    ENDING: { call: false, answer: false, hangup: false }
};

class CallStateManager {
    constructor(elements) {
        this.currentState = CallState.IDLE;
        this.isTransitioning = false;
        this.elements = elements;
        this.buttons = {
            call: elements.callButton,
            answer: elements.answerButton,
            hangup: elements.hangupButton,

        };
        
        this.updateButtonVisibility();
        console.log('CallStateManager initialized in IDLE state');
    }

    setState(newState) {
        if (this.isTransitioning) {
            console.warn(`State transition blocked: already transitioning from ${this.currentState}`);
            return false;
        }

        if (!CallState[newState]) {
            console.error(`Invalid state: ${newState}. Falling back to IDLE.`);
            newState = CallState.IDLE;
        }

        const oldState = this.currentState;
        if (oldState === newState) {
            console.log(`State unchanged: ${newState}`);
            return true;
        }

        console.log(`State transition: ${oldState} → ${newState}`);
        
        this.isTransitioning = true;
        this.currentState = newState;
        
        try {
            this.updateButtonVisibility();
            console.log(`State successfully changed to: ${newState}`);
            return true;
        } catch (error) {
            console.error(`Error during state transition to ${newState}:`, error);
            this.currentState = CallState.IDLE;
            this.updateButtonVisibility();
            return false;
        } finally {
            this.isTransitioning = false;
        }
    }

    updateButtonVisibility() {
        const config = BUTTON_STATES[this.currentState];
        if (!config) {
            console.error(`No button configuration for state: ${this.currentState}`);
            return;
        }

        try {
            // 更新各个按钮的显示状态
            this.updateButton('call', config.call);
            this.updateButton('answer', config.answer);
            this.updateButton('hangup', config.hangup);


            console.log(`Button visibility updated for state: ${this.currentState}`, config);
        } catch (error) {
            console.error('Error updating button visibility:', error);
        }
    }

    updateButton(buttonName, shouldShow) {
        const button = this.buttons[buttonName];
        if (!button) {
            console.warn(`Button ${buttonName} not found`);
            return;
        }

        if (shouldShow) {
            this.showButton(button);
        } else {
            this.hideButton(button);
        }
    }

    showButton(button) {
        if (!button) {
            console.warn('Cannot show button: button element is null or undefined');
            return;
        }
        try {
            button.classList.remove('d-none');
            button.disabled = false;
        } catch (error) {
            console.error('Error showing button:', error);
        }
    }

    hideButton(button) {
        if (!button) {
            console.warn('Cannot hide button: button element is null or undefined');
            return;
        }
        try {
            button.classList.add('d-none');
            button.disabled = true;
        } catch (error) {
            console.error('Error hiding button:', error);
        }
    }

    resetToIdle() {
        console.log('Resetting to IDLE state');
        this.clearCallContext();
        this.setState(CallState.IDLE);
    }

    getCurrentState() {
        return this.currentState;
    }

    setCallContext(context) {
        this.callContext = context;
        console.log('设置通话上下文:', context);
    }

    getCallContext() {
        return this.callContext;
    }

    clearCallContext() {
        this.callContext = null;
        console.log('清除通话上下文');
    }

    testState(stateName) {
        if (!CallState[stateName]) {
            console.error(`Invalid state name: ${stateName}`);
            return false;
        }

        console.log(`Testing ${stateName} state...`);
        this.setState(CallState[stateName]);
        
        const config = BUTTON_STATES[stateName];
        const results = {};
        
        Object.keys(config).forEach(buttonName => {
            const button = this.buttons[buttonName];
            if (button) {
                const shouldBeVisible = config[buttonName];
                const isVisible = !button.classList.contains('d-none');
                results[buttonName] = isVisible === shouldBeVisible;
                console.log(`${buttonName}: expected ${shouldBeVisible}, actual ${isVisible}`);
            }
        });
        
        const allCorrect = Object.values(results).every(result => result);
        console.log(`${stateName} state test: ${allCorrect ? 'PASSED' : 'FAILED'}`);
        
        return { state: stateName, results, passed: allCorrect };
    }

    runAllTests() {
        console.log('Running all state tests...');
        const testResults = {};
        
        Object.keys(CallState).forEach(stateName => {
            testResults[stateName.toLowerCase()] = this.testState(stateName);
        });
        
        const allPassed = Object.values(testResults).every(result => result.passed);
        console.log('Test results:', testResults);
        console.log(`All tests ${allPassed ? 'PASSED' : 'FAILED'}`);
        
        // 重置到IDLE状态
        this.setState(CallState.IDLE);
        
        return testResults;
    }
}

// 全局测试对象
window.testCallStateManager = {
    testIdleState: () => window.phoneApp?.callStateManager?.testState('IDLE'),
    testOutgoingState: () => window.phoneApp?.callStateManager?.testState('OUTGOING'),
    testIncomingState: () => window.phoneApp?.callStateManager?.testState('INCOMING'),
    testConnectedState: () => window.phoneApp?.callStateManager?.testState('CONNECTED'),
    testAllStates: () => window.phoneApp?.callStateManager?.runAllTests(),
    getCurrentState: () => window.phoneApp?.callStateManager?.getCurrentState()
};