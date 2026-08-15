using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace EasyFramework.Tests
{
    public class AssetModuleTests
    {
        private sealed class ControlledLoader : IAssetLoader
        {
            private readonly TaskCompletionSource<object> _completion =
                new TaskCompletionSource<object>();

            public int LoadCalls { get; private set; }
            public int ReleaseCalls { get; private set; }

            public Task<bool> InitializeAsync() => Task.FromResult(true);

            public async Task<T> LoadAsync<T>(string location) where T : class
            {
                LoadCalls++;
                return (T)await _completion.Task;
            }

            public void Complete(object asset) => _completion.SetResult(asset);
            public void Release(string location) => ReleaseCalls++;
            public void ReleaseAll() { }
        }

        [UnityTest]
        public IEnumerator ConcurrentAcquire_SharesLoad_AndReleasesAfterLastLease()
        {
            Task task = ConcurrentAcquireCore();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
        }

        private static async Task ConcurrentAcquireCore()
        {
            var manager = new ModuleManager();
            manager.Register<AssetModule>();
            manager.InitAll();

            var loader = new ControlledLoader();
            AssetModule.Instance.SetLoader(loader);
            Assert.IsTrue(await AssetModule.Instance.InitializeAsync());

            var firstTask = AssetModule.Instance.AcquireAsync<object>("shared");
            var secondTask = AssetModule.Instance.AcquireAsync<object>("shared");
            Assert.AreEqual(1, loader.LoadCalls);

            var asset = new object();
            loader.Complete(asset);
            var first = await firstTask;
            var second = await secondTask;

            Assert.AreSame(asset, first.Asset);
            Assert.AreSame(asset, second.Asset);
            first.Dispose();
            Assert.AreEqual(0, loader.ReleaseCalls);
            second.Dispose();
            Assert.AreEqual(1, loader.ReleaseCalls);

            manager.ShutdownAll();
        }
    }
}
