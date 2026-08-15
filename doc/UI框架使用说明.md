# EasyFramework UI 使用说明

UI 采用“Prefab + 一个纯 C# View + 运行时 Binding”的结构。没有 ViewModel 要求，也不再生成第二个 Binding 脚本。

## 1. 生成结果

打开 `Tools/EasyFramework/UI/Create Display`，只需配置名称、Prefab 路径、资源地址、命名空间和脚本路径。

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

在 Prefab 任意节点添加 `EasyUIReference`，配置 Key 和目标组件。例如：

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

在普通 Prefab 上挂 `EasyLoadingPresenter`，配置进度条、文本、图片或视频列表，再将 Prefab 赋给 Startup Preset 的 `Loading Display > Prefab`。

如果没有显式指定 Prefab，运行时会自动尝试加载 `Resources/LoadingView.prefab`，可直接用于默认启动演示。

`EasyLoadingRuntimeHost` 会创建独立的 Canvas、CanvasScaler 和 GraphicRaycaster，并固定使用不依赖相机的 Overlay，确保启动画面直接渲染。Loading 使用较高的 Sorting Order，启动期间覆盖其他画面。

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
