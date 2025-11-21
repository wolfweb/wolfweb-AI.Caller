/**
 * SIP线路选择组件
 */
class LineSelector {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        if (!this.container) {
            throw new Error(`Container with id ${containerId} not found`);
        }

        this.options = {
            autoSelect: true,
            selectedLineId: null,
            onLineChange: () => {},
            ...options
        };

        this.availableLines = [];
        this.init();
    }

    async init() {
        await this.loadAvailableLines();
        this.render();
        this.bindEvents();
    }

    async loadAvailableLines() {
        try {
            const response = await fetch('/api/SipLine/account/lines', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (response.ok) {
                this.availableLines = await response.json();
            } else {
                console.error('Failed to load available lines:', await response.text());
            }
        } catch (error) {
            console.error('Error loading available lines:', error);
        }
    }

    render() {
        this.container.innerHTML = `
            <div class="line-selector mb-3">
                <div class="form-check mb-2">
                    <input class="form-check-input" type="checkbox" id="autoSelectLine" 
                           ${this.options.autoSelect ? 'checked' : ''}>
                    <label class="form-check-label" for="autoSelectLine">
                        自动选择线路
                    </label>
                </div>
                <div class="form-group" id="lineSelectionGroup" 
                     style="${this.options.autoSelect ? 'display: none;' : ''}">
                    <label for="lineSelect" class="form-label">选择线路</label>
                    <select class="form-select" id="lineSelect">
                        <option value="">请选择线路</option>
                        ${this.availableLines.map(line => `
                            <option value="${line.id}" 
                                    ${line.id === this.options.selectedLineId ? 'selected' : ''}
                                    ${!line.isActive ? 'disabled' : ''}>
                                ${line.name} (${line.region || '未知区域'}) - ${line.description || '无描述'}
                                ${line.isDefault ? '[默认]' : ''}
                                ${!line.isActive ? '[已禁用]' : ''}
                            </option>
                        `).join('')}
                    </select>
                    <div class="form-text">
                        选择用于呼叫的SIP线路。如果不选择，系统将自动选择最优线路。
                    </div>
                </div>
                <div class="alert alert-info mt-2" id="lineStatus" style="display: none;">
                    <strong>当前线路：</strong> <span id="currentLineName">-</span>
                </div>
            </div>
        `;
    }

    bindEvents() {
        const autoSelectCheckbox = document.getElementById('autoSelectLine');
        const lineSelectionGroup = document.getElementById('lineSelectionGroup');
        const lineSelect = document.getElementById('lineSelect');

        // 自动选择复选框变化事件
        autoSelectCheckbox.addEventListener('change', (e) => {
            const autoSelect = e.target.checked;
            lineSelectionGroup.style.display = autoSelect ? 'none' : 'block';
            
            this.options.autoSelect = autoSelect;
            this.options.onLineChange({
                autoSelect: autoSelect,
                selectedLineId: autoSelect ? null : lineSelect.value
            });

            this.updateLineStatus(autoSelect ? null : lineSelect.value);
        });

        // 线路选择变化事件
        lineSelect.addEventListener('change', (e) => {
            const selectedLineId = e.target.value ? parseInt(e.target.value) : null;
            
            this.options.selectedLineId = selectedLineId;
            this.options.onLineChange({
                autoSelect: this.options.autoSelect,
                selectedLineId: selectedLineId
            });

            this.updateLineStatus(selectedLineId);
        });

        // 初始化线路状态
        if (!this.options.autoSelect && this.options.selectedLineId) {
            this.updateLineStatus(this.options.selectedLineId);
        }
    }

    updateLineStatus(lineId) {
        const lineStatus = document.getElementById('lineStatus');
        const currentLineName = document.getElementById('currentLineName');

        if (lineId) {
            const line = this.availableLines.find(l => l.id === lineId);
            if (line) {
                currentLineName.textContent = `${line.name} (${line.region || '未知区域'})`;
                lineStatus.style.display = 'block';
                lineStatus.className = 'alert alert-info mt-2';
            }
        } else {
            lineStatus.style.display = 'none';
        }
    }

    /**
     * 获取当前选择的线路参数
     */
    getLineSelection() {
        return {
            selectedLineId: this.options.autoSelect ? null : this.options.selectedLineId,
            autoSelectLine: this.options.autoSelect
        };
    }

    /**
     * 设置线路选择
     */
    setLineSelection(selectedLineId, autoSelectLine) {
        this.options.selectedLineId = selectedLineId;
        this.options.autoSelect = autoSelectLine;
        
        const autoSelectCheckbox = document.getElementById('autoSelectLine');
        const lineSelectionGroup = document.getElementById('lineSelectionGroup');
        const lineSelect = document.getElementById('lineSelect');

        autoSelectCheckbox.checked = autoSelectLine;
        lineSelectionGroup.style.display = autoSelectLine ? 'none' : 'block';
        
        if (selectedLineId) {
            lineSelect.value = selectedLineId;
        }

        this.updateLineStatus(autoSelectLine ? null : selectedLineId);
    }
}

// 导出为全局变量，供其他脚本使用
window.LineSelector = LineSelector;