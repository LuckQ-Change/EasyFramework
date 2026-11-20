# UI 框架使用说明

## 创建界面和绑定控件

1. 在 `Project Settings > EasyFramework UI` 设置 Prefab 目录、业务脚本默认目录、生成脚本目录、命名空间和资源包。可在「控件关键字映射」添加关键字并搜索组件类型。
2. 打开 `Tools > EasyFramework > UI > UI Editor`，点「创建页面 / Node / Item」。输入类名，选择 `Screen`、`Popup`、`Node` 或 `Item`。已有对象可在 Inspector 配置挂载组件的类型和业务脚本。
3. 在 Prefab 中按「关键字_属性名」命名节点，例如 `brn_SellButton`、`txt_CountLabel`，然后保存。工作台「引用」页可按选中节点已有的组件列出匹配关键字。
4. 在业务脚本里直接写 `SellButton`、`CountLabel`。向导生成的 `.g.cs` 保存组件类型和访问代码，之后由编辑器维护。

默认映射包括 `brn` → `Button`、`txt` → `Text`、`img` → `Image` 和 `node` → `UINode`。新增映射时填写关键字并选择组件类型。同一界面的属性名应唯一，内嵌 Item 的控件从属于该 Item。改动已有关键字后，按新关键字调整受影响的 Prefab 节点名并保存。

工作台「引用」页会显示改名后的属性。点「预览」可以跟到脚本里的使用位置，再点「确认改名并保存」同步生成文件、业务代码和 Prefab。业务引用按当前编辑器平台的 C# 编译条件解析。删除节点后可在此页移除失效引用。

## 生成脚本目录与热更新

默认把生成文件集中存放在 `Assets/Generated/UI`。可在项目设置的「生成脚本目录」修改路径，现有生成文件随设置一起迁移。创建 UI 时可单独选择该页面或组件的业务脚本目录。移动业务脚本时，通过 Unity 移动资产并保留其 GUID，生成文件会跟随程序集归属调整。

生成的 `.g.cs` 是业务类的 `partial` 部分，两者一起编译。有多个自定义程序集时，生成目录内按需要创建带 `.asmref` 的子目录，让各份生成文件编译到各自的业务程序集。使用 Unity 默认业务程序集的 UI，应将生成目录选在同一默认程序集覆盖的范围内。

热更包的拆分单位仍是业务 DLL。按业务模块配置 `.asmdef` 后，各模块的生成绑定会随各自的 DLL 一起更新。热更资源收集按实际需要登记 DLL 和相关资源。

## 页面、弹窗和返回

```csharp
await InventoryView.OpenAsync(new InventoryViewArgs(playerId));

var result = await SellDialog.ShowAsync(new SellDialogArgs(itemId));
if (result.Confirmed)
    await SellItemAsync(result.Value);

await UIService.BackAsync();
```

- Screen 打开后会隐藏上一张 Screen。上一张页面仍持有原实例，返回时原滚动位置等界面状态可以保留，框架会再次执行 `Refresh` 读取最新数据。
- 重复打开同类型会复用实例、更新参数并移到该层顶端。弹窗叠在 Screen 上，并可用遮罩阻挡背景输入。同一个弹窗再次打开时，先前的调用收到取消结果，新的调用等待玩家操作。
- 弹窗可以用 `ConfirmAsync(value)` 返回值，返回键、遮罩、`CloseAsync` 默认产生取消结果。等待用的 `CancellationToken` 取消时，当前调用会抛出取消异常。
- `UIService.Get<T>()` 可获取仍在栈中的界面，`UIService.CloseAsync<T>()` 可定向关闭，界面内部直接调用 `CloseAsync()`。框架在退场和对象清理后归还 Prefab 租约。
- 全屏页面以最后一次开页请求为准。被后续请求或返回操作取代的任务会取消，调用方可按正常的取消流程处理。

## 数据和交互

常规数据在 `Refresh` 里赋值。调用方传 ID 等必要参数，View 从业务模块读取当前数据。按钮在 `OnCreated` 绑定一次。确实持续变化的字段可用 `Watch`，隐藏或关闭时自动取消订阅，恢复时直接读取当前值。

```csharp
public sealed partial class InventoryView : UIView<InventoryViewArgs>
{
    protected override void OnCreated() =>
        Click(SellButton, SellSelected);

    protected override void Refresh(InventoryViewArgs args)
    {
        var inventory = InventoryModule.Instance.Get(args.PlayerId);
        TitleLabel.text = inventory.Title;
        Watch(inventory.Count, count => CountLabel.text = count.ToString());
    }

    private void SellSelected() { /* 执行业务操作 */ }
}
```

Item 的业务类继承 `UIItem`，复用时调用 `GetComponent<UIDisplay>().GetItem<MyItem>()` 取得一次实例。设置数据后调用 `Rebind()`，通过 Item 的 `Refresh()` 访问同样的强类型控件。回收时禁用 GameObject，框架会暂停 Item 的可见期订阅。

## 独立组件 Node

页面根节点使用 `UIDisplay`，独立组件根节点使用 `UINode`。向导选择 `Node` 会生成组件 Prefab 和继承 `UINodeView` 的业务脚本。也可给现有 RectTransform 添加 `UINode`，在 Inspector 关联脚本。

