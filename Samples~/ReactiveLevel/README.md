# Reactive Level

1. 在 Canvas 下创建一个 Text，命名为 `bind_Level`。
2. 使用 Create Display 向导创建根节点，`EasyUIDisplay` 会自动带上 `UIBindingContext`；把 `ReactiveLevelModel` 拖到 Source。
3. 点击“自动收集 bind_ 节点”，把格式改成 `关卡 {0}`。
4. 在 `EasyUIDisplay` 点击“生成 / 更新 Display 脚本”，响应绑定会写入对应的只读 Binding。
5. Button 的 OnClick 调用 `ReactiveLevelModel.NextLevel`。

`Level.Value` 改变后，Text 会在同一帧由数据流推送刷新，不需要在 Update 中轮询。
