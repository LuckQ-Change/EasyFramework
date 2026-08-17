# Changelog

## [Unreleased]

### Changed

- 移除框架程序集中的具体业务流程与流程专用启动配置，项目流程现在可直接引用项目 UI 和脚本。
- 启动配置支持在 Inspector 中选择项目的首流程脚本，并由 Launcher 自动注册和启动。
- `StartAsync`、`ChangeAsync` 和 `Next` 支持按需自动注册具有公开无参构造函数的流程。
- 流程、Loading、资源与热更基础模块改为由项目按需注册。
- `ProcedureContext` 改为可继承、幂等释放的业务上下文基类，流程支持注册带构造参数的实例。
- UI 与 Loading 共用唯一一套屏幕适配和 Layer 配置；Loading 不再隐式加载 `Resources/LoadingView`。
- 关闭 UI 启动后不再由自动 Manager 绕过配置创建 UI 根节点。
- Runtime 源码注释和主要 Inspector 分组统一为中文。

## [0.1.0] - 2026-08-11

### Changed

- 模块启动改为完整注册后统一初始化，并增加失败回滚和异常隔离。
- 修复自动启动与业务 registrar 同阶段执行时可能漏注册的问题。
- 默认模块缩减为 Event、Timer、Pool；Asset、Network、Hotfix 改为按需注册。
- TCP 使用连接上下文隔离，发送改为异步顺序写入，并增加队列和帧预算。
- 对象池增加归属检查、重复回收检查、命名池、销毁回调和正确容量语义。
- 事件派发改为无 `GetInvocationList` 快照分配的通道实现。
- AssetModule 增加并发加载合并、引用计数和 `AssetLease<T>`。
- TimerModule 支持取消派发过程中刚创建的待处理计时器。

### Added

- Core、Bootstrap、Event、Pool、Timer、Asset、Network EditMode 测试。
- README 和与当前实现一致的使用/开发文档。
