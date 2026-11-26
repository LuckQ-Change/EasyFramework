# Event

`IEvent` 是事件标记。`EventModule` 按所属宿主维护类型化订阅并同步派发。调用方通过 `launcher.Modules.Require<EventModule>()` 获取模块，模块内部通过 `Host.Require<EventModule>()` 获取。订阅、派发和释放在同一宿主线程进行。

`Subscribe` 返回一个 `IDisposable`。在 `OnEnable` 订阅的组件应在 `OnDisable` 释放，在初始化阶段订阅的对象应在销毁时释放。每次订阅都有独立句柄，可重复调用 `Dispose`。模块关闭会释放全部回调。

派发开始时确定本次的订阅范围，回调中新增加的订阅可由后续派发或嵌套派发看到。本次尚未执行的回调被释放后会跳过，单个回调的异常会记录并隔离。
