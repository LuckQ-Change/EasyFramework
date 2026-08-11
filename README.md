# EasyFramework

EasyFramework 是面向 Unity 2022.3 LTS 的轻量游戏框架，采用单包、单 Runtime 程序集结构。

默认提供事件、定时器和 C# 对象池；TCP、资源加载和 HybridCLR 热更能力保留在同一包内，由项目显式注册。框架关注生命周期、所有权和主线程派发，不试图替代成熟的资源、热更或协议库。

## 快速开始

把目录放入 Unity 项目的 `Assets/`，进入 Play Mode 后即可使用：

```csharp
TimerModule.Instance.Delay(1f, () => Log.Info("hello"));
```

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

## 设计边界

- 不使用反射扫描或大型 DI 容器注册模块。
- 模块完整注册后再按顺序初始化，失败自动回滚。
- 资源使用引用计数租约，网络回调在 Unity 主线程派发。
- YooAsset、HybridCLR 通过可选 Loader 接入，不作为包的强依赖。

## 测试

在 Unity Test Runner 中运行 `com.wjq.easyframework.tests` EditMode 测试程序集。
