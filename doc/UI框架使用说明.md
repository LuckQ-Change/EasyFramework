# EasyFramework UI 使用说明

UI 采用“Prefab + 一个纯 C# View + 运行时 Binding”的结构。没有 ViewModel 要求，也不再生成第二个 Binding 脚本。

## 先记住这三个角色

日常业务只需要接触前两个：

| 角色 | 职责 | 业务代码是否常用 |
| --- | --- | --- |
| `UIService` | 打开、查找、返回和关闭页面的统一入口 | 是 |
| `UIView` | 一个页面的行为、状态和组件引用 | 是 |
| `UIDisplay` / `UIManager` | Prefab 配置与底层页面栈 | 仅编辑器或高级扩展 |

推荐保持单向调用：业务系统通过 `UIService` 打开页面，页面内部通过自身的 `Show`、`Hide`、`Close` 等方法控制显示。不要在普通页面逻辑里同时混用 Manager 和 Display。

## 1. 创建页面

打开 `Tools/EasyFramework/UI/Create Display`，默认只需填写界面名称。Prefab 路径、脚本路径和命名空间会记住上次设置，资源地址默认自动生成为 `UI/<界面名称>`；只有项目使用特殊寻址规则时才展开高级设置修改。

```text
LoginView.prefab   // UIDisplay、可选 UIBindingContext、序列化引用
LoginView.cs       // Args + View，唯一生成脚本
```

生成的 View：

```csharp
[UIPrefab("UI/LoginView")]
public sealed class LoginView : UIView<LoginViewArgs>
{
    protected override void OnOpened(LoginViewArgs args)
    {
    }
}
```

`UIView<TArgs>` 只有一个泛型参数。无参数页面可直接继承 `UIView`。

## 2. 获取 Prefab 组件

在 Prefab 任意节点添加 `UIReference` 并拖入目标组件。Target 赋值后 Inspector 会自动生成 Display 内唯一的 Key，也可以按项目习惯修改。例如：

```text
Key: CloseButton
Target: Close/Button
```

View 中直接获取：

```csharp
Button close = Binding.Get<Button>("CloseButton");
close.onClick.AddListener(Close);

if (Binding.TryGet<Image>("Icon", out var icon))
{
    icon.enabled = true;
}
```

引用保存在 Prefab，不需要修改或重新生成脚本。相同 Display 内 Key 必须唯一。

如果引用还配置了 Resource Location：

```csharp
string location = Binding.GetResourceLocation("Icon");
Sprite sprite = await Binding.LoadAsync<Sprite>(location);
```

Binding 销毁时会自动释放它持有的资源 Lease。

## 3. 可选响应式绑定

`UIBindingContext` 仍可用于少量确实需要的响应式绑定，但它是可选能力，不参与脚本生成。普通页面完全可以只使用 `UIReference + Binding.Get<T>()`。

绑定 Source 会优先显示 View 上可绑定响应属性的下拉列表。`bind_` 自动收集默认采用合并模式，不会直接覆盖已有手工绑定；需要完全重建时可在确认窗口选择覆盖。

## 4. 打开与关闭

`OpenAsync` 使用 AssetModule 加载 Prefab。创建向导只负责生成 Prefab 和 View，不会自动注册资源模块或把 Prefab 放进 Loader。项目启动时先注册 AssetModule，在调用 `OpenAsync` 前设置 Loader 并等待初始化；Loader 中的地址必须与 View 的 `UIPrefab` 一致：

```csharp
// 在启动注册阶段执行一次：
ModuleRegistry.AddRegistrar(modules => modules.Register<AssetModule>());

// FrameworkLauncher 完成 Awake 后，以项目提供的 LoginView Prefab 初始化资源：
var loader = new MemoryAssetLoader();
loader.Add("UI/LoginView", loginPrefab);
AssetModule.Instance.SetLoader(loader);
await AssetModule.Instance.InitializeAsync();

LoginView view = await UIService.OpenAsync<LoginView>();
```

如果项目使用 YooAsset，使用相同的地址规则配置其 Loader。编辑器中需要直接验证一个已有 Prefab 时，也可以调用 `UIService.Open<LoginView>(loginPrefab)`，它不经过 AssetModule。

按生成脚本上的 `UIPrefab` 地址打开：

```csharp
LoginView view = await UIService.OpenAsync<LoginView>(args);
```

查询、返回和关闭也使用同一个入口：

```csharp
if (UIService.TryGet<LoginView>(out var login))
{
    login.Hide();
    login.Show();
}

UIService.Back();             // 关闭最上层且响应返回的页面
UIService.Close<LoginView>();
UIService.CloseAll();
```

页面内部可直接控制自身，不需要绕回 Manager：

```csharp
public sealed class LoginView : UIView<LoginViewArgs>
{
    protected override void OnOpened(LoginViewArgs args)
    {
        Binding.Get<Button>("CloseButton").onClick.AddListener(Close);
    }

    private void Collapse() => Hide();
}
```

只有动态资源地址等高级场景才显式指定地址：

```csharp
LoginView view = await UIService.OpenAtAsync<LoginView>(
    "UI/LoginView",
    args,
    UILayer.Screen);
```

`Hide` 只隐藏实例，之后可以 `Show`；`Close` 会结束生命周期并释放 Binding 与资源。`UIManager` 仍负责资源 Lease、单例页面、返回键、层级、共享背景和关闭释放，但普通业务无需直接调用它。

