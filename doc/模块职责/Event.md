# Event

`IEvent` 是事件标记；`EventModule` 按事件类型维护订阅并同步派发，不管理业务对象生命周期。依赖 Core。订阅者持有委托引用并负责退订；模块关闭清空全部订阅。

派发时新增、移除订阅与嵌套派发保持确定顺序，单个回调异常被隔离。`FoundationModuleTests` 覆盖这些边界。
