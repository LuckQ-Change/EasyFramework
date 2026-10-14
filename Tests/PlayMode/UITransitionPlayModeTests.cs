using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using EasyFramework.Tweening;
using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.Tests
{
    /// <summary>以真实 Play Mode 生命周期验证转场竞争与 UI 资源租约收尾。</summary>
    public sealed class UITransitionPlayModeTests
    {
        private sealed class CountingLoader : IAssetLoader
        {
            private readonly GameObject _prefab;
            public int ReleaseCalls { get; private set; }
            public CountingLoader(GameObject prefab) => _prefab = prefab;
            public Task<bool> InitializeAsync() => Task.FromResult(true);
            public Task<T> LoadAsync<T>(string location) where T : class =>
                Task.FromResult(location == "UI/TransitionTest" ? _prefab as T : null);
            public void Release(string location) => ReleaseCalls++;
            public void ReleaseAll() { }
        }

        [UnityTest]
        public IEnumerator CloseDuringOpen_ThenReopen_ReleasesSharedAssetOnce()
        {
            var prefab = new GameObject("TransitionTest", typeof(RectTransform));
            prefab.SetActive(false);
            prefab.AddComponent<UIDisplay>();
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<UITransition>();
            var modules = new ModuleManager();
            GameObject host = null;
            try
            {
                modules.Register<TweenModule>();
                modules.Register<AssetModule>();
                modules.InitAll();
                var loader = new CountingLoader(prefab);
                AssetModule.Instance.SetLoader(loader);
                Task<bool> initialized = AssetModule.Instance.InitializeAsync();
                while (!initialized.IsCompleted) yield return null;
                Assert.IsTrue(initialized.GetAwaiter().GetResult());

                host = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
                UIManager manager = host.GetComponent<UIRuntimeHost>().Manager;
                Task<UIView> firstOpen = UIService.OpenAtAsync<UIView>("UI/TransitionTest");
                while (!firstOpen.IsCompleted) yield return null;
                UIView first = firstOpen.GetAwaiter().GetResult();
                UIDisplay oldDisplay = first.Display;
                Assert.AreEqual(1, TweenScheduler.ActiveCount);
                Assert.IsFalse(oldDisplay.GetComponent<CanvasGroup>().blocksRaycasts);

                Assert.IsTrue(manager.Close(first));
                Assert.AreEqual(0, loader.ReleaseCalls);
                Task<UIView> secondOpen = UIService.OpenAtAsync<UIView>("UI/TransitionTest");
                while (!secondOpen.IsCompleted) yield return null;
                UIView second = secondOpen.GetAwaiter().GetResult();
                Assert.AreNotSame(first, second);
                Assert.AreEqual(1, manager.OpenCount);

                TweenScheduler.Tick(1f, 1f);
                yield return null;
                Assert.IsTrue(oldDisplay == null);
                Assert.IsTrue(second.Display != null);
                Assert.IsTrue(second.Display.GetComponent<CanvasGroup>().blocksRaycasts);
                Assert.AreEqual(0, loader.ReleaseCalls);

                Assert.IsTrue(manager.Close(second));
                TweenScheduler.Tick(1f, 1f);
                yield return null;
                Assert.AreEqual(0, manager.OpenCount);
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
                Assert.AreEqual(1, loader.ReleaseCalls);
            }
            finally
            {
                TweenScheduler.KillAll();
                if (host != null) Object.Destroy(host);
                Object.Destroy(prefab);
                modules.ShutdownAll();
            }
        }

        [UnityTest]
        public IEnumerator DisablingDuringClose_CompletesOnlyOnce()
        {
            var modules = new ModuleManager();
            modules.Register<TweenModule>();
            modules.InitAll();
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(UITransition));
            try
            {
                UITransition transition = panel.GetComponent<UITransition>();
                transition.PlayOpen();
                int completions = 0;
                transition.PlayClose(() => completions++);
                panel.SetActive(false);
                TweenScheduler.Tick(1f, 1f);
                Assert.AreEqual(1, completions);
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
            }
            finally
            {
                Object.Destroy(panel);
                modules.ShutdownAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReopeningDuringFadeIn_KeepsCurrentAppearanceAndRestoresInput()
        {
            var modules = new ModuleManager();
            modules.Register<TweenModule>();
            modules.InitAll();
            var panel = new GameObject("Panel", typeof(RectTransform),
                typeof(CanvasGroup), typeof(UITransition));
            try
            {
                var group = panel.GetComponent<CanvasGroup>();
                group.alpha = 0.6f;
                panel.transform.localScale = new Vector3(2f, 3f, 1f);
                UITransition transition = panel.GetComponent<UITransition>();
                transition.PlayOpen();
                TweenScheduler.Tick(0.05f, 0.05f);
                float interruptedAlpha = group.alpha;
                Vector3 interruptedScale = panel.transform.localScale;

                transition.PlayOpen();
                Assert.AreEqual(interruptedAlpha, group.alpha, 0.0001f);
                Assert.AreEqual(interruptedScale, panel.transform.localScale);
                Assert.AreEqual(1, TweenScheduler.ActiveCount);
                Assert.IsFalse(group.blocksRaycasts);

                TweenScheduler.Tick(1f, 1f);
                Assert.AreEqual(0.6f, group.alpha, 0.0001f);
                Assert.AreEqual(new Vector3(2f, 3f, 1f), panel.transform.localScale);
                Assert.IsTrue(group.blocksRaycasts);
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
            }
            finally
            {
                Object.Destroy(panel);
                modules.ShutdownAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ZeroDuration_ClosesImmediatelyAndOnlyOnce()
        {
            var modules = new ModuleManager();
            modules.Register<TweenModule>();
            modules.InitAll();
            var panel = new GameObject("Panel", typeof(RectTransform),
                typeof(CanvasGroup), typeof(UITransition));
            try
            {
                UITransition transition = panel.GetComponent<UITransition>();
                typeof(UITransition).GetField("_openDuration", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(transition, 0f);
                typeof(UITransition).GetField("_closeDuration", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(transition, 0f);
                transition.PlayOpen();
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
                Assert.IsTrue(panel.GetComponent<CanvasGroup>().blocksRaycasts);

                int completions = 0;
                transition.PlayClose(() => completions++);
                TweenScheduler.Tick(1f, 1f);
                Assert.AreEqual(1, completions);
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
            }
            finally
            {
                Object.Destroy(panel);
                modules.ShutdownAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingDuringClose_CompletesOnlyOnce()
        {
            var modules = new ModuleManager();
            modules.Register<TweenModule>();
            modules.InitAll();
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(UITransition));
            try
            {
                UITransition transition = panel.GetComponent<UITransition>();
                transition.PlayOpen();
                int completions = 0;
                transition.PlayClose(() => completions++);
                Object.Destroy(panel);
                yield return null;
                TweenScheduler.Tick(1f, 1f);
                Assert.AreEqual(1, completions);
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
            }
            finally
            {
                if (panel != null) Object.Destroy(panel);
                modules.ShutdownAll();
            }
        }

        [UnityTest]
        public IEnumerator TransitionedAndPlainPages_KeepTheSameStackFocusAndBackground()
        {
            var modules = new ModuleManager();
            modules.Register<TweenModule>();
            modules.InitAll();
            var plainPrefab = new GameObject("Plain", typeof(RectTransform));
            plainPrefab.SetActive(false);
            UIDisplay plainDisplay = plainPrefab.AddComponent<UIDisplay>();
            var animatedPrefab = new GameObject("Animated", typeof(RectTransform));
            animatedPrefab.SetActive(false);
            UIDisplay animatedDisplay = animatedPrefab.AddComponent<UIDisplay>();
            animatedPrefab.AddComponent<CanvasGroup>();
            animatedPrefab.AddComponent<UITransition>();
            var backgroundMode = typeof(UIDisplay).GetField("_backgroundMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            backgroundMode.SetValue(plainDisplay, UIBackgroundMode.Modal);
            backgroundMode.SetValue(animatedDisplay, UIBackgroundMode.Modal);
            var host = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            try
            {
                UIManager manager = host.GetComponent<UIRuntimeHost>().Manager;
                UIView plain = manager.Open(plainPrefab, layer: UILayer.Popup);
                UIView animated = manager.Open(animatedPrefab, layer: UILayer.Popup);
                Transform background = manager.GetLayerRoot(UILayer.Popup).Find("[Background]");
                Assert.NotNull(background);
                Assert.AreEqual(2, manager.OpenCount);
                Assert.AreSame(animated, manager.TopView);
                Assert.IsTrue(animated.IsFocused);
                Assert.IsFalse(plain.IsFocused);
                Assert.IsTrue(background.GetComponent<Image>().raycastTarget);

                Assert.IsTrue(manager.Close(animated));
                Assert.AreEqual(1, manager.OpenCount);
                Assert.AreSame(plain, manager.TopView);
                Assert.IsTrue(plain.IsFocused);
                Assert.IsTrue(background.gameObject.activeSelf);
                Assert.IsTrue(background.GetComponent<Image>().raycastTarget);
                Assert.IsFalse(animated.Display.GetComponent<CanvasGroup>().blocksRaycasts);

                Assert.IsTrue(manager.Close(plain));
                Assert.AreEqual(0, manager.OpenCount);
                Assert.IsFalse(background.gameObject.activeSelf);
                TweenScheduler.Tick(1f, 1f);
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(plainPrefab);
                Object.Destroy(animatedPrefab);
                modules.ShutdownAll();
            }
            yield return null;
        }
    }
}
