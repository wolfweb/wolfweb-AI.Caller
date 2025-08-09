# AI.Caller

一个基于 .NET 8 的智能 SIP 电话系统，提供 Web 界面进行电话呼叫管理和监控。

## 项目概述

AI.Caller 是一个现代化的 SIP 电话解决方案，集成了人工智能功能，支持通过 Web 界面进行电话呼叫、管理联系人和监控通话状态。系统采用模块化架构，分为核心 SIP 功能库和 Web 应用程序两个主要组件。

## 技术栈

- **.NET 8.0** - 主要开发框架
- **ASP.NET Core MVC** - Web 应用框架
- **Entity Framework Core** - 数据访问层
- **SQLite** - 数据库
- **SignalR** - 实时通信
- **SIPSorcery** - SIP 协议实现
- **FFmpeg** - 媒体处理

## 项目结构

```
AI.Caller/
├── src/
│   ├── AI.Caller.Core/          # 核心 SIP 功能库
│   │   ├── SIPClient.cs         # SIP 客户端实现
│   │   ├── SIPClientOptions.cs  # SIP 客户端配置
│   │   ├── SIPTransportManager.cs # SIP 传输管理
│   │   └── SoftphoneSTUNClient.cs # STUN 客户端
│   └── AI.Caller.Phone/         # Web 应用程序
│       ├── Controllers/         # MVC 控制器
│       ├── Models/             # 数据模型
│       ├── Views/              # 视图文件
│       ├── Services/           # 业务服务
│       ├── Hubs/               # SignalR 集线器
│       ├── BackgroundTask/     # 后台任务
│       └── wwwroot/            # 静态资源
└── tests/                      # 测试项目
```

## 主要功能

### 核心功能
- **SIP 电话呼叫** - 支持标准 SIP 协议的语音通话
- **联系人管理** - 管理通话联系人信息
- **通话监控** - 实时监控通话状态和质量
- **挂断检测** - 智能检测通话结束状态
- **网络监控** - 监控网络连接状态

### Web 界面功能
- **用户认证** - 安全的用户登录系统
- **实时通信** - 基于 SignalR 的实时状态更新
- **通话控制** - Web 界面控制电话呼叫
- **状态监控** - 实时显示系统和通话状态

## 发展路线图 (Roadmap)

### 🚀 第一阶段 - 核心功能完善 (Q1 2025)

#### 已完成 ✅
- [x] 基础 SIP 通话功能
- [x] Web 管理界面
- [x] 用户认证系统
- [x] 实时状态监控
- [x] 挂断检测和网络监控
- [x] 通话录音功能

#### 进行中 🔄
- [ ] 通话质量分析
- [ ] 多用户并发支持优化
- [ ] 单元测试覆盖率提升至 80%

### 🎯 第二阶段 - AI 功能集成 

#### 计划功能
- [ ] **语音识别 (ASR)**
  - 实时语音转文字
  - 支持多语言识别
  - 关键词检测和标记

- [ ] **自然语言处理 (NLP)**
  - 通话内容情感分析
  - 自动生成通话摘要
  - 智能分类和标签

- [ ] **语音合成 (TTS)**
  - 自动语音应答
  - 多语言语音播报
  - 个性化语音定制

- [ ] **智能路由**
  - 基于内容的智能转接
  - 自动客服机器人
  - 智能排队和分配

### 📈 第三阶段 - 企业级功能 

#### 高级功能
- [ ] **多租户支持**
  - 企业级权限管理
  - 资源隔离和配额
  - 自定义品牌界面

- [ ] **高可用性**
  - 集群部署支持
  - 负载均衡
  - 故障自动恢复

- [ ] **数据分析**
  - 通话统计报表
  - 性能监控仪表板
  - 预测性分析

- [ ] **集成能力**
  - CRM 系统集成
  - Webhook 支持
  - RESTful API 完善

### 🌐 第四阶段 - 云原生和扩展 

#### 云原生特性
- [ ] **容器化部署**
  - Docker 容器支持
  - Kubernetes 编排
  - Helm Charts

- [ ] **微服务架构**
  - 服务拆分和解耦
  - 服务网格集成
  - 分布式追踪

- [ ] **云服务集成**
  - 支持主流云平台
  - 对象存储集成
  - 云数据库支持

#### 移动端支持
- [ ] **移动应用**
  - iOS/Android 原生应用
  - 跨平台解决方案
  - 推送通知支持

