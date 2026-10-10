# EasyFramework

EasyFramework 是面向 Unity 2022.3 LTS 的轻量游戏框架，采用单包、单 Runtime 程序集结构。

默认提供事件、定时器和 C# 对象池；TCP、资源加载和 HybridCLR 热更能力保留在同一包内，由项目显式注册。框架关注生命周期、所有权和主线程派发，不试图替代成熟的资源、热更或协议库。

## 快速开始

把目录放入 Unity 项目的 `Assets/`，进入 Play Mode 后即可使用：

```csharp
TimerModule.Instance.Delay(1f, () => Log.Info("hello"));
```

自动启动会应用内置启动配置：创建持久化 UI 根节点、独立 UI Camera、Canvas 和 EventSystem。
可通过 `Assets/Create/EasyFramework/Startup Config` 创建自定义配置，并挂到场景中的
`FrameworkLauncher`；自动创建 Launcher 时，也可以把配置保存为 `Resources/FrameworkStartupConfig.asset`。

Development Build 中会出现 `DEBUG` 悬浮按钮，点击可查看设备日志、异常堆栈、FPS 和系统信息。
在 Startup Config 的“真机调试窗口”中选择 `Always` 可在正式包中启用，选择 `Disabled` 可关闭；
没有 Launcher 的场景也会按 Resources 中的配置创建该窗口。日志只保留最近 500 条。

按需模块通过 `ModuleRegistry.AddRegistrar` 注册：

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void RegisterModules()
{
    ModuleRegistry.AddRegistrar(modules =>
    {
        modules.Register<AssetModule>();
        modules.Register<NetworkModule>();
        modules.Register<HotfixModule>();
    });
}
```

完整 API 和生命周期说明见 [使用说明](doc/使用说明.md)，框架约束和测试要求见 [开发指南](doc/开发指南.md)。

UI 业务统一通过 `UIService` 打开、查找和关闭纯 C# View；Prefab 配置、Binding、页面栈与状态机由底层自动管理。详见 [UI 框架使用说明](doc/UI框架使用说明.md)。

## 设计边界

- 不使用反射扫描或大型 DI 容器注册模块。
- 模块完整注册后再按顺序初始化，失败自动回滚。
- 资源使用引用计数租约，网络回调在 Unity 主线程派发。
- YooAsset、HybridCLR 通过可选 Loader 接入，不作为包的强依赖。

## 测试

在 Unity Test Runner 中运行 `com.wjq.easyframework.tests` EditMode 测试程序集。

## 当前启动架构

- UI 只生成一个 `Args + View` 脚本；Prefab 引用由运行时 Binding 读取，不强制 ViewModel。
- 框架只保留流程基类、上下文和调度模块；具体流程由项目定义，可直接访问项目 UI 与业务脚本。
- 运行模式：Editor、Online、Offline、Web。
- UI 与全局 Loading 共用一套屏幕适配配置；Loading Prefab 为空时不创建，也不会隐式查找 Resources。
- 全局 Loading 不依赖 UI 框架，支持场景切换、图片轮换、视频列表、进度和配置文本轮询。

详见 [启动流程与运行模式](doc/启动流程与运行模式.md) 和 [UI 框架使用说明](doc/UI框架使用说明.md)。
