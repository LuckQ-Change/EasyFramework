# UI 框架使用说明

## 创建界面和绑定控件

1. 在 `Project Settings > EasyFramework UI` 设置 Prefab 目录、业务脚本默认目录、生成脚本目录、命名空间和资源包。可在「控件关键字映射」添加关键字并搜索组件类型。
2. 通过 `Assets > Create > EasyFramework > UI > Page Manifest` 创建页面清单，在 Startup Config 的「UI 启动」中关联。打开 `Tools > EasyFramework > UI > UI Editor`，点「创建页面 / Node / Item」。创建 `Screen` 或 `Popup` 时选择该清单，确认页面 ID。向导生成 Prefab、View 和工厂，同时登记页面。Node 与 Item 仍通过各自组件绑定。
3. 在 Prefab 中按「关键字_属性名」命名节点，例如 `brn_SellButton`、`txt_CountLabel`，然后保存。工作台「引用」页可按选中节点已有的组件列出匹配关键字。
4. 在业务脚本里直接写 `SellButton`、`CountLabel`。向导生成的 `.g.cs` 保存组件类型和访问代码，之后由编辑器维护。

默认映射包括 `brn` → `Button`、`txt` → `Text`、`img` → `Image` 和 `node` → `UINode`。新增映射时填写关键字并选择组件类型。同一界面的属性名应唯一，内嵌 Item 的控件从属于该 Item。改动已有关键字后，按新关键字调整受影响的 Prefab 节点名并保存。

工作台「引用」页会显示改名后的属性。点「预览」可以跟到脚本里的使用位置，再点「确认改名并保存」同步生成文件、业务代码和 Prefab。业务引用按当前编辑器平台的 C# 编译条件解析。删除节点后可在此页移除失效引用。

## 生成脚本目录与热更新

生成脚本默认放在 `Assets/GameMain/Runtime/Generated/`。在项目设置中修改「生成脚本目录」会移动现有生成文件。创建 UI 时可单独选择业务脚本目录。通过 Unity 移动业务脚本时，生成文件会跟随程序集归属调整。

生成的 `.g.cs` 是业务类的 `partial` 部分，两者一起编译。有多个自定义程序集时，生成目录内按需要创建带 `.asmref` 的子目录，让各份生成文件编译到各自的业务程序集。使用 Unity 默认业务程序集的 UI，应将生成目录选在同一默认程序集覆盖的范围内。

热更包的拆分单位仍是业务 DLL，生成绑定随各自的 DLL 交付。常规程序集由 Unity 运行时初始化回调登记页面工厂。动态加载的 DLL 由热更入口显式调用相应 View 的 `RegisterPageFactory()`，入口任务成功后再开放受管页面资源。

## 页面清单

清单统一保存页面 ID、Prefab 地址、Screen 或 Popup 路由、返回键和遮罩策略，以及基于 View 脚本 GUID 的工厂键。Prefab 持有控件绑定和过渡配置。清单保存 Prefab GUID 供编辑器定位，资源按已登记地址加载。

`InventoryView.OpenAsync(...)`、`UIService.OpenAsync<...>(...)` 和按 ID 开页使用同一条登记。页面 ID 可用于协议、本地导航或菜单配置。

```csharp
await UIService.OpenAsync("Game.UI.InventoryView", new InventoryViewArgs(playerId));
```

需要等待弹窗结果时继续使用强类型的 `ShowAsync`。统一清单的地址、返回键和遮罩行为在清单 Inspector 调整。

在页面向导选择清单并创建页面，再把清单关联到 Startup Config。在清单 Inspector 使用「预览地址变更」或「预览移除登记」确认要修改的直接引用包条目。应用后可用一次 Undo 恢复清单和包，其他页面或常驻资源使用的地址会保留。YooAsset 条目通过收集器维护。使用「校验登记」核对 ID、Prefab、工厂和地址。

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

## UI 状态

选中 `UIStateController`，点击 Inspector 的「打开 UIState 工作台」，或使用 `Tools > EasyFramework > UI > State Workbench`。左侧管理状态顺序和运行时初态。右侧支持按路径搜索节点、筛选当前状态的覆盖和多选节点。

