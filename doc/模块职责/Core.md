# Core

`ModuleComposition` 声明模块工厂和必需的模块类型。`ModuleManager` 持有一份宿主专用的模块实例和生命周期。Unity 场景通过 `FrameworkLauncher` 选择配置并驱动这个管理器。

`com.wjq.easyframework.core` 是独立的纯 C# 程序集，提供模块宿主、帧时钟、运行模式和日志。Unity Runtime 依赖 Core。使用自定义 `.asmdef` 的项目模块、启动装配器和热更入口，在直接使用这些类型时引用 Core。

```csharp
var composition = new ModuleComposition();
composition.Add<AssetModule>();
composition.Add<AudioModule>(typeof(AssetModule));

var host = new ModuleManager(runtimeMode: RuntimeMode.Offline);
await host.StartAsync(composition);
AudioModule audio = host.Require<AudioModule>();
// 每帧由驱动器传入两路时钟，退出时等待 host.StopAsync()。
host.Tick(new FrameTime(scaledDeltaTime, unscaledDeltaTime));
```

1. 先检查完整模块图。缺依赖时报告依赖方和缺失类型，循环时报告循环路径。两种错误都在调用任何工厂和初始化回调前抛出。
2. 按依赖排序。工厂通过其 `ModuleManager` 参数获取已经就绪的依赖。`OnInit()` 执行同步初始化，`OnInitAsync(CancellationToken)` 执行可取消的异步初始化。同阶段任务可重叠，全部成功后才发布该阶段的就绪模块。
3. 初始化失败时取消同阶段任务，等已进入的初始化退出后，按进入顺序的反向逐个调用 `OnShutdownAsync()` 和 `OnShutdown()`。关闭异常记录后继续清理剩余模块。
4. 启动成功后只调度实现了 `IFrameUpdate` 的模块。停止时先暂停帧调度，再按相同的逆序规则关闭。显式装配的模块图在启动后固定。

每次调用 `Tick` 时，所有更新模块接收同一份 `FrameTime`。Unity Launcher 使用 `Time.deltaTime` 和 `Time.unscaledDeltaTime` 创建这份值。

Unity 入口持有对应 `FrameworkLauncher` 的引用，等待 `launcher.Ready`，再从 `launcher.Modules` 获取模块。模块内部用 `Host.Require<T>()` 引用其声明的依赖。`Module<T>.Instance` 按当前宿主作用域查找，作用域外使用显式默认宿主。初始化、关闭和同步帧回调带有作用域。延迟或异步回调可直接持有宿主，或在回调内用 `using var scope = host.EnterScope();` 建立跨 `await` 的作用域。
