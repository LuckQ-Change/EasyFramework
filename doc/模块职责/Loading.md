# Loading

`LoadingModule` 管理加载作用域、进度与场景切换；`LoadingPresenter` 只处理展示；`LoadingRuntimeHost` 将 Unity 更新接到展示；`LoadingSettings` 保存配置。依赖 Core 和 Unity 场景 API，不持有业务流程。

加载状态由作用域和进度事件推进。取消令牌可以撤销排队任务。场景操作会在提交前检查取消，提交给 Unity 后等待加载完成，再结束作用域。自定义任务可在委托内部主动响应取消。
