using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor
{
    /// <summary>选择启动配置初始装配的系统。</summary>
    internal enum FrameworkSetupProfile
    {
        MinimalLocal,
        StandardUI,
        OnlineUpdate,
    }

    /// <summary>保存向导中的场景、输出目录和所选在线发行配置。</summary>
    internal sealed class FrameworkSetupRequest
    {
        internal Scene Scene { get; set; }
        internal string Folder { get; set; }
        internal FrameworkSetupProfile Profile { get; set; }
        internal AssetPackageConfig[] Packages { get; set; }
        internal HotUpdateStartupConfig Release { get; set; }
        internal HotUpdateChannelConfig Channel { get; set; }
        internal string BaselineId { get; set; }
    }

    /// <summary>记录一次预检时的场景目标、已有绑定与具体写入路径。</summary>
    internal sealed class FrameworkSetupPlan
    {
        internal Scene Scene { get; }
        internal FrameworkLauncher Launcher { get; }
        internal FrameworkStartupConfig ExistingConfig { get; }
        internal string ConfigPath { get; }
        internal string ManifestPath { get; }
        internal string LocalPackagePath { get; }
        internal string Summary { get; }
        internal bool HasChanges => ConfigPath != null;

        internal FrameworkSetupPlan(Scene scene, FrameworkLauncher launcher, string summary,
            string configPath = null, string manifestPath = null, string localPackagePath = null)
        {
            Scene = scene;
            Launcher = launcher;
            ExistingConfig = launcher != null ? launcher.StartupConfig : null;
            ConfigPath = configPath;
            ManifestPath = manifestPath;
            LocalPackagePath = localPackagePath;
            Summary = summary;
        }
    }

    /// <summary>预检启动装配，并将新资产绑定到指定场景的默认 Launcher。</summary>
    internal static class FrameworkSetupInstaller
    {
        private const string UNDO_LABEL = "安装 EasyFramework";
        private const string LAUNCHER_NAME = "EasyFramework";

        #region 公共方法

        internal static FrameworkSetupPlan Preview(FrameworkSetupRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Scene scene = request.Scene;
            ValidateScene(scene);
            FrameworkLauncher launcher = FindLauncher(scene);
            string sceneLabel = string.IsNullOrEmpty(scene.path) ? scene.name + "（未保存）" : scene.path;
            if (launcher != null && launcher.StartupConfig != null)
            {
                return new FrameworkSetupPlan(scene, launcher,
                    $"场景 {sceneLabel}\n启动器已有配置 {AssetDatabase.GetAssetPath(launcher.StartupConfig)}。");
            }

            ValidateLauncher(launcher);
            ValidateSceneWritable(scene);
            string folder = ValidateFolder(request.Folder);
            string prefix = FilePrefix(scene.name);
            string configPath = NewAssetPath(folder, prefix + "_StartupConfig.asset");
            string manifestPath = null;
            string localPackagePath = null;
            var summary = new StringBuilder();

            switch (request.Profile)
            {
                case FrameworkSetupProfile.MinimalLocal:
                    summary.AppendLine("最小本地 · 模块宿主与场景模块");
                    break;
                case FrameworkSetupProfile.StandardUI:
                    summary.AppendLine("标准 UI · 默认模块、场景与 UI");
                    manifestPath = NewAssetPath(folder, prefix + "_PageManifest.asset");
                    localPackagePath = NewAssetPath(folder, prefix + "_LocalPackage.asset");
                    break;
                case FrameworkSetupProfile.OnlineUpdate:
                    summary.AppendLine("在线更新 · 默认模块、场景、UI 与发行启动");
                    manifestPath = NewAssetPath(folder, prefix + "_PageManifest.asset");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Profile));
            }

            summary.AppendLine($"场景 {sceneLabel}");
            summary.AppendLine(launcher == null ? $"创建场景根节点 {LAUNCHER_NAME}，挂载 Launcher。" :
                $"为现有 Launcher（{launcher.name}）绑定配置。");
            summary.AppendLine($"创建 {configPath}");
            if (manifestPath != null) summary.AppendLine($"创建 {manifestPath}");
            if (localPackagePath != null) summary.AppendLine($"创建 {localPackagePath}（main 直接引用包）");
            if (request.Profile == FrameworkSetupProfile.OnlineUpdate) DescribeOnline(request, summary);

            summary.Append("场景绑定支持一次撤销，生成的配置资产可继续使用。应用后保存入口场景。");
            return new FrameworkSetupPlan(scene, launcher, summary.ToString(), configPath, manifestPath, localPackagePath);
        }

        internal static FrameworkStartupConfig Apply(FrameworkSetupRequest request, FrameworkSetupPlan preview)
        {
            FrameworkSetupPlan plan = Preview(request);
            if (preview == null || plan.Scene != preview.Scene || plan.Launcher != preview.Launcher ||
                !string.Equals(plan.Summary, preview.Summary, StringComparison.Ordinal))
                throw new InvalidOperationException("场景或配置已变化，请重新预览。");
            if (!plan.HasChanges) return plan.ExistingConfig;

            var created = new List<Object>(3);
            int undoGroup = -1;
            GameObject newRoot = null;
            try
            {
                UIPageManifest manifest = plan.ManifestPath != null ? CreateAsset<UIPageManifest>(plan.ManifestPath, created) : null;
                ReferencedAssetPackageConfig package = plan.LocalPackagePath != null
                    ? CreateAsset<ReferencedAssetPackageConfig>(plan.LocalPackagePath, created) : null;
                AssetPackageConfig[] packages = package != null
                    ? new AssetPackageConfig[] { package } : Array.Empty<AssetPackageConfig>();
                if (request.Profile == FrameworkSetupProfile.OnlineUpdate) packages = request.Packages;
                FrameworkStartupConfig config = CreateAsset<FrameworkStartupConfig>(plan.ConfigPath, created,
                    startup => Configure(startup, request, manifest, packages));

                ValidateScene(plan.Scene);
                ValidateSceneWritable(plan.Scene);
                FrameworkLauncher launcher = FindLauncher(plan.Scene);
                if (launcher != plan.Launcher || (launcher != null && launcher.StartupConfig != null))
                    throw new InvalidOperationException("应用期间场景的启动器已变化。");
                ValidateLauncher(launcher);
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UNDO_LABEL);
                if (launcher == null)
                {
                    newRoot = new GameObject(LAUNCHER_NAME);
                    if (newRoot.scene != plan.Scene) SceneManager.MoveGameObjectToScene(newRoot, plan.Scene);
                    Undo.RegisterCreatedObjectUndo(newRoot, UNDO_LABEL);
                    launcher = Undo.AddComponent<FrameworkLauncher>(newRoot);
                }

                using var launcherData = new SerializedObject(launcher);
                launcherData.FindProperty("_startupPreset").objectReferenceValue = config;
                launcherData.ApplyModifiedProperties();
                if (launcher.StartupConfig != config || !EditorSceneManager.MarkSceneDirty(plan.Scene))
                    throw new InvalidOperationException("无法将启动配置绑定到入口场景。");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                return config;
            }
            catch
            {
                // 先撤销场景引用，再清理本次创建的资产。
                try
                {
                    if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                    if (newRoot != null) Object.DestroyImmediate(newRoot);
                    DeleteCreatedAssets(created);
                }
                catch (Exception exception) { Debug.LogError($"无法还原安装时的场景改动：{exception.Message}"); }
                throw;
            }
        }

        #endregion

        #region 私有逻辑

        private static void ValidateScene(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("退出 Play Mode 后再安装。");
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene) ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("请选择已打开的普通场景。");
        }

        private static void ValidateSceneWritable(Scene scene)
        {
            if (!string.IsNullOrEmpty(scene.path) &&
                (!AssetDatabase.IsOpenForEdit(scene.path) || new FileInfo(scene.path).IsReadOnly))
                throw new InvalidOperationException($"入口场景当前不可写：{scene.path}。");
        }

        private static FrameworkLauncher FindLauncher(Scene scene)
        {
            FrameworkLauncher launcher = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (FrameworkLauncher candidate in root.GetComponentsInChildren<FrameworkLauncher>(true))
                {
                    if (candidate == null || candidate.HostMode != FrameworkHostMode.Default) continue;
                    if (launcher != null)
                        throw new InvalidOperationException("场景中存在多个默认 Launcher，请先确定唯一入口。");
                    launcher = candidate;
                }
            }
            return launcher;
        }

        private static void ValidateLauncher(FrameworkLauncher launcher)
        {
            if (launcher == null) return;
            if (launcher.transform.parent != null || !launcher.isActiveAndEnabled)
                throw new InvalidOperationException("默认 Launcher 需要位于场景根节点并启用。");
            using var data = new SerializedObject(launcher);
            if (!data.FindProperty("_startAutomatically").boolValue)
                throw new InvalidOperationException("此 Launcher 由代码启动，请先确定此场景的自动启动入口。");
        }

        private static string ValidateFolder(string folder)
        {
            folder = string.IsNullOrWhiteSpace(folder) ? "Assets" : folder.Trim();
            if ((folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal)) ||
                Array.Exists(folder.Split('/'), segment => segment.Length == 0 || segment == "." || segment == "..") ||
                !AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException("请选择 Assets 下的输出目录。");
            if (folder.Equals("Assets/StreamingAssets", StringComparison.OrdinalIgnoreCase) ||
                folder.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择 StreamingAssets 之外的输出目录。");
            return folder;
        }

        private static string FilePrefix(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Entry";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var prefix = new StringBuilder(name.Length);
            foreach (char value in name) prefix.Append(invalid.Contains(value) ? '_' : value);
            return prefix.ToString();
        }

        private static string NewAssetPath(string folder, string fileName)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}");
            RequireUnusedPath(path);
            return path;
        }

        private static void RequireUnusedPath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)) ||
                File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path + ".meta"))
                throw new InvalidOperationException($"资产路径已被占用：{path}。请更换输出目录后重试。");
        }

        private static T CreateAsset<T>(string path, List<Object> created, Action<T> configure = null) where T : ScriptableObject
        {
            RequireUnusedPath(path);
            T asset = ScriptableObject.CreateInstance<T>();
            created.Add(asset);
            configure?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            if (AssetDatabase.LoadMainAssetAtPath(path) != asset)
                throw new InvalidOperationException($"创建资产失败：{path}。");
            return asset;
        }

        private static void Configure(FrameworkStartupConfig startup, FrameworkSetupRequest request,
            UIPageManifest manifest, AssetPackageConfig[] packages)
        {
            bool hasUI = request.Profile != FrameworkSetupProfile.MinimalLocal;
            bool isOnline = request.Profile == FrameworkSetupProfile.OnlineUpdate;
            using var data = new SerializedObject(startup);
            data.FindProperty("_startUI").boolValue = hasUI;
            data.FindProperty("_builtinModules").intValue = (int)(hasUI ? FrameworkBuiltinModules.Default : FrameworkBuiltinModules.None);
            data.FindProperty("_isSceneModuleEnabled").boolValue = true;
            data.FindProperty("_runtimeMode").intValue = (int)(isOnline ? RuntimeMode.Online : RuntimeMode.Offline);
            data.FindProperty("_pageManifest").objectReferenceValue = manifest;
            SerializedProperty property = data.FindProperty("_assetPackages");
            property.arraySize = packages.Length;
            for (int i = 0; i < packages.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = packages[i];
            if (isOnline)
            {
                data.FindProperty("_hotUpdate").objectReferenceValue = request.Release;
                data.FindProperty("_hotUpdateChannel").objectReferenceValue = request.Channel;
                data.FindProperty("_playerBaselineId").stringValue = request.BaselineId?.Trim() ?? string.Empty;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DescribeOnline(FrameworkSetupRequest request, StringBuilder summary)
        {
            RequireSavedAsset(request.Release, "随包发行");
            if (request.Channel != null) RequireSavedAsset(request.Channel, "发行渠道");
            if (request.Packages == null || request.Packages.Length == 0)
                throw new InvalidOperationException("请添加随包发行需要的资源包配置。");
            foreach (AssetPackageConfig package in request.Packages)
            {
                string path = RequireSavedAsset(package, "资源包");
                summary.AppendLine($"引用资源包 {package.Alias} · {path}");
            }
            summary.AppendLine($"引用随包发行 {AssetDatabase.GetAssetPath(request.Release)}");
            if (request.Channel != null) summary.AppendLine($"引用发行渠道 {AssetDatabase.GetAssetPath(request.Channel)}");
            summary.AppendLine($"Player 基线 {request.BaselineId?.Trim()}");

            var startup = ScriptableObject.CreateInstance<FrameworkStartupConfig>();
            try
            {
                Configure(startup, request, null, request.Packages);
                summary.AppendLine(FrameworkHotUpdateSetup.Validate(startup));
            }
            finally { Object.DestroyImmediate(startup); }
        }

        private static string RequireSavedAsset(Object asset, string label)
        {
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException($"请选择已保存的{label}配置。");
            return path;
        }

        private static void DeleteCreatedAssets(List<Object> created)
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                Object asset = created[i];
                if (asset == null) continue;
                try
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    if (string.IsNullOrEmpty(path)) Object.DestroyImmediate(asset);
                    else if (AssetDatabase.LoadMainAssetAtPath(path) == asset && !AssetDatabase.DeleteAsset(path))
                        Debug.LogError($"无法清理安装失败时创建的资产：{path}。");
                }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        #endregion
    }
}
