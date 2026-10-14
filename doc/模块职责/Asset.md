# Asset

`AssetModule` 只合并相同地址的异步加载并计算租约；`AssetLease<T>` 表示一次持有；`IAssetLoader` 是外部资源系统边界；`MemoryAssetLoader` 和 `YooAssetLoader` 分别供内存演示与可选 YooAsset 集成。依赖 Core，UI 通过 Asset 加载 Prefab，Asset 不依赖 UI。

租约由调用方释放，最后一个租约触发底层释放。共享加载期间关闭时，迟到结果只释放一次；初始化失败可重新配置。`AssetModuleTests` 覆盖并发、关闭及失败路径；YooAsset 适配需在安装对应包和宏后另做目标工程验证。