需要取消异步打开时可传入 `CancellationToken`：

```csharp
LoginView view = await UIService.OpenAsync<LoginView>(
    args,
    cancellationToken: token);
```

## 5. Loading 不属于 UI 框架

Loading Prefab 是普通 UI 内容节点，不需要自带 Canvas，也不需要 `UIDisplay`、View 脚本或 `UIManager`。

在普通 Prefab 上挂 `LoadingPresenter`，配置进度条、文本、图片或视频列表，再将 Prefab 赋给 Startup Config 的 `全局 Loading > Prefab`。

Prefab 为空表示不启用全局 Loading，框架不会隐式加载 Resources 中的同名资源。

`LoadingRuntimeHost` 会创建独立的 Canvas、CanvasScaler 和 GraphicRaycaster，并固定使用不依赖相机的 Overlay，确保加载画面直接渲染。Loading 与 UI 共用启动配置中的参考分辨率、宽高匹配值和 Layer，只单独配置 Prefab 与 Sorting Order。

```csharp
await LoadingModule.Instance.LoadSceneAsync("Game");

await LoadingModule.Instance.RunAsync(
    "正在匹配",
    async progress =>
    {
        progress.Report(0.3f);
        await MatchAsync();
    });
```

`LoadingModule` 只维护 `IsLoading`、`Progress`、`Message` 和 `Changed` 事件，不引用 EasyFramework UI。

## 6. 原生 UGUI 与扩展组件

页面里直接用 Unity 自带的 Image、Button、Text 即可。`UIReference`、状态机和 Binding 都不要求先换成框架组件。

`Tools/EasyFramework/UI/UI Editor` 是页面工作台。选中 Prefab 或其子节点后，可在“页面、引用、状态、适配、动画、校验”间切换；校验页中的“定位”会选中并高亮需要修复的组件。
从工作台点击“创建页面 / Item”，向导创建成功后会自动打开 Prefab Mode。状态页可在页面根节点添加 `UIStateController`；选中其下的节点后，可添加 `UIElement` 并记录状态外观。

只有需要 **Sprite 列表 / 按索引切图** 时，才把 Image 换成 `UIImage`：

- Inspector 标题栏：仅 Image / UIImage 显示切换按钮
- `Tools/EasyFramework/UI/UI Editor` → `组件转换`：集中扫描、转换和还原选中层级
- Image / UIImage 组件右键菜单：在两者之间转换

框架不再提供没有额外行为的 Button、Text、Slider 等包装类型；这些组件直接使用原生 UGUI。唯一保留的扩展组件是提供 Sprite 列表能力的 `UIImage`。

切换不会再自动挂上 `UIElement`。状态机是单独加的。

## 7. UI 状态机

在根节点挂 `UIStateController`，只在**会随状态变化的节点**上挂 `UIElement`。

推荐编辑顺序：

1. 在 Controller 的状态列表里添加任意多个状态（如 Normal、Hover、Disabled），并指定当前状态
2. 点状态名，子节点会立刻切换到该状态
3. 在 Scene / Inspector 里改颜色、图片、显隐；编辑器会自动记录受控属性，工作台以标记提示变化
4. 若需显式记录或恢复，可使用工作台对应操作；切换状态时未记录的变化会提示记录、丢弃或取消
5. 保存 Prefab 时会先写入默认外观，随后仍回到当前状态，避免把预览结果存进资源

未勾选的属性沿用默认外观。工作台 `Tools/EasyFramework/UI/State Workbench` 用来按“状态 × 节点”查看和记录。不同分组可以把 Controller 挂在不同父节点上，子节点会跟随最近的父级 Controller。

## 8. 引用 Key、适配与时间轴

选中页面下的组件，在工作台“引用”页点击一键暴露，生成的 Key 只在最近的 `UIDisplay` 内有效。点击“生成 Key 常量”会在 View 脚本同目录创建 `*Keys.g.cs`，业务脚本可用这些常量访问引用。Key 改名后再次生成会列出旧常量的业务使用点；先修复使用点再提交。未绑定目标或重复 Key 在校验页报错。

“适配”页提供 Anchor 与 Layout 预设、安全区组件以及常见分辨率轮廓预览。预览只读 Prefab，计算 Anchor、Pivot、Size Delta 和位置；`LayoutGroup` 的实际排列仍在 Game 视图检查。`UISafeArea` 只在运行时把选中的边映射到 `Screen.safeArea`。

Item 模板通过“创建页面 / Item”向导选择“嵌套组件 Item”。Item 内的引用可以复用页面中已有的 Key；页面绑定只读取属于自己 `UIDisplay` 作用域的引用。长列表虚拟化应在有明确数据量、复用和滚动性能需求的业务列表中实现。

演示场景中的 `WorkbenchListItem.prefab` 与 `UIVirtualList` 展示 80 条逻辑数据的可见行复用。列表只实例化视口范围内的 Item，滚动时重绑行号；页面禁用时移除滚动监听并销毁实例。大数据量列表应使用相同模式，并在具体业务中测量 Item 复杂度与滚动帧率。

在节点添加 `UITimeline` 后，添加颜色、锚点位置、缩放或透明度片段，设置起点、时长、缓动及始末值。“动画”页打开 `Tween Timeline`，拖动进度可调用运行时同一 `TweenSequence.Seek`。退出预览、保存 Prefab、撤销或进入 Play Mode 都会恢复原始外观；运行时调用 `Play()`，关闭页面前调用 `Stop()`。
