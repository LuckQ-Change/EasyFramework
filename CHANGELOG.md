# Changelog

## [0.2.0] - 2026-10-08

### Changed

- 移除所有类型和脚本文件的 `Easy` 前缀，不提供旧名称兼容层；核心入口统一为 `Entry`、`Runtime`、`FrameworkLauncher`、`LoadingPresenter`、`UIService`、`UIManager`、`UIView`、`UIDisplay` 等。
- 新增 `UIService` 业务入口，统一页面打开、查询、返回和关闭 API；显式资源地址收敛为 `OpenAtAsync` 高级入口。
- `UIView` 新增 `Show`、`Hide`、可见性、焦点与层级状态，页面内部不再需要绕回 Manager/Display。
- Display Inspector 按“页面 / 嵌套 Item”区分配置，隐藏 Item 无关的页面栈、层级和背景选项。
- `OpenDisplays` 改为只读视图，并补齐泛型 `TryGet` 与生成页面打开时的取消令牌传递。
- 重做 UI 编辑体验：创建 Display 默认只需名称，资源地址自动跟随名称，目录支持选择并记忆，输入错误会在创建前提示。
- `UIPrefab` 成为 View 资源地址的权威来源，Display 自动读取并同步该地址，避免 Prefab 与脚本配置漂移。
- `UIReference` 使用紧凑列表编辑器，拖入 Target 后自动生成 Display 内唯一 Key，并即时提示空项和重复项。
- 修复程序集限定 View 类型无法用于响应绑定成员下拉的问题；绑定格式增加编辑期校验，`bind_` 自动收集默认合并且覆盖前确认。
- 新增统一的 `UI Editor` 组件转换窗口。只保留有实际扩展能力的 `Image ↔ UIImage` 转换；删除 Button、Text、Slider 等无行为包装组件。
- Startup Config 与 Display Inspector 重新分组，UI Layer 改为 Unity Layer 下拉，低频字符串配置收进高级区域。

- UI 组件不再强制挂 `UIElement`。原生 UGUI 可直接参与状态机，只有需要图集索引时才把 Image 换成 UIImage。
- Image 与 UIImage 的切换改为按层级处理，并保留已加载引用；不再在每种 UGUI 组件标题栏上推动整表替换。
- UI 状态机的当前状态会立刻应用到子节点；状态列表支持任意多个自定义名称，改名时保留已记录的变体。保存 Prefab 时仍先写入默认外观。

- 移除框架程序集中的具体业务流程与流程专用启动配置，项目流程现在可直接引用项目 UI 和脚本。
- 启动配置支持在 Inspector 中选择项目的首流程脚本，并由 Launcher 自动注册和启动。
- `StartAsync`、`ChangeAsync` 和 `Next` 支持按需自动注册具有公开无参构造函数的流程。
- 流程、Loading、资源与热更基础模块改为由项目按需注册。
- `ProcedureContext` 改为可继承、幂等释放的业务上下文基类，流程支持注册带构造参数的实例。
- UI 与 Loading 共用唯一一套屏幕适配和 Layer 配置；Loading 不再隐式加载 `Resources/LoadingView`。
- 关闭 UI 启动后不再由自动 Manager 绕过配置创建 UI 根节点。
- Runtime 源码注释和主要 Inspector 分组统一为中文。

## [0.1.0] - 2026-08-11

### Changed

- 模块启动改为完整注册后统一初始化，并增加失败回滚和异常隔离。
- 修复自动启动与业务 registrar 同阶段执行时可能漏注册的问题。
- 默认模块缩减为 Event、Timer、Pool；Asset、Network、Hotfix 改为按需注册。
- TCP 使用连接上下文隔离，发送改为异步顺序写入，并增加队列和帧预算。
- 对象池增加归属检查、重复回收检查、命名池、销毁回调和正确容量语义。
- 事件派发改为无 `GetInvocationList` 快照分配的通道实现。
- AssetModule 增加并发加载合并、引用计数和 `AssetLease<T>`。
- TimerModule 支持取消派发过程中刚创建的待处理计时器。

### Added

- Core、Bootstrap、Event、Pool、Timer、Asset、Network EditMode 测试。
- README 和与当前实现一致的使用/开发文档。
