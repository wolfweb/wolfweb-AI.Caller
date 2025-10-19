/**
 * 铃音管理页面
 */
class RingtoneManagement {
    constructor() {
        this.ringtones = [];
        this.currentSettings = null;
        this.previewAudio = document.getElementById('previewAudio');
        
        this.init();
    }

    async init() {
        await this.loadRingtones();
        await this.loadUserSettings();
        this.bindEvents();
    }

    async loadRingtones() {
        try {
            const response = await fetch('/api/ringtone');
            if (!response.ok) throw new Error('加载铃音列表失败');
            
            this.ringtones = await response.json();
            this.renderRingtoneSelects();
            this.renderRingtoneList();
        } catch (error) {
            console.error('加载铃音列表失败:', error);
            this.showStatus('加载铃音列表失败', 'danger');
        }
    }

    async loadUserSettings() {
        try {
            const response = await fetch('/api/ringtone/user-settings');
            if (!response.ok) throw new Error('加载用户设置失败');
            
            this.currentSettings = await response.json();
            this.applySettings();
        } catch (error) {
            console.error('加载用户设置失败:', error);
        }
    }

    renderRingtoneSelects() {
        const incomingSelect = document.getElementById('incomingRingtoneSelect');
        const ringbackSelect = document.getElementById('ringbackToneSelect');

        // 清空选项
        incomingSelect.innerHTML = '<option value="">使用系统默认</option>';
        ringbackSelect.innerHTML = '<option value="">使用系统默认</option>';

        // 添加铃音选项
        this.ringtones.forEach(ringtone => {
            if (ringtone.type === 'Incoming' || ringtone.type === 'Both') {
                const option = document.createElement('option');
                option.value = ringtone.id;
                option.textContent = `${ringtone.name}${ringtone.isSystem ? ' (系统)' : ''}`;
                option.dataset.filePath = ringtone.filePath;
                incomingSelect.appendChild(option);
            }

            if (ringtone.type === 'Ringback' || ringtone.type === 'Both') {
                const option = document.createElement('option');
                option.value = ringtone.id;
                option.textContent = `${ringtone.name}${ringtone.isSystem ? ' (系统)' : ''}`;
                option.dataset.filePath = ringtone.filePath;
                ringbackSelect.appendChild(option);
            }
        });
    }

    renderRingtoneList() {
        const tbody = document.getElementById('ringtoneList');
        
        if (this.ringtones.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-center">暂无铃音</td></tr>';
            return;
        }

        tbody.innerHTML = this.ringtones.map(ringtone => `
            <tr>
                <td>${ringtone.name}</td>
                <td>${this.getTypeText(ringtone.type)}</td>
                <td>${this.formatFileSize(ringtone.fileSize)}</td>
                <td>${ringtone.duration}秒</td>
                <td>${ringtone.isSystem ? '系统内置' : '自定义'}</td>
                <td>
                    <button class="btn btn-sm btn-outline-primary" onclick="ringtoneManagement.previewRingtone('${ringtone.filePath}')">
                        <i class="bi bi-play-circle"></i> 试听
                    </button>
                    ${!ringtone.isSystem ? `
                        <button class="btn btn-sm btn-outline-danger" onclick="ringtoneManagement.deleteRingtone(${ringtone.id})">
                            <i class="bi bi-trash"></i> 删除
                        </button>
                    ` : ''}
                </td>
            </tr>
        `).join('');
    }

    applySettings() {
        if (!this.currentSettings) return;

        const incomingSelect = document.getElementById('incomingRingtoneSelect');
        const ringbackSelect = document.getElementById('ringbackToneSelect');

        if (this.currentSettings.incomingRingtone) {
            incomingSelect.value = this.currentSettings.incomingRingtone.id;
        }

        if (this.currentSettings.ringbackTone) {
            ringbackSelect.value = this.currentSettings.ringbackTone.id;
        }
    }

