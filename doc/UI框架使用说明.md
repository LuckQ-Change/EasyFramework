# EasyFramework UI

这套 UI 使用“Prefab Display + 纯 C# 业务 + 可再生 Binding”的结构。Prefab 不挂专用 View 脚本，只挂通用 `EasyUIDisplay`。

## 1. 创建 Display

打开 `Tools/EasyFramework/UI/Create Display`，填写：

- Class / Prefab Name：例如 `InventoryView`、`Item`、`CostItem`。
- Managed As View：页面和弹窗勾选，列表 Item 不勾选。
- Prefab Path：Prefab 保存路径。
- Prefab Location：交给 `AssetModule` 的资源地址。
- Business Path：可编辑业务脚本目录，默认 `Assets/GameScripts/UI/Logic`。
- Binding Path：只读生成目录，默认 `Assets/GameScripts/UI/Generated/Bindings`。
- Business Base：默认 `EasyUIView` 或 `EasyUIItem`；派生 Item 可填写 `Item`。

向导创建 Prefab、`EasyUIDisplay`、`UIBindingContext`，并分别生成业务脚本和 Binding。Display 会记录唯一 Record ID、类型名、基类、两个脚本路径和 Prefab Location。

## 2. Display、业务脚本和 Binding

Prefab 只保存 Unity 数据：

```text
InventoryView.prefab
└─ EasyUIDisplay
   ├─ Script Record
   ├─ View / Layer 配置
   ├─ Shared Background 配置
   └─ UIBindingContext
```

业务脚本是纯 C#，可以修改：

```csharp
public partial class InventoryView
    : EasyUIView<InventoryViewBinding, InventoryViewArgs>
{
    protected override void OnOpened(InventoryViewArgs args)
    {
        Binding.CloseButton.onClick.AddListener(Close);
    }
}
```

`InventoryViewBinding.g.cs` 是只读文件，包含：

- 属性对应的 Transform 路径和强类型组件引用；
- 属性标记上的 Resource Location；
- `UIBindingContext` 的响应式绑定注册；
- View、Binding、Record ID 和 Prefab Location 注册。

修改 Prefab 或标记后，在 `EasyUIDisplay` Inspector 点击“生成 / 更新 Display 脚本”，不要编辑 `.g.cs`。

## 3. 属性标记与资源路径

在需要暴露的节点添加 `EasyUIReference`，为每条 Entry 配置：

- Property Name：生成的属性名，例如 `Icon`、`CloseButton`。
- Target：实际组件。
- Resource Location：可选，例如 `UI/Icons/Coin`。

生成结果同时保留层级路径和资源路径：

```csharp
[EasyUIPath("Content/Icon")]
[EasyUIResource("UI/Icons/Coin")]
public EasyImage Icon { get; private set; }

public const string IconResource = "UI/Icons/Coin";
```

`EasyUIBinding.LoadAsync<T>` 始终通过 `AssetModule.AcquireAsync<T>` 加载，并在 Binding 销毁时释放 Lease：

```csharp
Sprite coin = await Binding.LoadAsync<Sprite>(InventoryViewBinding.IconResource);
```

## 4. Item 继承

业务对象不继承 MonoBehaviour，因此可以使用普通 C# 继承：

```csharp
public partial class Item : EasyUIItem
{
    protected override void OnCreated() { }
}

public partial class CostItem : Item
{
    protected override void OnCreated()
    {
        base.OnCreated();
    }
}
```

生成器会让 `CostItemBinding` 同时继承 `ItemBinding`。基类 Item 的引用和逻辑在 CostItem Display 上仍然有效。

## 5. 响应式绑定

数据源公开 `ReactiveProperty<T>` 字段或属性；非 public 成员使用 `[UIBindable]`：

```csharp
public readonly ReactiveProperty<int> Level = new ReactiveProperty<int>(1);

[UIBindable("CanStart")]
private readonly ReactiveProperty<bool> _canStart = new ReactiveProperty<bool>(true);
```

在 `UIBindingContext` 中：

- Source 有值时，Source Key 提供响应成员下拉选择；
- Target Property 只显示目标组件支持的属性；
- 配置错误直接显示在绑定条目和 Inspector 中；
- `bind_Level`、`bind_Level$Text` 仍可自动收集；
- InputField、Toggle、Slider、Scrollbar、Dropdown 自动支持双向绑定；
- UGUI 与 TMP Text/InputField/Dropdown 均可识别，TMP 不是框架硬依赖。

纯 C# View 默认把自身作为 Binding Source，也可以覆盖：

```csharp
public override object BindingSource => _viewModel;
```

## 6. FGUI 风格状态

根节点添加 `EasyUIStateController`，定义 `Normal`、`Locked`、`Selected` 等状态。子节点使用 `EasyUIElement` 配置 Variant。

- Variant 的 State 从 Controller 状态下拉选择，不再手写字符串；
- 属性根据实际组件过滤，Image 不显示 Text；
- 切换状态先恢复默认值，再应用覆盖值；
- `Tools/EasyFramework/UI/State Workbench` 以“状态 × Display 节点”集中预览和定位；
- 保存 Prefab 时校验未定义状态、空引用、重复属性和过期脚本记录。

## 7. 打开、层级与强类型参数

生成 Binding 后，Prefab Location 已注册，不需要再次写地址：

```csharp
InventoryView view = await EasyUIManager.Instance
    .OpenAsync<InventoryView, InventoryViewArgs>(args);
```

也可以显式使用资源地址：

```csharp
InventoryView view = await EasyUIManager.Instance
    .OpenAsync<InventoryView>("UI/InventoryView", args, UILayer.Screen);
```

两种方式都经过 `AssetModule`。管理器持有独立 `AssetLease<GameObject>`，关闭 Display 后自动释放。支持预加载、取消、返回键、单例复用、隐藏、置顶和分层。

默认层级为 Background、Screen、Window、Popup、Guide、Toast、System。

## 8. 共享 Background 与转场

`EasyUIDisplay.Background Mode`：

- `None`：不显示背景；
- `Shared`：复用通用背景，只显示、不阻断下层输入；
- `Modal`：复用同一个背景并阻断下层输入。

`Close On Background` 控制点击背景关闭。多个 Popup/Modal 永远只使用一个 Background，并自动放在当前最上层目标 Display 的后面。

在 `EasyUIRuntimeHost` 可以指定自定义 Background Prefab；未指定时框架创建默认纯色背景。Display 添加 `EasyUITransition` 后自动执行淡入、缩放和关闭转场。

## 9. UGUI 组件切换

原生 UGUI 组件的 Inspector 标题区可以切换到对应 Easy UI 组件，也可以通过右键菜单还原。切换器会复制序列化属性、修复已加载内容中的组件引用，并在失败时 Undo 回滚。

`EasyImage` 提供独立 Sprite 列表和 Index：

```csharp
image.SetSpriteIndex(2);
image.NextSprite();
image.PreviousSprite();
```

Sprite Index 不依赖状态，也可以由 `ReactiveProperty<int>` 驱动。

## 10. 推荐工作流

1. 用 Create Display 向导创建 Prefab 和脚本记录。
2. 用 `EasyUIReference` 标记业务需要的组件及资源地址。
3. 用 `bind_` 命名或 Inspector 配置响应式绑定。
4. 用 State Controller 和 State Workbench 配置显示状态。
5. 在 Display 上重新生成 Binding，并点击校验。
6. 只修改业务脚本；Prefab 路径、资源地址和绑定路径统一由 `.g.cs` 承担。
