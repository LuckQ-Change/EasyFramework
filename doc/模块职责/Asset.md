# 资源管理

在 `Framework Startup Config` 的「资源包」拖入包配置。Launcher 注册 `AssetModule`，对应包在第一次请求时自动初始化。地址写成 `包别名:包内地址`，省略别名时使用 `main`。别名区分大小写，包内地址使用构建清单的完整地址。

```csharp
AssetModule assets = AssetModule.Instance;

// 单个资源在使用期间持有租约。
using var lease = await assets.AcquireAsync<TextAsset>("config:items", cancellationToken);
string json = lease.Asset.text;

// 关卡级对象将 scope 保存到关卡上下文，在退出时 Dispose。
using var scope = assets.CreateScope();
AudioClip music = await scope.LoadAsync<AudioClip>("battle:audio/bgm", cancellationToken);
```

- UI 页面通过生成的 `OpenAsync` 或 `ShowAsync` 获取 Prefab 租约。关闭后由框架释放，界面通过 `LoadAssetAsync<T>()` 加载的资源也随页面一起归还。
- 关卡、音频、场景内对象池使用各自的作用域。去掉组件上的资源引用并销毁实例或清空对象池后，再归还它使用的租约。
- `WarmPackageAsync("battle")` 只使后端及清单就绪。`WarmAsync<T>(address)` 会加载对象预热到有限的空闲缓存，适合少量即将使用的资源。
- 调用方可以取消等待。其他使用者可继续共享同一个加载。尚未发出的请求可以跳过，底层已开始的工作由 SDK 收尾。没有使用者的迟到结果会立即归还。
- 后端错误、包缺失、类型不匹配以异常返回。失败的加载可以重试。模块的公共 API、租约和作用域在 Unity 主线程使用。

## 常驻资源

在 `Framework Startup Config` 的「启动常驻资源」填写地址。配置中的本地引用包先就绪，`FrameworkLauncher.Ready` 完成后接着加载常驻资源。需要这些资源的业务可以等待 `FrameworkLauncher.StartupAssetsReady`。单次加载失败或超时会显示本地重试入口，当前页面可以继续使用。业务可以通过 `StartupAssetError` 和 `RetryStartupAssetsAsync()` 查询失败或触发重试。未完成的底层包操作仍由后端继续处理，重试可继续等待同一批后端操作。

```csharp
var assets = AssetModule.Instance;
await assets.PinAsync("main:Common/Icon");
bool resident = assets.IsResident("main:Common/Icon");
assets.Unpin("main:Common/Icon");
```

同一地址只持有一份常驻租约。空闲回收和内存告警期间会继续保留。取消常驻后，最后一份租约归还时进入正常回收流程，需要立即回收时可调用 `TrimUnusedAsync()`。含常驻资源的包要先解除常驻并归还业务租约才能卸载。框架关闭时自动归还所有常驻租约。

## 缓存与释放

| 策略 | 原生默认 | WebGL / 微信小游戏默认 |
| --- | ---: | ---: |
| 空闲保留时间 | 15 秒 | 5 秒 |
| 空闲资源上限 | 32 项 | 12 项 |
| 空闲估计大小上限 | 24 MiB | 8 MiB |
| 同时发起的独立对象加载 | 6 | 3 |
| 同时初始化的资源包 | 2 | 2 |

可在 Startup Config 覆盖默认值。包初始化和已就绪包中的对象读取分别限流。空闲预算设为零时，最后一个租约归还即释放底层句柄。多个资源竞争缓存时，先回收最久未使用的空闲项。时间使用非缩放时钟。仍持有租约的资源继续受保护。

- `GetSnapshot()` 返回待完成的地址数、活跃资源和租约、空闲项数与估计大小，以及其中的常驻项 `ResidentAssets` 和 `ResidentEstimatedBytes`。常驻项已计入活跃资源。估计值只用于限制空闲缓存。真机总内存还包括依赖 Bundle、纹理、实例和 WebAssembly 堆。
- 在安全过场调用 `TrimUnusedAsync()` 可立即清空空闲缓存，并请求后端清理未使用的 Bundle。正常回收在帧循环中合并执行。
- `Application.lowMemory` 会触发空闲清理，并暂停保留空闲资源 10 秒。小游戏 SDK 可从自己的内存告警回调在 Unity 线程调用 `HandleMemoryPressureAsync()`。
- `UnloadPackageAsync("battle")` 释放对应包的空闲句柄和后端。还有活跃租约、初始化或对象加载时会拒绝卸载。下一次访问重新创建后端并初始化。
- 模块关闭会结束所有租约的有效期，负责释放已经加载和迟到的句柄。显式等待 `StopAsync()` 时，资源模块会等已发出的后端操作结束，并等待包卸载。依赖资源的业务对象应在资源模块关闭前结束自己的作用域。
- 这里的预算用于运行时内存。磁盘与小游戏平台文件缓存由资源后端和平台 SDK 按其策略管理。进关卡前的文件预下载可以接到 YooAsset 的平台策略上。

## YooAsset 与微信小游戏

1. 通过 Unity Package Manager 以 Git URL 安装 YooAsset 3.0.7：`https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#3.0.7`。可选适配程序集会随 UPM 版本定义启用。
2. 在 YooAsset 构建工具中配置包和地址。通过 `Assets/Create/EasyFramework/Assets/YooAsset Package` 创建配置，填写对应的 YooAsset 包名和业务别名，加入 Startup Config。每个配置使用独立包名。在线包需要对应构建平台和每个包的 HTTPS CDN 根目录，支持备份 CDN 和可访问的已发布清单版本回退。
3. 微信小游戏项目安装官方转换 SDK，并导入同版本 YooAsset 的 **Mini Game** 示例中的微信文件系统。将 [YooAssetWechatFileSystem.cs](../../Samples~/WeChatAssets/YooAssetWechatFileSystem.cs) 导入项目脚本目录，创建一个 **WeChat File System** 配置，再赋给需要从 CDN 加载的包。示例调用 YooAsset 的微信创建器，使用平台文件缓存及下载并发设置。
4. 包别名可以按首屏、玩法和活动组织，启动只请求首屏需要的资源。极小的启动场景和 Loading Prefab 可直接随首包打入。微信转换工具负责小游戏代码分包。YooAsset 负责资源包、清单和 Bundle 请求。

发布前在目标设备验证冷缓存、弱网重试、后台切回、重复进入退出玩法、内存告警和最新版本/回退版本。通过微信开发者工具核对小游戏首包与分包，通过真机与 Profiler 核对实际内存峰值及回落。配置示例依赖各平台的实际构建产物、导出目标和合法域名。

[YooAsset 3.0.7 源码与依赖](https://github.com/tuyoogame/YooAsset/tree/3.0.7/Assets/YooAsset) · [YooAsset 官方微信接入示例](https://github.com/tuyoogame/YooAsset/blob/3.0.7/Assets/YooAsset/Samples~/Space%20Shooter/GameScript/Runtime/PatchLogic/FsmNode/FsmInitializePackage.cs) · [团结引擎微信小游戏部署说明](https://docs.unity.cn/cn/tuanjiemanual/Manual/UploadWeixinMiniGame.html)
