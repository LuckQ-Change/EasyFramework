# Core

`ModuleManager` 管理注册、初始化和逆序关闭。模块继承 `Module<T>`，`Instance` 从当前管理器取用。只有实现 `IFrameUpdate` 的模块会进入逐帧调度。Core 不依赖具体功能模块。

`Register<T>(order: 0)` 指定初始化阶段，数字越小越先执行。依赖别的模块时，把依赖放在更早的阶段。同一阶段按注册顺序开始初始化，异步等待可以重叠，当前阶段全部完成后才进入下一阶段。同步初始化放在 `OnInit()`，异步初始化重写 `OnInitAsync(CancellationToken)` 并传递令牌。两者都从 Unity 主线程调用。

场景入口通过 `await FrameworkLauncher.Instance.Ready` 等待模块及 UI、Loading 宿主就绪。运行期可通过 `await ModuleManager.Current.InstallAsync(...)` 安装一批模块。已启动模块会继续 Tick，新批次就绪后开始 Tick。

初始化失败会取消同批任务，等它们结束后逆序收尾并移除该批模块。模块可以在 `OnShutdownAsync()` 中等待异步清理，框架等待该模块结束后再按初始化的逆序关闭下一个模块。需要保证清理完成的业务入口应等待 `ModuleManager.Current.StopAsync()`。
