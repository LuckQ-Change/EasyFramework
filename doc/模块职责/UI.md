# UI 模块

`UIView<TArgs>` 和 `UIPopup<TArgs, TResult>` 是业务入口。生成的静态方法提供带类型的打开参数和弹窗结果。`UIManager` 管理 Screen 栈、Popup 层、返回行为、遮罩和 Prefab 租约。 `UIDisplay` 保存在 Prefab 上的页面设置和 Unity 组件引用。

编辑器按项目设置中的关键字与组件类型映射生成强类型成员。独立子组件使用 `UINode` 挂载 `UINodeView`，父页面直接访问生成的组件属性，组件随父页面暂停、恢复和释放。View 在 `Refresh` 刷新数据，`Watch` 管理可见期订阅，`Click` 管理按钮监听。工作台提供创建、改名预览及校验，构建前检查地址和实际包收集结果。

`UIStateController`、`UIElement` 保存状态，`UITransition` 和 `UITimeline` 使用 Tween 播放动画，`UISafeArea` 处理运行时安全区。详细用法见 [UI 框架使用说明](../UI框架使用说明.md)。
