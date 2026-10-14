# Core

`Entry` 提供单一框架入口；`ModuleManager` 按注册顺序初始化和更新模块，并逆序关闭、回滚；`IModule` 定义生命周期接口；`ModuleSingleton<T>` 只维护模块实例；`Singleton<T>` 提供非模块单例；`Log` 隔离日志输出失败。Core 不依赖具体功能模块。

模块只能由 Manager 注册并拥有。初始化失败清空已注册单例，更新或关闭时的单个异常不会阻止其他模块收尾。`ModuleManagerTests` 覆盖顺序、回滚、晚到安装及重复调用；`FoundationModuleTests` 覆盖日志异常。