    bindEvents() {
        // 保存设置
        document.getElementById('saveSettingsBtn').addEventListener('click', () => {
            this.saveSettings();
        });

        // 上传铃音
        document.getElementById('uploadBtn').addEventListener('click', () => {
            this.uploadRingtone();
        });

        // 试听按钮
        document.getElementById('previewIncomingBtn').addEventListener('click', () => {
            const select = document.getElementById('incomingRingtoneSelect');
            const option = select.options[select.selectedIndex];
            if (option && option.dataset.filePath) {
                this.previewRingtone(option.dataset.filePath);
            }
        });

        document.getElementById('previewRingbackBtn').addEventListener('click', () => {
            const select = document.getElementById('ringbackToneSelect');
            const option = select.options[select.selectedIndex];
            if (option && option.dataset.filePath) {
                this.previewRingtone(option.dataset.filePath);
            }
        });
    }

    async saveSettings() {
        const incomingRingtoneId = document.getElementById('incomingRingtoneSelect').value;
        const ringbackToneId = document.getElementById('ringbackToneSelect').value;

        try {
            const response = await fetch('/api/ringtone/user-settings', {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    incomingRingtoneId: incomingRingtoneId || null,
                    ringbackToneId: ringbackToneId || null
                })
            });

            if (!response.ok) throw new Error('保存设置失败');

            this.showStatus('铃音设置已保存', 'success');
            await this.loadUserSettings();
        } catch (error) {
            console.error('保存设置失败:', error);
            this.showStatus('保存设置失败', 'danger');
        }
    }

    async uploadRingtone() {
        const name = document.getElementById('ringtoneName').value.trim();
        const type = document.getElementById('ringtoneType').value;
        const fileInput = document.getElementById('ringtoneFile');
        const file = fileInput.files[0];

        if (!name) {
            this.showStatus('请输入铃音名称', 'warning');
            return;
        }

        if (!file) {
            this.showStatus('请选择铃音文件', 'warning');
            return;
        }

        const formData = new FormData();
        formData.append('file', file);
        formData.append('name', name);
        formData.append('type', type);

        try {
            const response = await fetch('/api/ringtone/upload', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                const error = await response.text();
                throw new Error(error);
            }

            this.showStatus('铃音上传成功', 'success');
            
            // 清空表单
            document.getElementById('ringtoneName').value = '';
            document.getElementById('ringtoneFile').value = '';
            
            // 重新加载列表
            await this.loadRingtones();
        } catch (error) {
            console.error('上传铃音失败:', error);
            this.showStatus(error.message || '上传铃音失败', 'danger');
        }
    }

    async deleteRingtone(id) {
        if (!confirm('确定要删除这个铃音吗？')) return;

        try {
            const response = await fetch(`/api/ringtone/${id}`, {
                method: 'DELETE'
            });

            if (!response.ok) {
                const error = await response.text();
                throw new Error(error);
            }

            this.showStatus('铃音已删除', 'success');
            await this.loadRingtones();
        } catch (error) {
            console.error('删除铃音失败:', error);
            this.showStatus(error.message || '删除铃音失败', 'danger');
        }
    }

    previewRingtone(filePath) {
        if (!filePath) return;

        // 停止当前播放
        this.previewAudio.pause();
        this.previewAudio.currentTime = 0;
        
        this.previewAudio.src = filePath;
        this.previewAudio.play();
    }

    stopPreview() {
        if (this.previewAudio) {
            this.previewAudio.pause();
            this.previewAudio.currentTime = 0;
            this.previewAudio.src = '';
        }
    }

    getTypeText(type) {
        const typeMap = {
            'Incoming': '来电铃音',
            'Ringback': '回铃音',
            'Both': '通用'
        };
        return typeMap[type] || type;
    }

    formatFileSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
        return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    }

    showStatus(message, type) {
        const alert = document.getElementById('statusAlert');
        alert.textContent = message;
        alert.className = `alert alert-${type}`;
        alert.classList.remove('d-none');

        setTimeout(() => {
            alert.classList.add('d-none');
        }, 3000);
    }
}

// 初始化
let ringtoneManagement;
document.addEventListener('DOMContentLoaded', () => {
    ringtoneManagement = new RingtoneManagement();
});

// 页面卸载时停止播放
window.addEventListener('beforeunload', () => {
    if (ringtoneManagement) {
        ringtoneManagement.stopPreview();
    }
});
