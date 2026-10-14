# Network

`TcpNetClient` 管理单次 TCP 连接、帧收发与异步任务；`NetMessage` 保存消息 ID 和负载；`NetworkModule` 限流并在 Unity 主线程派发连接、断开和消息事件。依赖 Core，不负责心跳、重连策略或业务协议。

每次连接拥有独立取消源及代数；旧连接只能清理自身，重连后旧事件不能进入新连接的主线程队列。关闭释放套接字并丢弃未处理事件。`NetworkModuleTests` 覆盖半包、坏帧、队列满、断开和重连；外部网络环境不参与演示。
