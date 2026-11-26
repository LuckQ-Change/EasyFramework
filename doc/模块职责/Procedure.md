# Procedure

`ProcedureBase` 定义业务流程的进入、更新与退出。`ProcedureContext` 保存一次流程运行的数据。`ProcedureModule` 同时执行一个切换，切换完成后恢复流程 Tick。

项目通过 `FrameworkModuleInstaller` 将 `ProcedureModule` 装配到本次宿主。在模块工厂内用 `Register(() => new SomeProcedure(...))` 登记流程工厂，或用 `Register(instance)` 登记该宿主的独立实例。工厂在流程首次使用时执行，后续切换复用实例。`StartAsync`、`ChangeAsync` 和 `Next<T>()` 的目标都要先登记。

装配时调用 `ConfigureStartup<T>()` 可指定使用基础 `ProcedureContext` 的首流程，由启动管线在宿主就绪后开始执行。需要自定义上下文时，只登记流程，由项目入口显式调用 `StartAsync<T>(context)`。流程切换回调需要结束当前宿主时，应把停止请求交给外部入口，避免回调与宿主互相等待。

进入流程时返回 `Next<T>()` 自动跳转，或返回 `Stay()` 等待下一次业务操作。切换期间的额外 `ChangeAsync` 会立即报错。模块接受 `StartAsync` 后持有上下文，退出逻辑结束后释放。执行中的切换失败会清理当前流程和上下文，调用方可以重新启动。
