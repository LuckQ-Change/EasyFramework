using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFramework
{
    /// <summary>通过 Build Settings 和 Unity SceneManager 实现场景适配。</summary>
    public sealed class UnitySceneBackend : ISceneBackend
    {
        private const string SCENE_EXTENSION = ".unity";
        private const string ASSETS_PREFIX = "Assets/";
        private const string STANDBY_SCENE_NAME = "EasyFramework Standby";
        private Action<Scene> _sceneUnloaded;

        /// <summary>转发 Unity 的场景卸载事件。</summary>
        public event Action<Scene> SceneUnloaded
        {
            add
            {
                if (value == null) return;
                if (_sceneUnloaded == null) SceneManager.sceneUnloaded += NotifySceneUnloaded;
                _sceneUnloaded += value;
            }
            remove
            {
                _sceneUnloaded -= value;
                if (_sceneUnloaded == null) SceneManager.sceneUnloaded -= NotifySceneUnloaded;
            }
        }

        /// <summary>当前活动的 Unity 场景。</summary>
        public Scene ActiveScene => SceneManager.GetActiveScene();

        /// <summary>已完成加载并且可由 Unity 场景系统管理的场景数量。</summary>
        public int LoadedSceneCount
        {
            get
            {
                int loaded = 0;
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    if (scene.IsValid() && scene.isLoaded) loaded++;
                }
                return loaded;
            }
        }

        #region 公共方法

        /// <summary>按 Build Settings 解析场景名或完整路径。同名时要求完整路径。</summary>
        /// <param name="sceneNameOrPath">场景名或构建列表中的路径，可包含 .unity 后缀。</param>
        /// <returns>Build Settings 中的真实场景路径。</returns>
        public string ResolvePath(string sceneNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(sceneNameOrPath))
                throw new ArgumentException("请提供场景名称或完整路径。", nameof(sceneNameOrPath));

            string value = WithoutExtension(sceneNameOrPath.Trim().Replace('\\', '/'));
            bool hasDirectory = value.IndexOf('/') >= 0;
            bool hasAssetsPrefix = value.StartsWith(ASSETS_PREFIX, StringComparison.OrdinalIgnoreCase);
            var matches = new List<string>();
            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(index);
                if (string.IsNullOrEmpty(path)) continue;
                string candidate = WithoutExtension(path);
                if (!hasDirectory) candidate = candidate.Substring(candidate.LastIndexOf('/') + 1);
                else if (!hasAssetsPrefix && candidate.StartsWith(ASSETS_PREFIX, StringComparison.OrdinalIgnoreCase))
                    candidate = candidate.Substring(ASSETS_PREFIX.Length);
                if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                    matches.Add(path);
            }

            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0)
                throw new InvalidOperationException($"Build Settings 未启用场景「{sceneNameOrPath}」。");
            throw new InvalidOperationException($"场景「{sceneNameOrPath}」对应多个路径，请使用完整路径。{string.Join("、", matches)}");
        }

        /// <summary>查找已完成加载的 Unity 场景，避免以名字猜测同路径的多个实例。</summary>
        /// <param name="path">Build Settings 中的完整场景路径。</param>
        /// <param name="scene">匹配的场景句柄，找不到时为默认值。</param>
        /// <returns>是否查到唯一的已加载场景。</returns>
        public bool TryGetLoadedScene(string path, out Scene scene)
        {
            scene = default;
            if (string.IsNullOrEmpty(path)) return false;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (!candidate.IsValid() || !candidate.isLoaded ||
                    !string.Equals(candidate.path, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (scene.IsValid())
                    throw new InvalidOperationException($"路径「{path}」已加载多个实例，需按场景句柄处理。");
                scene = candidate;
            }
            return scene.IsValid();
        }

        /// <summary>用构建序号提交叠加加载，并返回本次 Unity 操作新增的场景句柄。</summary>
        /// <param name="scenePath">Build Settings 中的完整场景路径。</param>
        /// <param name="progress">场景加载进度，可传空。</param>
        /// <returns>完成加载的场景。</returns>
        public async Task<Scene> LoadAdditiveAsync(string scenePath, IProgress<float> progress)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
                throw new ArgumentException("请提供完整的场景路径。", nameof(scenePath));
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (buildIndex < 0) throw new InvalidOperationException($"Build Settings 未启用场景「{scenePath}」。");

            var existingHandles = new HashSet<int>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
                existingHandles.Add(SceneManager.GetSceneAt(index).handle);

            // 让 Unity 完成引擎级激活，避免等待在 0.9 时卡住后续的卸载操作。
            AsyncOperation operation = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
            if (operation == null) throw new InvalidOperationException($"Unity 无法提交场景「{scenePath}」的叠加加载。");
            await WaitForOperationAsync(operation, progress);

            Scene loaded = default;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (!candidate.IsValid() || !candidate.isLoaded || existingHandles.Contains(candidate.handle) ||
                    !string.Equals(candidate.path, scenePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (loaded.IsValid())
                    throw new InvalidOperationException($"场景「{scenePath}」出现多个新实例，无法辨别本次加载结果。");
                loaded = candidate;
            }
            if (!loaded.IsValid())
                throw new InvalidOperationException($"Unity 已结束操作，场景「{scenePath}」没有可用的加载结果。");
            return loaded;
        }

        /// <summary>将已加载的 Unity 场景设置为活动场景。</summary>
        /// <param name="scene">需要切换为活动状态的场景。</param>
        /// <returns>Unity 是否接受切换。</returns>
        public bool SetActiveScene(Scene scene) => scene.IsValid() && scene.isLoaded && SceneManager.SetActiveScene(scene);

        /// <summary>为关闭最后一个受管场景创建可承接卸载的空场景。</summary>
        /// <returns>Unity 运行时创建的场景句柄。</returns>
        public Scene CreateStandbyScene()
        {
            string sceneName = STANDBY_SCENE_NAME;
            for (int suffix = 2; SceneManager.GetSceneByName(sceneName).IsValid(); suffix++)
                sceneName = $"{STANDBY_SCENE_NAME} {suffix}";
            Scene standby = SceneManager.CreateScene(sceneName);
            if (!standby.IsValid() || !standby.isLoaded)
                throw new InvalidOperationException("Unity 未能创建用于收尾的空场景。");
            return standby;
        }

        /// <summary>提交场景卸载并等待 Unity 完成销毁。</summary>
        /// <param name="scene">需要卸载的确切场景句柄。</param>
        /// <param name="progress">场景卸载进度，可传空。</param>
        /// <returns>底层场景卸载完成的任务。</returns>
        public async Task UnloadAsync(Scene scene, IProgress<float> progress)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            string label = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
            if (LoadedSceneCount <= 1)
                throw new InvalidOperationException($"场景「{label}」是最后一个已加载场景，请先加载替代场景。");
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null) throw new InvalidOperationException($"Unity 无法提交场景「{label}」的卸载。");
            await WaitForOperationAsync(operation, progress);

            if (scene.IsValid() && scene.isLoaded)
                throw new InvalidOperationException($"Unity 已结束操作，场景「{label}」仍处于加载状态。");
        }

        #endregion

        #region 私有逻辑

        private static string WithoutExtension(string path) =>
            path.EndsWith(SCENE_EXTENSION, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - SCENE_EXTENSION.Length) : path;

        private void NotifySceneUnloaded(Scene scene)
        {
            Action<Scene> handlers = _sceneUnloaded;
            if (handlers == null) return;
            foreach (Action<Scene> handler in handlers.GetInvocationList())
            {
                try { handler(scene); }
                catch (Exception exception) { Log.Error("[Scene] 场景卸载通知失败。", exception); }
            }
        }

        private static async Task WaitForOperationAsync(AsyncOperation operation, IProgress<float> progress)
        {
            float previous = -1f;
            // 进度接收方发生异常时停止通知，已提交的 Unity 操作继续正常收尾。
            while (!operation.isDone)
            {
                float current = Mathf.Clamp01(operation.progress);
                if (current != previous && !TryReport(progress, current)) progress = null;
                previous = current;
                await Task.Yield();
            }
            TryReport(progress, 1f);
        }

        private static bool TryReport(IProgress<float> progress, float value)
        {
            try { progress?.Report(value); }
            catch (Exception exception)
            {
                Log.Error("[Scene] 场景进度接收失败。", exception);
                return false;
            }
            return true;
        }

        #endregion
    }
}
