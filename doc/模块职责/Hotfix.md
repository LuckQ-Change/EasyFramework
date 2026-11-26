# Hotfix

`FrameworkLauncher` 支持固定随包发行以及可选的 HTTPS 发行渠道。每次启动选定一份完整记录，YooAsset 按记录锁定清单，HybridCLR 装载该记录指定的 AOT 元数据和热更 DLL。项目通过 `HotUpdateEntryContext` 接收启动上下文。

## 配置和出包

1. 选择目标平台，在 Player Settings 选择 IL2CPP。安装 HybridCLR 和对应的 IL2CPP 支持，在 HybridCLR Settings 中登记项目热更程序集。将本地首流程和入口契约保留在 AOT 侧，热更程序集引用 AOT 契约。运行 HybridCLR 的生成步骤，将本次 Player 对应的裁剪后 AOT 元数据、热更 DLL、构建目标与架构归档到唯一 Player 基线。 [HybridCLR 程序集配置](https://www.hybridclr.cn/docs/basic/hotupdateassemblysetting) · [HybridCLR 构建工作流](https://www.hybridclr.cn/docs/basic/buildpipeline)
2. 安装 YooAsset 3.0.7，在资源收集器中将归档的 AOT 元数据和热更 DLL 作为 `.dll.bytes` 资源收集。发布目标平台的 Bundle 和包清单，记下每个包的清单版本。在 Startup Config 的「资源包与缓存」中加入 YooAsset Package 配置，填写包名、别名和同一版本的主备 CDN。原生在线 Player 的运行模式选 `Online`。 [YooAsset 3.0.7](https://github.com/tuyoogame/YooAsset/tree/3.0.7/Assets/YooAsset) · [资源包配置](Asset.md)
3. 通过 `Assets/Create/EasyFramework/Hot Update Startup` 创建发行配置，填写发行 ID、所需 Player 基线、目标平台、各可更新包的别名和固定清单版本。填写启动必需资源的包内地址。通过 `Assets/Create/EasyFramework/Hot Update/HybridCLR Runtime` 创建 HybridCLR 加载器配置，并将它赋给发行配置的 `Runtime Provider`。
4. 在 AOT 元数据和热更 DLL 列表填写 `包别名:包内地址`、程序集简单名和文件的 SHA-256。热更 DLL 按依赖顺序登记，依赖字段列出先装载的热更程序集简单名。填入入口程序集、类型全名和方法名。入口签名为 `public static Task Start(HotUpdateEntryContext context, CancellationToken token)`，方法名以发行配置为准。
5. 在 Startup Config 的「热更启动」中指定「随包发行」，填写此 Player 的基线 ID，并点击「校验热更配置」。不同架构、AOT 裁剪结果或安装基线使用不同 ID。首流程、本地错误页和重试 UI 随 Player 构建，使用本地资源。

单个文件的摘要可用 PowerShell 计算。填写原始 `.dll.bytes` 文件内容的十六进制摘要。

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'path/to/Assembly.dll.bytes'
```

## 导出和切换渠道

1. 使用 `Assets/Create/EasyFramework/Hot Update Channel` 创建渠道资产，填写稳定的渠道 ID 和 HTTPS 指针 URL，把它赋给 Startup Config 的「发行渠道」。渠道留空时固定启动随包发行。
2. 构建包含该启动配置的 IL2CPP Player。把输出路径旁的 `*.easyframework-build.json` 和 Player 一起归档。记录包含构建 GUID、平台、后端、构建类型和实际打包的热更启动配置。
3. 在渠道 Inspector 选择「Player 启动配置」「要发布的发行」「Player 基线记录」和「YooAsset 待上传目录」，点「预检发行」。多包发行可以选同一个平台的构建输出目录。工具核对 Player 基线、收集地址、源程序集名称和 SHA-256，以及实际清单、包版本、Bundle 文件和启动依赖 CRC。它还会按清单从待上传的 Unity AssetBundle 读取 AOT 元数据和热更 DLL 的 `TextAsset.bytes`，逐个匹配发行记录的 SHA-256。承载这些字节的 Bundle 及其依赖应支持 Unity Editor 直接读取。加密包、Raw/Archive 格式和读取失败都会阻止导出，并报告对应产物。导出时会重新校验。
4. 先发布 YooAsset Bundle、对应版本清单和导出的 `release-<sha256>.json`，再更新指针。发行 JSON 放在与指针相同的 HTTPS 目录。主备 CDN 都保留仍可能被选择的整套产物。
5. 对同一份内容保留发行 ID 和包版本。内容变更时使用新发行 ID，并给有变更的资源包发布新版本。导出会把发行 ID 与记录摘要写入 `ProjectSettings/EasyFrameworkReleaseIds.json`，团队共用这份记录可在不同导出目录拦住重复 ID。手动回退渠道时，可把指针切回已存在的完整发行记录。

## 启动、放行与重试

1. `launcher.Ready` 表示本地宿主已就绪。本地首流程可显示进度与错误，发布配置控制的资源包保持关闭。
2. 渠道启动会验证指针、记录摘要和 Player 兼容性，再锁定整份记录并写入待确认状态。YooAsset 按包锁定清单版本，下载启动必需资源与其依赖。框架读取并检查 AOT 元数据和热更 DLL 的摘要。
3. 调用 HybridCLR 补充所需 AOT 元数据。全部成功后，按配置顺序装载热更程序集，并等待约定入口返回的任务。
4. 入口成功后，框架验证本次暂存的页面工厂，再写入渠道的最后可用发行，并保留一份更早的已确认发行。确认保存完成后统一发布工厂，开放受管资源包，结束 `launcher.HotUpdateReady`。

入口返回的任务应覆盖全部必需初始化。受管包在任务成功前保持关闭，入口需要的本地资源可通过 `context.Modules` 获取。`StartupAssetsReady` 在常驻列表引用受管包时一并等待热更就绪。

### 发行作用域

随包 AOT 页面的生成工厂在 Unity 初始化阶段登记。热更页面在约定的入口内调用生成的 `View.RegisterPageFactory()`。自定义工厂也可调用 `context.RegisterPageFactory<TView>(factoryKey, factory)`。这两条入口都会暂存工厂，直到发行通过确认。

入口及后续发行代码可在 Unity 主线程用 `context.Own(handle)` 交付事件订阅句柄、资源租约或资源作用域。C# 事件可以通过 `context.OnDispose(() => publisher.Event -= Handler)` 登记退订。计划失败、取消或宿主结束时，框架撤回该发行的工厂，按登记的逆序释放句柄和执行清理回调。Unity 对象及项目内部状态可通过登记对应的清理回调交付此作用域。

需要渲染热更页面或读取受管资源时，由外部入口等待 `launcher.HotUpdateReady`。同一个热更入口的任务应在工厂全部登记后返回，延迟到任务结束后执行的工厂登记会报错。

本地页面读取 `launcher.HotUpdateStatus` 显示当前阶段，默认宿主的失败窗会展示发行 ID、包版本或字节地址及错误。配置、下载或文件校验失败发生在进程装载前，可以调用 `launcher.RetryHotUpdateAsync()` 重试同一发行。多次点击共享在途操作。原生在线沙盒中的摘要失败会尝试逐出该地址所属的 Bundle，供重试时重新下载。开始补充 AOT 元数据或装载 DLL 后，后续失败需要干净重启。`HotUpdateReady` 会返回错误，失败窗只显示重启提示。

### 恢复规则

- 上次启动留有待确认记录时，重启先排除该发行，从最近的兼容已确认发行回退。如果上次未确认的恰好是最后可用版本，会尝试更早的已确认发行。还可以选用兼容且未被排除的随包发行。
- 渠道超时、指针或记录无效、目标不兼容时，从已确认发行或随包发行启动。复用已确认记录时，仍会验证它指定的清单与文件。断网启动要求本地具备该套清单及启动资源。
- 装载前可重试当前锁定的发行。重启后，曾未确认的发行 ID 会保留在该渠道的拒绝记录中。修复产物后发布新的发行 ID。如果没有可启动的兼容发行，保留本地错误页。
- 通过 `HotUpdateStatus.ReleaseId` 和 `ReleaseSource` 核对实际选择。记录存放在 `Application.persistentDataPath/EasyFramework/HotUpdate`，按应用、渠道、Player 基线、平台和构建类型分开。编辑器与 Player 使用独立记录。

### Player 验收

1. 同一个 IL2CPP Player 启动 R1，等待 `HotUpdateReady`，核对入口和资源都属于 R1。再切渠道到 R2，干净启动，核对入口和资源都切到 R2。
2. 从已确认 R1 的状态单独走失败分支。切到一份在 DLL 摘要校验或入口阶段失败的 R2 记录，并保持指针指向 R2。重启后应显示已确认发行 R1。再重启一次，确认当前渠道仍跳过未确认的 R2。
3. 在 R2 下载或入口期间结束进程。重启应回到兼容的旧整套版本。另测 R2 成功确认后再次启动失败，核对是否回到更早的 R1。
4. 完整缓存 R1 后断开网络启动，核对记录来源与版本。剩余缺包的情况应停在本地错误页。

详细组合范围和集成验收见 [2.0 路线](../2.0优化路线.md)。
