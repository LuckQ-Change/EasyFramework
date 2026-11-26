# EasyFramework

EasyFramework 是面向 Unity 2022.3 LTS 的轻量游戏框架。核心 Runtime 独立于可选的 YooAsset 适配程序集。

默认提供事件、定时器、C# 对象池、Tween、[音频播放](doc/模块职责/Audio.md)和 [场景模块](doc/模块职责/Scene.md)。TCP、资源加载和 HybridCLR 热更能力保留在同一包内，由项目显式注册。框架关注生命周期、所有权和主线程派发，不试图替代成熟的资源、热更或协议库。

## 快速开始

把目录放入 Unity 项目的 `Assets/`，打开入口场景，通过 `Tools > EasyFramework > Setup Wizard` 选择最小本地、标准 UI 或在线更新。预览并应用后，保存入口场景。向导会创建 Startup Config 并绑定场景里的 `FrameworkLauncher`。 [安装步骤](doc/启动流程与运行模式.md#安装向导)

标准 UI 和在线更新起点启用了定时器。调用方持有 Launcher 引用，等待宿主就绪后取用模块。

```csharp
await launcher.Ready;
launcher.Modules.Require<TimerModule>().Delay(1f, () => Log.Info("hello"));
```

默认宿主提供全局 UI 和默认模块作用域。隔离宿主使用指定配置、独立模块实例和运行模式，生命周期随 Launcher。每个 Launcher 都通过显式引用取得启动配置。

可选 OnGUI Debug 由默认宿主的启动配置启用，支持日志、性能、资源租约和 UI 状态，程序集只参与编辑器及开发构建。模块装配器、宿主选择和诊断设置见 [启动流程与运行模式](doc/启动流程与运行模式.md)。

完整 API 和生命周期说明见 [使用说明](doc/使用说明.md)，框架约束和测试要求见 [开发指南](doc/开发指南.md)。
分阶段的职责审查、UI 编辑器与动画优化见 [开发优化计划](doc/开发优化计划.md)。
2.0 的分层设计、工具改造、YooAsset + HybridCLR 组合与发布验收见 [2.0 优化路线](doc/2.0优化路线.md)。
各模块的所有权与异常收尾见 [模块职责](doc/模块职责)。
Tween API 与播放语义见 [Tween 使用说明](doc/Tween使用说明.md)。

UI 业务统一通过 `UIService` 打开、查找和关闭纯 C# View；Prefab 配置、Binding、页面栈与状态机由底层自动管理。详见 [UI 框架使用说明](doc/UI框架使用说明.md)。

资源包可直接拖入 Startup Config。Launcher 会注册资源模块，首次请求时自动初始化对应包。资源地址支持 `package:address`，空闲缓存会自动回收。详见 [资源管理与微信小游戏接入](doc/模块职责/Asset.md)。

## 设计边界

- 不使用反射扫描或大型 DI 容器注册模块。
- 模块完整注册后再按顺序初始化，失败自动回滚。
- 资源使用引用计数租约，网络回调在 Unity 主线程派发。
- YooAsset、HybridCLR 通过可选 Loader 接入，不作为包的强依赖。

## 演示

演示场景为 `Assets/Scenes/FrameworkShowcase.unity`。发布检查可在独立工程副本中运行，历史结果见 `doc/验收记录-1.1.1.md`。

## 当前启动架构

- UI 只生成一个 `Args + View` 脚本；Prefab 引用由运行时 Binding 读取，不强制 ViewModel。
- 框架只保留流程基类、上下文和调度模块；具体流程由项目定义，可直接访问项目 UI 与业务脚本。
- 运行模式：Editor、Online、Offline、Web。
- `SceneModule` 按场景句柄管理切换、叠加和卸载。项目界面可读取 `State` 并订阅 `Changed` 展示进度。

详见 [启动流程与运行模式](doc/启动流程与运行模式.md) 和 [UI 框架使用说明](doc/UI框架使用说明.md)。
