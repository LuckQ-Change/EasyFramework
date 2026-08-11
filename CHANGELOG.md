# Changelog

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
