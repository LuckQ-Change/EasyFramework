# Reactive Level

1. 在 UI 向导创建 Screen，添加命名为 `txt_LevelLabel` 的文本和命名为 `brn_NextButton` 的按钮，然后保存 Prefab。
2. 在 View 的 `OnCreated` 中用 `Click(NextButton, model.NextLevel)` 绑定按钮。在 `Refresh` 中调用 `Watch(model.Level, level => LevelLabel.text = $"关卡 {level}")`。
3. 打开界面后，`Level.Value` 改变时会即时刷新文本。界面隐藏期间暂停刷新，恢复时自动显示当前值。