1. 多选节点后勾选要控制的字段，点击「纳入控制」。节点当前值作为这些字段的基准。
2. 选择状态，在节点字段中直接修改覆盖，或在批量编辑区填写值后点「应用覆盖」。点「恢复基准」可清除当前状态所选字段的覆盖。每次批量命令对应一次 Undo。
3. 点击「打开隔离预览」，在 Scene 视图编辑副本。在工作台选择相应节点和字段，再点「记录预览覆盖」提交变化。直接结束预览会丢弃副本。工作台会提示 UIState 与时间轴共同控制的字段。

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
- **事件帧**。把游标移到触发点，点击「游标处添加事件」，在事件轨选中标记后编辑名称和时间。也可拖动标记或删除。事件可放在结束点之后，动画会延长到该事件的时间。
- **预览**支持拖动标尺、输入时间、播放和暂停。停止、保存、退出工作台或进入 Play Mode 时，工作台恢复预览前的目标属性。
- **自动开关**在工作台将动画「设为开场」或「设为退场」。自定义进退场与默认淡入淡出使用同一个时间轴播放器，按非缩放时间推进。关闭时等待退场结束再释放页面。普通动效调用 `Timeline.Play("Pulse")`，使用时间轴配置的计时方式。

同一段动画中，不同属性可以并行。同一目标的同一属性发生片段重叠或关键帧时间重复时，工作台与构建校验会提示目标和时间。示例页面 `WorkbenchDemoView` 的开场同时使用缩放片段和旋转关键帧。

### 共享转场预设

在 Project 窗口通过 `Create > EasyFramework > UI > Transition Preset` 创建预设，将它拖到页面 `UITransition` 的「共享转场预设」。要统一多个页面的转场，把同一资产分配给这些页面。

进场和退场可分别设置时长、缓动、透明度比例与缩放倍率。进场的比例用于起点，退场的比例用于终点。每个页面都按自己的原始透明度和缩放计算端点。时长设为 0 会立即应用终值。关闭某一阶段的缩放后，该阶段只播放透明度。

进场和退场各自按「自定义时间轴 → 共享预设 → 标准淡入淡出」选用配置。UI 工作台和构建校验会检查页面实际使用的预设参数。

### 订阅事件帧

通过 `UITimeline.EventTriggered` 接收 `UITimelineEvent`。它包含动画名 `AnimationName`、事件名 `EventName` 和时间 `Time`。在生成的 View 中，可在 `OnCreated` 订阅并在 `OnDispose` 解除。0 秒事件在启动播放时发出，订阅应在播放前完成。

```csharp
protected override void OnCreated()
{
    if (Timeline != null) Timeline.EventTriggered += OnTimelineEvent;
}

protected override void OnDispose()
{
    if (Timeline != null) Timeline.EventTriggered -= OnTimelineEvent;
}

private void OnTimelineEvent(UITimelineEvent marker)
{
    if (marker.AnimationName == "Open" && marker.EventName == "Reveal")
        RevealContent();
}
```

一帧跨过多个事件时，先按时间派发，同一时间按登记顺序派发。末尾事件先于完成回调。暂停续播保留进度，`Restart()` 从头发出事件。回调停止当前播放或使页面开始关闭时，本次播放停止派发后续事件。

编辑器预览、`Seek()` 和强制 `Complete()` 都静默处理事件帧。调用 `Seek(t)` 后继续播放，只派发时间大于 `t` 的事件。需要再次触发 0 秒事件时，可重播动画。

## 资源与构建

从安装向导点「创建页面」，或选中 Startup Config 后使用 `Tools > EasyFramework > UI > Create Display`。创建器会带入页面清单，资源包列表取自该配置。也可以在创建器里直接选择清单和资源包。

页面地址由选中包的别名和项目设置的路径前缀组成，例如 `shop:UI/InventoryView`。选中直接引用包时，创建器同时保存页面清单和包条目，包内只登记 `UI/InventoryView` 并指向生成的 Prefab。选中 YooAsset 时，通过收集器将 Prefab 收集为清单对应的包内地址。

工作台「校验」会列出可定位的页面配置和组件问题。构建校验按本次 Player 构建场景所引用的 Startup Config 检查页面清单，包括重复 ID 和地址、Prefab 与工厂绑定、控件引用和资源收集。脚本构建可通过 `BuildPlayerOptions.scenes` 指定场景，页面清单校验与离线预检使用同一份列表。未登记的草稿可独立保存。页面内部用 `LoadAssetAsync<T>(address)` 加载的资源归属于界面作用域，关闭时统一归还。

头像、图标等会替换的图片可以在 `Refresh` 中调用 `SetSpriteAsync(Avatar, address)`。同一控件改图时以最新请求为准，新图到手后归还旧图。隐藏界面会取消待加载请求，并归还已绑定的动态图片。运行时选中 `[UI]`，Inspector 会显示每个页面的租约数及估算大小，共享资源可能在多个页面的估算值中出现。
