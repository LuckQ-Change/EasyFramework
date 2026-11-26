using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>显示框架启动配置和编辑期校验入口。</summary>
    [CustomEditor(typeof(FrameworkStartupConfig))]
    internal sealed class FrameworkStartupConfigInspector : UnityEditor.Editor
    {
        #region 公共方法

        /// <summary>绘制启动配置、模块装配、资源与诊断设置。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("EasyFramework 启动配置", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.Space(4f);
            SerializedProperty startUI = serializedObject.FindProperty("_startUI");
            DrawSection("UI 启动");
            EditorGUILayout.PropertyField(startUI, new GUIContent("启用 UI"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_pageManifest"),
                new GUIContent("页面清单", "指定此启动配置可打开的托管页面。仅使用画布与事件系统时可留空。"));
            using (new EditorGUI.DisabledScope(!startUI.boolValue))
            {
                SerializedProperty renderMode = serializedObject.FindProperty("_renderMode");
                EditorGUILayout.PropertyField(renderMode, new GUIContent("渲染模式"));
                if ((UIRenderMode)renderMode.enumValueIndex == UIRenderMode.ScreenSpaceCamera)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_uiCamera"), new GUIContent("UI 相机"));
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("_createCameraWhenMissing"),
                        new GUIContent("缺少时自动创建相机"));
                }
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_createEventSystem"),
                    new GUIContent("自动创建 EventSystem"));

                DrawSection("相机与层级");
                DrawLayerField(serializedObject.FindProperty("_uiLayerName"));
                if ((UIRenderMode)renderMode.enumValueIndex == UIRenderMode.ScreenSpaceCamera)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_cameraDepth"), new GUIContent("相机深度"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_planeDistance"), new GUIContent("平面距离"));
                }

                DrawSection("屏幕适配");
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_referenceResolution"),
                    new GUIContent("参考分辨率"));
                EditorGUILayout.Slider(
                    serializedObject.FindProperty("_matchWidthOrHeight"),
                    0f,
                    1f,
                    new GUIContent("宽高匹配"));

                DrawSection("共享背景");
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_backgroundPrefab"),
                    new GUIContent("背景 Prefab"));
            }

            DrawSection("运行环境");
            SerializedProperty runtimeMode = serializedObject.FindProperty("_runtimeMode");
            EditorGUILayout.PropertyField(runtimeMode, new GUIContent("运行模式"));
            bool isOffline = runtimeMode.intValue == (int)RuntimeMode.Offline;
            DrawSection("模块装配");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_builtinModules"), new GUIContent("内置模块"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_isSceneModuleEnabled"),
                new GUIContent("场景模块", "由 SceneModule 统一切换、叠加加载和卸载构建场景。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_moduleInstallers"),
                new GUIContent("模块安装器", "装配项目模块。流程模块及首流程由项目安装器显式登记。"), true);
            DrawAssetSettings();
            DrawHotUpdate(isOffline);
            if (isOffline) DrawOfflineValidation();
            DrawDiagnostics();
            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region 私有逻辑

        private void DrawHotUpdate(bool isOffline)
        {
            DrawSection("热更启动");
            SerializedProperty hotUpdate = serializedObject.FindProperty("_hotUpdate");
            SerializedProperty channel = serializedObject.FindProperty("_hotUpdateChannel");
            EditorGUILayout.PropertyField(hotUpdate, new GUIContent("随包发行", "提供热更运行时适配器和本地备用发行。"));
            EditorGUILayout.PropertyField(channel, new GUIContent("发行渠道", "留空时固定启动随包发行。"));
            if (hotUpdate.objectReferenceValue == null && channel.objectReferenceValue == null) return;
            if (isOffline && channel.objectReferenceValue != null)
                EditorGUILayout.HelpBox(FrameworkStartupConfig.OFFLINE_CHANNEL_ERROR, MessageType.Error);
            else if (hotUpdate.objectReferenceValue == null)
                EditorGUILayout.HelpBox("发行渠道需要随包发行配置提供热更运行时适配器和备用发行。", MessageType.Error);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playerBaselineId"),
                new GUIContent("Player 基线 ID", "填写此 Player 归档对应的基线 ID。发行配置必须匹配此值。"));
            if (!GUILayout.Button("校验热更配置")) return;

            // 先提交本次 Inspector 修改，使编辑期和 Player 校验同一份配置。
            serializedObject.ApplyModifiedProperties();
            try
            {
                var startup = (FrameworkStartupConfig)target;
                startup.ValidateStartupMode();
                string result = FrameworkHotUpdateSetup.Validate(startup);
                EditorUtility.DisplayDialog("热更配置校验通过", result, "确定");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("热更配置校验失败", exception.Message, "确定");
            }
        }

        private void DrawOfflineValidation()
        {
            if (!GUILayout.Button("校验离线配置")) return;
            serializedObject.ApplyModifiedProperties();
            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
            bool isServer = BuildPipeline.GetBuildTargetGroup(buildTarget) == BuildTargetGroup.Standalone &&
                            EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
            try
            {
                string result = FrameworkOfflineBuildValidation.Validate((FrameworkStartupConfig)target, buildTarget, isServer);
                EditorUtility.DisplayDialog("离线配置校验通过", result, "确定");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("离线配置校验失败", exception.Message, "确定");
            }
        }

        private void DrawDiagnostics()
        {
            DrawSection("可选运行时诊断");
            SerializedProperty mode = serializedObject.FindProperty("_debugWindowMode");
            EditorGUILayout.PropertyField(mode, new GUIContent("OnGUI Debug", "在编辑器 Play Mode 和 Development Build 中启用。"));
        }

        private void DrawAssetSettings()
        {
            DrawSection("资源包与缓存");
            SerializedProperty packages = serializedObject.FindProperty("_assetPackages");
            EditorGUILayout.PropertyField(packages, new GUIContent("资源包"), true);
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < packages.arraySize; i++)
            {
                var package = packages.GetArrayElementAtIndex(i).objectReferenceValue as AssetPackageConfig;
                string alias = package == null ? null : package.Alias?.Trim();
                if (string.IsNullOrEmpty(alias) || alias.Contains(":") || !aliases.Add(alias))
                {
                    EditorGUILayout.HelpBox("每个资源包需要一个不同且不含冒号的别名。默认资源包的别名为 main。",
                        MessageType.Error);
                    break;
                }
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_residentAssets"),
                new GUIContent("启动常驻资源", "填写包别名:包内地址。首屏就绪后在后台加载。"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_startupAssetTimeout"),
                new GUIContent("常驻加载超时（秒）"));
            SerializedProperty overrideCache = serializedObject.FindProperty("_overrideAssetCache");
            EditorGUILayout.PropertyField(overrideCache, new GUIContent("自定义缓存预算"));
            if (overrideCache.boolValue)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_assetCache"), new GUIContent("缓存预算"), true);
        }

        private static void DrawSection(string title)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawLayerField(SerializedProperty layerName)
        {
            int current = LayerMask.NameToLayer(layerName.stringValue);
            if (current < 0) current = Mathf.Max(0, LayerMask.NameToLayer("UI"));
            int next = EditorGUILayout.LayerField(new GUIContent("UI Layer"), current);
            string selected = LayerMask.LayerToName(next);
            if (!string.IsNullOrEmpty(selected)) layerName.stringValue = selected;
        }

        #endregion
    }
}