把玩家卡 Prefab 放进页面后，将嵌套节点命名为 `node_PlayerCard`，再保存父 Prefab。父页面得到类型为 `PlayerCard` 的属性，子组件拥有自己的 `PlayerName` 和 `PlayerLevel`。默认的 `node` 关键字映射到 `UINode`，可在项目设置调整。

```csharp
protected override void Refresh(WorkbenchDemoViewArgs args)
{
    PlayerCard.SetPlayer("林星河", 28, health: 76, maxHealth: 100);
}
```

玩家卡内嵌独立的 `HealthBar` Prefab，实例名为 `node_HealthBar`。玩家卡直接调用生成的 `HealthBar.SetHealth(current, maximum)`，血条自己管理填充区和数值文字。组件在父页面初始化时自动创建，隐藏会暂停订阅并归还组件动态加载的资源，显示时使用最新数据刷新。关闭父页面会一并释放组件。可在 `FrameworkShowcase` 的「UI 工作台 · 制作闭环」演示里查看玩家卡和血条。

## 虚拟列表

1. 在 Scroll Rect 上添加 `UIVirtualList`，并配置列表下的 Viewport 和 Content。由虚拟列表负责计算 Content 尺寸和 Item 位置。
2. 在「Item 模板」中添加多种 Prefab，例如 `header` 和 `item`。纵向和网格使用模板行高，横向使用模板项宽。可配置四边内边距、行或项间距、层级缩进，网格还可设置列数和列间距。网格的分组标题独占一行，组内子项按列排列。
3. 点 Inspector 的「预览虚拟列表」，使用当前模板和布局查看示例数据、滚动和折叠效果。也可在 UI 工作台「适配」页打开预览。

业务页通过 `SetItems` 提交树形数据。为每一项提供稳定、唯一的 Key，再次提交数据时可保留同 Key 分组的展开状态。仅选择展开的分支参与排版。

```csharp
InventoryList.SetItems(new[]
{
    new UIVirtualListRow<string>("weapons", "header", "武器",
        new[] { new UIVirtualListRow<string>("sword", "item", "长剑") },
        initiallyExpanded: true),
});
```

列表 Item 继承 `UIListItem<T>`，在 `Refresh()` 中用 `Data` 取当前数据，通过 `Row` 取得层级、展开状态或调用 `Row.ToggleExpanded()`。控件仍按关键字生成，例如直接在 `OnCreated` 使用 `Click(RowButton, () => Row.ToggleExpanded())`。使用普通 Prefab 时也可给 `SetItems` 传一个绑定回调。

使用 `InventoryList.UpdateItem("sword", "强化长剑")` 按 Key 替换数据。已有数据对象的属性发生变化时，调用 `InventoryList.RefreshItem("sword")` 重新绑定。已渲染的项立即更新，屏幕外和折叠中的项下次显示时读取最新数据。换模板或调整树结构时继续使用 `SetItems`。

## UI 时间轴

在页面或 Node 上添加 `UITimeline`，从 Inspector 打开「时间轴工作台」。在同一个组件里创建多段命名动画，例如 `Open`、`Close` 和 `Pulse`。

- **片段**支持在时间轴上拖动位置或拖拽右端调整时长，可从场景捕获起点和终点，也可填数值。
- **关键帧**按目标与属性建立轨道。移动游标，调整目标后点「在游标处记录关键帧」。也可直接编辑关键帧值、拖动时间，并为每键设置到下一帧的缓动。
- **预览**支持拖动标尺、输入时间、播放和暂停。停止、保存、退出工作台或进入 Play Mode 时，工作台恢复预览前的目标属性。
- **自动开关**在工作台将动画「设为开场」或「设为退场」。自动进退场使用非缩放时间，关闭时等待退场播放完再释放页面。普通动效从生成的控件属性调用 `Timeline.Play("Pulse")`，使用时间轴配置的计时方式。

同一段动画中，不同属性可以并行。同一目标的同一属性发生片段重叠或关键帧时间重复时，工作台与构建校验会提示目标和时间。示例页面 `WorkbenchDemoView` 的开场同时使用缩放片段和旋转关键帧。

## 资源与构建

向导按项目设置生成页面地址，例如 `shop:UI/InventoryView`。将 UI Prefab 放入对应资源包的收集规则，并在启动配置里注册该包。使用 YooAsset 时，在包中启用可寻址地址，确认收集器生成的包内地址与 UIDisplay 一致。演示工程的 `showcase` 包由启动配置显式引用，适用于少量内置资源。分包项目可给不同收集目录设置对应资源包别名。

工作台「校验」会列出可定位的页面配置和组件问题。构建前会检查失效引用、重复页面类型或地址以及 Prefab 是否已被所配置的包收集。页面内部用 `LoadAssetAsync<T>(address)` 加载的资源归属于界面作用域，关闭时统一归还。

头像、图标等会替换的图片可以在 `Refresh` 中调用 `SetSpriteAsync(Avatar, address)`。同一控件改图时以最新请求为准，新图到手后归还旧图。隐藏界面会取消待加载请求，并归还已绑定的动态图片。运行时选中 `[UI]`，Inspector 会显示每个页面的租约数及估算大小，共享资源可能在多个页面的估算值中出现。
