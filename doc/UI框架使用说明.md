# EasyFramework UI 使用说明

UI 采用“Prefab + 一个纯 C# View + 运行时 Binding”的结构。没有 ViewModel 要求，也不再生成第二个 Binding 脚本。

## 1. 生成结果

打开 `Tools/EasyFramework/UI/Create Display`，默认只需填写界面名称。Prefab 路径、脚本路径和命名空间会记住上次设置，资源地址默认自动生成为 `UI/<界面名称>`；只有项目使用特殊寻址规则时才展开高级设置修改。

```text
LoginView.prefab   // EasyUIDisplay、可选 UIBindingContext、序列化引用
LoginView.cs       // Args + View，唯一生成脚本
```

生成的 View：

```csharp
[EasyUIPrefab("UI/LoginView")]
public sealed class LoginView : EasyUIView<LoginViewArgs>
{
    protected override void OnOpened(LoginViewArgs args)
    {
    }
}
```

`EasyUIView<TArgs>` 只有一个泛型参数。无参数页面可直接继承 `EasyUIView`。

## 2. 获取 Prefab 组件

在 Prefab 任意节点添加 `EasyUIReference` 并拖入目标组件。Target 赋值后 Inspector 会自动生成 Display 内唯一的 Key，也可以按项目习惯修改。例如：

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

`UIBindingContext` 仍可用于少量确实需要的响应式绑定，但它是可选能力，不参与脚本生成。普通页面完全可以只使用 `EasyUIReference + Binding.Get<T>()`。

绑定 Source 会优先显示 View 上可绑定响应属性的下拉列表。`bind_` 自动收集默认采用合并模式，不会直接覆盖已有手工绑定；需要完全重建时可在确认窗口选择覆盖。

## 4. 打开与关闭

按生成脚本上的 `EasyUIPrefab` 地址打开：

```csharp
LoginView view = await EasyUIManager.Instance
    .OpenAsync<LoginView, LoginViewArgs>(args);
```

也可以直接指定资源地址：

```csharp
EasyUIView view = await EasyUIManager.Instance
    .OpenAsync("UI/LoginView", args, UILayer.Screen);
```

UI Manager 负责资源 Lease、单例页面、返回键、层级、共享背景和关闭释放。

## 5. Loading 不属于 UI 框架

Loading Prefab 是普通 UI 内容节点，不需要自带 Canvas，也不需要 `EasyUIDisplay`、View 脚本或 `EasyUIManager`。

在普通 Prefab 上挂 `EasyLoadingPresenter`，配置进度条、文本、图片或视频列表，再将 Prefab 赋给 Startup Config 的 `全局 Loading > Prefab`。

Prefab 为空表示不启用全局 Loading，框架不会隐式加载 Resources 中的同名资源。

`EasyLoadingRuntimeHost` 会创建独立的 Canvas、CanvasScaler 和 GraphicRaycaster，并固定使用不依赖相机的 Overlay，确保加载画面直接渲染。Loading 与 UI 共用启动配置中的参考分辨率、宽高匹配值和 Layer，只单独配置 Prefab 与 Sorting Order。

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

## 6. 原生 UGUI 与 Easy 组件

页面里直接用 Unity 自带的 Image、Button、Text 即可。`EasyUIReference`、状态机和 Binding 都不要求先换成 Easy 组件。

只有需要 **Sprite 列表 / 按索引切图** 时，才把 Image 换成 `EasyImage`：

- Inspector 标题栏：仅 Image / EasyImage 显示切换按钮
- `Tools/EasyFramework/UI/Easy UI Editor`：集中扫描、转换和还原选中层级
- Image 组件右键菜单：转换为 EasyImage；旧 Easy 包装组件右键菜单：还原为原生 UGUI

`EasyButton`、`EasyText` 等没有额外行为的旧包装类型只保留兼容和还原能力，不再提供正向批量替换入口。新页面继续使用原生 UGUI 即可。

切换不会再自动挂上 `EasyUIElement`。状态机是单独加的。

## 7. UI 状态机

在根节点挂 `EasyUIStateController`，只在**会随状态变化的节点**上挂 `EasyUIElement`。

推荐编辑顺序：

1. 在 Controller 的状态列表里添加任意多个状态（如 Normal、Hover、Disabled），并指定当前状态
2. 点状态名，子节点会立刻切换到该状态
3. 在 Scene / Inspector 里改颜色、图片、显隐
4. 点“将当前外观记录到当前状态”
5. 保存 Prefab 时会先写入默认外观，随后仍回到当前状态，避免把预览结果存进资源

未勾选的属性沿用默认外观。工作台 `Tools/EasyFramework/UI/State Workbench` 用来按“状态 × 节点”查看和记录。不同分组可以把 Controller 挂在不同父节点上，子节点会跟随最近的父级 Controller。
