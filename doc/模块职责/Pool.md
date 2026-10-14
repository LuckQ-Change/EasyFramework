# Pool

`ObjectPool<T>` 管理一个类型的一组空闲与已借出对象；`PoolModule` 管理默认和命名池；`IPoolable` 定义借出、归还回调。依赖 Core，不负责 Unity `GameObject` 的销毁策略，销毁回调由创建者提供。

池拥有创建出的对象，调用方只在借出期间持有。外来对象和重复回收报错；借出或归还回调失败时销毁有问题的对象，避免计数与所有权不一致。`FoundationModuleTests` 验证容量、回收和异常路径。
