using System;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class FoundationModuleTests
    {
        private struct TestEvent : IEvent
        {
            public int Value;
        }

        private sealed class PooledItem : IPoolable
        {
            public int SpawnCount;
            public int DespawnCount;
            public void OnSpawn() => SpawnCount++;
            public void OnDespawn() => DespawnCount++;
        }

        [Test]
        public void Log_ErrorWithException_PreservesOriginalException()
        {
            Action<string, Exception> previous = Log.ExceptionHandler;
            string actualMessage = null;
            Exception actualException = null;
            var expected = new InvalidOperationException("测试异常");
            try
            {
                Log.ExceptionHandler = (message, exception) =>
                {
                    actualMessage = message;
                    actualException = exception;
                };

                Log.Error("测试消息", expected);

                Assert.AreEqual("测试消息", actualMessage);
                Assert.AreSame(expected, actualException);
            }
            finally
            {
                Log.ExceptionHandler = previous;
            }
        }

        [Test]
        public void Event_UnsubscribeDuringDispatch_DoesNotAllocateSnapshotOrInvokeRemovedHandler()
        {
            var manager = new ModuleManager();
            manager.Register<EventModule>();
            manager.InitAll();

            int firstCalls = 0;
            int secondCalls = 0;
            Action<TestEvent> second = _ => secondCalls++;
            Action<TestEvent> first = _ =>
            {
                firstCalls++;
                EventModule.Instance.Unsubscribe(second);
            };
            EventModule.Instance.Subscribe(first);
            EventModule.Instance.Subscribe(second);

            EventModule.Instance.Dispatch(new TestEvent { Value = 1 });
            EventModule.Instance.Dispatch(new TestEvent { Value = 2 });

            Assert.AreEqual(2, firstCalls);
            Assert.AreEqual(0, secondCalls);
            manager.ShutdownAll();
        }

        [Test]
        public void Pool_RejectsDuplicateRelease_AndHonorsCapacity()
        {
            int destroyed = 0;
            var pool = new ObjectPool<PooledItem>(
                () => new PooledItem(),
                maxSize: 2,
                onDestroy: _ => destroyed++);
            pool.Prewarm(10);
            Assert.AreEqual(2, pool.CountInactive);

            var item = pool.Get();
            pool.Release(item);
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));

            var first = pool.Get();
            var second = pool.Get();
            var third = pool.Get();
            pool.Release(first);
            pool.Release(second);
            pool.Release(third);

            Assert.AreEqual(2, pool.CountInactive);
            Assert.AreEqual(1, destroyed);
        }

        [Test]
        public void Timer_CanCancelTimerCreatedInsideCallback()
        {
            var manager = new ModuleManager();
            manager.Register<TimerModule>();
            manager.InitAll();

            int nestedCalls = 0;
            TimerModule.Instance.Delay(0f, () =>
            {
                int id = TimerModule.Instance.Delay(0f, () => nestedCalls++);
                TimerModule.Instance.Cancel(id);
            });

            manager.Update(0.016f);
            manager.Update(0.016f);

            Assert.AreEqual(0, nestedCalls);
            manager.ShutdownAll();
        }
    }
}
