# Procedure

`ProcedureBase` 定义业务流程进入、退出；`ProcedureContext` 保存一次流程运行上下文；`ProcedureModule` 串行调度状态转换。依赖 Core，业务流程可以依赖 UI、Asset 等模块，调度器本身不内置游戏流程。

成功接受 `StartAsync` 后模块拥有上下文并在停止时释放；预取消请求在接受所有权前退出。切换失败保持可解释的当前状态，`ProcedureAndLoadingTests` 覆盖取消、切换与关闭。