### 🔮 未来展望

#### 创新功能
- [ ] **实时翻译**
  - 多语言实时翻译
  - 跨语言通话支持
  - 文化适应性优化

- [ ] **AR/VR 集成**
  - 虚拟会议室
  - 3D 通话界面
  - 沉浸式体验

- [ ] **区块链集成**
  - 通话记录不可篡改
  - 去中心化身份验证
  - 智能合约支持

- [ ] **边缘计算**
  - 本地 AI 处理
  - 低延迟优化
  - 离线功能支持

### 📊 技术债务和优化

#### 持续改进
- [ ] **性能优化**
  - 内存使用优化
  - 并发处理能力提升
  - 数据库查询优化

- [ ] **安全加固**
  - 端到端加密
  - 安全审计日志
  - 漏洞扫描和修复

- [ ] **代码质量**
  - 代码重构
  - 设计模式应用
  - 文档完善

### 🤝 社区贡献

#### 开源生态
- [ ] **插件系统**
  - 第三方插件支持
  - 插件市场
  - 开发者工具包

- [ ] **社区建设**
  - 开发者文档
  - 示例项目
  - 技术分享

## 快速开始

### 环境要求

- .NET 8.0 SDK
- Visual Studio 2022 或 VS Code
- SIP 服务器（用于测试）

### 安装步骤

1. **克隆项目**
   ```bash
   git clone https://github.com/wolfweb/wolfweb-AI.Caller.git
   cd AI.Caller
   ```

2. **还原依赖包**
   ```bash
   dotnet restore
   ```

3. **配置设置**
   
   编辑 `src/AI.Caller.Phone/appsettings.json`：
   ```json
   {
     "SipSettings": {
       "SipServer": "your-sip-server-ip"
     },
     "UserSettings": {
       "DefaultUser": {
         "Username": "your-username",
         "Password": "your-password"
       }
     }
   }
   ```

4. **初始化数据库**
   ```bash
   cd src/AI.Caller.Phone
   dotnet ef database update
   ```

5. **运行应用**
   ```bash
   dotnet run --project src/AI.Caller.Phone
   ```

6. **访问应用**
   
   打开浏览器访问 `https://localhost:5001`

## 配置说明

### SIP 配置
- `SipServer`: SIP 服务器地址
- 支持标准 SIP 认证和注册

### 数据库配置
- 默认使用 SQLite 数据库
- 数据库文件位置: `src/AI.Caller.Phone/app.db`

### 用户配置
- 默认管理员账户可在 `appsettings.json` 中配置
- 支持会话管理和认证过滤

## 开发指南

### 项目架构

1. **AI.Caller.Core** - 核心库
   - 提供 SIP 客户端功能
   - 处理媒体流和网络传输
   - 独立于 Web 应用的可重用组件

2. **AI.Caller.Phone** - Web 应用
   - MVC 架构的 Web 界面
   - SignalR 实时通信
   - 后台服务和任务调度

### 主要服务

- **SipService** - SIP 通话服务
- **UserService** - 用户管理服务
- **ContactService** - 联系人服务
- **HangupMonitoringService** - 挂断监控服务

### 扩展开发

1. 添加新的控制器到 `Controllers/` 目录
2. 创建对应的服务类到 `Services/` 目录
3. 在 `Program.cs` 中注册新服务
4. 使用 Entity Framework 进行数据操作

## 部署

### 开发环境部署
```bash
dotnet publish -c Release -o ./publish
```

### 生产环境部署
1. 配置 IIS 或 Nginx 反向代理
2. 设置环境变量和配置文件
3. 确保 SIP 服务器网络连通性
4. 配置防火墙规则允许 SIP 端口

## 故障排除

### 常见问题

1. **SIP 连接失败**
   - 检查 SIP 服务器地址和端口
   - 验证网络连通性
   - 确认防火墙设置

2. **数据库连接问题**
   - 检查 SQLite 文件权限
   - 运行数据库迁移命令

3. **媒体流问题**
   - 确认 FFmpeg 依赖安装
   - 检查音频设备配置

## 贡献指南

1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

## 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 联系方式

如有问题或建议，请通过以下方式联系：

- 创建 Issue
- 发送邮件至 [email]
- 项目讨论区

## 更新日志

### v1.0.0
- 初始版本发布
- 基础 SIP 通话功能
- Web 管理界面
- 用户认证系统
- 实时状态监控