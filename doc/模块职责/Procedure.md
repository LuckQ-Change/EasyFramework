# Procedure

`ProcedureBase` 定义业务流程的进入、更新与退出。`ProcedureContext` 保存一次流程运行的数据。`ProcedureModule` 同时执行一个切换，切换完成后恢复流程 Tick。

进入流程时返回 `Next<T>()` 自动跳转，或返回 `Stay()` 等待下一次业务操作。切换期间的额外 `ChangeAsync` 会立即报错。模块接受 `StartAsync` 后持有上下文，退出逻辑结束后释放。执行中的切换失败会清理当前流程和上下文，业务可以重新启动。
