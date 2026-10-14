# UI

`UIService` 是业务入口；`UIManager` 管理页面栈、焦点、背景和 Prefab 租约；`UIFactory` 创建 Display；`UIDisplay` 描述页面资产；`UIView` 是纯 C# 行为；`UIBinding` 与 `UIReference` 处理组件引用；`UIStateController`、`UIElement` 保存状态；`UITransition` 与 `UITimeline` 使用 Tween 播放动画；`UISafeArea` 处理运行时安全区。编辑器工作台直接修改真实 UGUI Prefab。

页面关闭由 Manager 释放实例和资源租约，转场中断清理输入遮挡与 Tween；引用收集止于最近的 UIDisplay，因此复用 Item 可拥有相同局部 Key。`UIManagerTests`、`UIBindingTests`、`UIStateTests`、`UITimelineTests`、`UIAdaptationTests` 及 PlayMode 转场测试覆盖核心闭环。布局预览只计算锚点几何；LayoutGroup 实际排版需在 Game 视图验收。
