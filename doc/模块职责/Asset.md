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

## 接入 YooAsset

通过 Unity Package Manager 以 Git URL 安装 YooAsset 3.0.7。可选适配程序集会随 UPM 版本定义启用。 [YooAsset 3.0.7 包声明](https://github.com/tuyoogame/YooAsset/blob/3.0.7/Assets/YooAsset/package.json)

```text
https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#3.0.7
```

每个包配置两处，资源列表和包内地址在收集器中维护一次。

1. 在 **YooAsset 收集器**中创建包，例如 `DefaultPackage`，设置资源目录和地址规则。使用自定义包内地址时，为该包开启地址寻址。目标平台和包清单版本在 YooAsset 打包时选择。 [YooAsset 3.0.7 收集器](https://github.com/tuyoogame/YooAsset/blob/3.0.7/Assets/YooAsset/Editor/BundleCollector/BundleCollectorSetting.cs)
2. 通过 `Assets/Create/EasyFramework/Assets/YooAsset Package` 创建 **EasyFramework 包配置**，将 `PackageName` 填为收集器中的同名包 `DefaultPackage`，将框架 `Alias` 填为 `main` 等业务别名。在线包填写 HTTPS 主 CDN 根目录，可选填备用 CDN。把配置拖入 Startup Config 的「资源包」列表，由 Startup Config 选择运行模式。

例如 YooAsset 将页面 Prefab 收集为 `UI/HomeView`，业务中的完整地址就是 `main:UI/HomeView`。YooAsset 包名用于找到收集结果，框架别名用于选择已注册的包。同一 Startup Config 中每个别名只登记一个包，从标准 UI 的直接引用包切到 YooAsset 时，用同别名的 YooAsset 包替换它，沿用收集器中相同的包内地址。

使用页面系统时，在页面清单登记页面地址和对应 Prefab，其他资源通过同样的地址格式加载。使用 YooAsset 和 HybridCLR 的整套在线发行时，通过热更配置锁定各包的版本，并按需配置发行渠道。 [热更配置](Hotfix.md)

## 资源校验

- **编辑器与 Player 构建前**。框架按 Startup Config 中的别名找到包。YooAsset 包查询同名包的收集结果，直接引用包查询其资源条目。页面校验还会核对地址指向的工程资产与页面清单中的 Prefab GUID。构建前检查启用场景引用到的页面清单，地址缺失、重复或指向错误资源会报告对应页面和包。
- **在线发行前**。发行预检核对 Player 基线及发行锁定的包版本，确认启动地址被正确收集，并校验所需 `.dll.bytes` 的程序集名称和 SHA-256。发布工具从待上传目录检查实际包清单及所需 Bundle。 [发行预检](Hotfix.md#导出和切换渠道)
- **Player 启动时**。普通包按配置初始化并使用 YooAsset 的清单。整套热更按选定发行加载指定版本清单，下载启动资源及依赖，并复核 AOT 元数据和热更 DLL 摘要后再执行入口。 [启动顺序](Hotfix.md#启动放行与重试)

## 微信小游戏附加配置

安装微信官方转换 SDK，并导入同版本 YooAsset 的 **Mini Game** 示例中的微信文件系统。将 [YooAssetWechatFileSystem.cs](../../Samples~/WeChatAssets/YooAssetWechatFileSystem.cs) 导入项目脚本目录，创建 **WeChat File System** 配置，赋给从 CDN 加载的包。微信转换工具负责代码分包，YooAsset 管理资源的清单和 Bundle 请求。

发布前通过微信开发者工具核对首包与分包，并在真机验证冷缓存、弱网重试、切回前台、重复进入退出玩法、内存告警及版本回退。构建与部署需要配置对应平台的合法域名。

[YooAsset 官方微信接入示例](https://github.com/tuyoogame/YooAsset/blob/3.0.7/Assets/YooAsset/Samples~/Space%20Shooter/GameScript/Runtime/PatchLogic/FsmNode/FsmInitializePackage.cs) · [团结引擎微信小游戏部署说明](https://docs.unity.cn/cn/tuanjiemanual/Manual/UploadWeixinMiniGame.html)
