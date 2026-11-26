# Bootstrap

`FrameworkLauncher` 将 Unity 生命周期、帧循环和默认 UI 接到独立的 `ModuleManager`，并公开 `Ready`、`Modules` 和 `StopAsync()`。`FrameworkStartupConfig` 保存内置模块选择、项目装配器、运行模式和默认宿主的呈现配置。依赖方向为 Bootstrap → Core/UI。

启动配置通过 `ModuleComposition` 登记所需模块，Core 校验依赖然后启动。内置默认集合为 Event、Timer、Pool、Tween 和 Audio，资源模块按资源配置加入。流程模块、首流程及其他项目模块由装配器声明。`Runtime.Mode` 读取当前作用域或默认宿主的模式。

配置资产由项目持有。每个 Launcher 的 `FrameworkStartupPipeline` 按配置装配模块，预热直接引用包，管理常驻资源和热更的就绪任务及重试，并在宿主就绪后启动装配器声明的首流程。模块初始化失败由 Core 逆序回滚。退出和销毁共用关闭入口，依次解除就绪等待、收尾启动任务、释放 UI 和热更发行作用域、停止模块，最后关闭热更包后端。普通资源包在首次请求时初始化。
