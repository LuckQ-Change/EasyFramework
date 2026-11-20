using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(FrameworkStartupConfig))]
    internal sealed class FrameworkStartupConfigInspector : UnityEditor.Editor
    {
        private SerializedProperty _startupProcedureTypeName;
        private MonoScript _startupProcedureScript;
        private string _validationError;

        private void OnEnable()
        {
            _startupProcedureTypeName = serializedObject.FindProperty("_startupProcedureTypeName");
            RefreshScript();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("EasyFramework 启动配置", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.Space(4f);
            DrawSection("启动流程");
            EditorGUI.BeginChangeCheck();
            MonoScript selected = (MonoScript)EditorGUILayout.ObjectField(
                "启动流程脚本",
                _startupProcedureScript,
                typeof(MonoScript),
                false);
            if (EditorGUI.EndChangeCheck()) ApplySelectedScript(selected);

            if (!string.IsNullOrEmpty(_validationError))
                EditorGUILayout.HelpBox(_validationError, MessageType.Error);
            else if (_startupProcedureScript == null)
                EditorGUILayout.HelpBox("未配置时不会自动注册或启动流程模块。", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Launcher 会自动启动该流程；Next 和 Change 遇到的新流程会按需注册。",
                    MessageType.Info);

            EditorGUILayout.Space();
            SerializedProperty startUI = serializedObject.FindProperty("_startUI");
            DrawSection("UI 启动");
            EditorGUILayout.PropertyField(startUI, new GUIContent("启用 UI"));
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_runtimeMode"), new GUIContent("运行模式"));
            DrawAssetSettings();
            DrawSection("全局 Loading");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_loading"), GUIContent.none, true);
            serializedObject.ApplyModifiedProperties();
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

        private void ApplySelectedScript(MonoScript script)
        {
            _startupProcedureScript = script;
            _validationError = null;
            if (script == null)
            {
                _startupProcedureTypeName.stringValue = string.Empty;
                return;
            }

            if (!TryValidateScript(script, out Type type, out string error))
            {
                _validationError = error;
                return;
            }

            _startupProcedureTypeName.stringValue =
                $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        private void RefreshScript()
        {
            _startupProcedureScript = null;
            _validationError = null;
            string typeName = _startupProcedureTypeName?.stringValue;
            if (string.IsNullOrWhiteSpace(typeName)) return;

            Type type = Type.GetType(typeName, false);
            if (type == null)
            {
                _validationError = $"无法加载已配置的启动流程类型：{typeName}";
                return;
            }

            string[] guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                {
                    _startupProcedureScript = script;
                    return;
                }
            }

            _validationError = $"找不到启动流程“{type.FullName}”对应的脚本资源。";
        }

        private static bool TryValidateScript(MonoScript script, out Type type, out string error)
        {
            type = script.GetClass();
            if (type == null)
            {
                error = "所选脚本尚未成功编译，或脚本中没有与文件同名的类型。";
                return false;
            }
            if (!typeof(ProcedureBase).IsAssignableFrom(type))
            {
                error = $"“{type.FullName}”必须继承 ProcedureBase。";
                return false;
            }
            if (type.IsAbstract)
            {
                error = $"“{type.FullName}”是抽象类，不能作为启动流程。";
                return false;
            }
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                error = $"“{type.FullName}”必须提供公开的无参构造函数。";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(script).Replace('\\', '/');
            if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "启动流程不能放在 Editor 目录中，否则 Player 构建无法加载。";
                return false;
            }

            error = null;
            return true;
        }
    }

    [CustomEditor(typeof(UIStateController))]
    internal sealed class UIStateControllerInspector : UnityEditor.Editor
    {
        private SerializedProperty _states;
        private SerializedProperty _selectedIndex;
        private SerializedProperty _allowUndefinedState;
        private SerializedProperty _onStateChanged;
        private ReorderableList _stateList;
        private string[] _previousStates;

        private void OnEnable()
        {
            _states = serializedObject.FindProperty("_states");
            _selectedIndex = serializedObject.FindProperty("_selectedIndex");
            _allowUndefinedState = serializedObject.FindProperty("_allowUndefinedState");
            _onStateChanged = serializedObject.FindProperty("_onStateChanged");
            _stateList = new ReorderableList(serializedObject, _states, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "状态列表"),
                drawElementCallback = DrawStateElement,
                onAddCallback = AddStateElement
            };
            CacheStates((UIStateController)target);
        }

        public override void OnInspectorGUI()
        {
            var controller = (UIStateController)target;
            serializedObject.Update();
            _stateList.DoLayoutList();
            var labels = BuildStateLabels();
            if (labels.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                int next = EditorGUILayout.Popup("当前状态", _selectedIndex.intValue, labels);
                if (EditorGUI.EndChangeCheck())
                    _selectedIndex.intValue = next;
            }
            EditorGUILayout.PropertyField(_allowUndefinedState, new GUIContent("允许未定义状态"));
            EditorGUILayout.PropertyField(_onStateChanged, new GUIContent("状态变化事件"));
            bool statesChanged = serializedObject.ApplyModifiedProperties();
            if (statesChanged)
            {
                controller.RelinkStates(_previousStates);
                CacheStates(controller);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("应用到子节点", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (string state in controller.States)
            {
                if (string.IsNullOrWhiteSpace(state)) continue;
                bool selected = controller.SelectedState == state;
                if (GUILayout.Toggle(selected, state, EditorStyles.miniButton) && !selected)
                    ApplyState(controller, state);
            }
            EditorGUILayout.EndHorizontal();
            if (!controller.IsPreviewing && GUILayout.Button("预览当前状态"))
                ApplyState(controller, controller.SelectedState);
            if (statesChanged)
                ApplyState(controller, controller.SelectedState);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("将当前外观记录到当前状态"))
            {
                Undo.RecordObjects(controller.GetComponentsInChildren<UIElement>(true), "Capture UI state");
                controller.CaptureCurrentToSelectedState();
                EditorUtility.SetDirty(controller);
            }
            if (GUILayout.Button("还原默认外观"))
            {
                Undo.RecordObjects(controller.GetComponentsInChildren<Component>(true), "Restore UI default");
                controller.RestoreSerializedAppearance();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "点状态名会立刻改子节点外观。可添加任意多个自定义状态；改名会保留已记录的变体。保存 Prefab 时写入默认外观，随后仍会回到当前状态。",
                MessageType.Info);
            if (GUILayout.Button("打开集中状态工作台"))
                UIStateWorkbench.Open(controller);
        }

        private void DrawStateElement(Rect rect, int index, bool active, bool focused)
        {
            if (index < 0 || index >= _states.arraySize) return;
            SerializedProperty element = _states.GetArrayElementAtIndex(index);
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(rect, element, GUIContent.none);
        }

        private void AddStateElement(ReorderableList list)
        {
            int index = _states.arraySize;
            _states.arraySize++;
            _states.GetArrayElementAtIndex(index).stringValue = UniqueStateName(index);
        }

        private string UniqueStateName(int ignoreIndex)
        {
            var used = new HashSet<string>();
            for (int i = 0; i < _states.arraySize; i++)
            {
                if (i == ignoreIndex) continue;
                used.Add(_states.GetArrayElementAtIndex(i).stringValue);
            }
            if (!used.Contains("State")) return "State";
            int suffix = 2;
            while (used.Contains("State" + suffix)) suffix++;
            return "State" + suffix;
        }

        private string[] BuildStateLabels()
        {
            var labels = new string[_states.arraySize];
            for (int i = 0; i < labels.Length; i++)
            {
                string value = _states.GetArrayElementAtIndex(i).stringValue;
                labels[i] = string.IsNullOrWhiteSpace(value) ? $"(State {i + 1})" : value;
            }
            return labels;
        }

        private void CacheStates(UIStateController controller)
        {
            if (controller == null)
            {
                _previousStates = Array.Empty<string>();
                return;
            }
            _previousStates = new string[controller.States.Count];
            for (int i = 0; i < _previousStates.Length; i++)
                _previousStates[i] = controller.States[i];
        }

        private static void ApplyState(UIStateController controller, string state)
        {
            if (controller == null || string.IsNullOrWhiteSpace(state)) return;
            Undo.RecordObjects(controller.GetComponentsInChildren<Component>(true), "Apply UI state");
            controller.SetState(state);
            SceneView.RepaintAll();
        }
    }

    [CustomEditor(typeof(UIElement))]
    internal sealed class UIElementInspector : UnityEditor.Editor
    {
        private SerializedProperty _controller;
        private SerializedProperty _captureDefaultOnAwake;
        private SerializedProperty _defaultValue;
        private SerializedProperty _variants;

        private void OnEnable()
        {
            _controller = serializedObject.FindProperty("_controller");
            _captureDefaultOnAwake = serializedObject.FindProperty("_captureDefaultOnAwake");
            _defaultValue = serializedObject.FindProperty("_defaultValue");
            _variants = serializedObject.FindProperty("_variants");
        }

        public override void OnInspectorGUI()
        {
            var element = (UIElement)target;
            element.SyncVariantsToController();
            serializedObject.Update();
            EditorGUILayout.PropertyField(_controller);
            EditorGUILayout.PropertyField(_captureDefaultOnAwake, new GUIContent("Awake 时重录默认外观"));
            EditorGUILayout.PropertyField(_defaultValue, true);
            if (GUILayout.Button("将当前组件属性记录为默认外观"))
            {
                Undo.RecordObject(element, "Capture UI defaults");
                element.CaptureDefault();
                EditorUtility.SetDirty(element);
                serializedObject.Update();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("状态变体", EditorStyles.boldLabel);
            if (element.Controller == null)
            {
                EditorGUILayout.HelpBox("指定 State Controller 后，会按状态列表自动生成变体。未勾选的属性沿用默认外观。", MessageType.Info);
                EditorGUILayout.PropertyField(_variants, true);
            }
            else
            {
                EditorGUILayout.HelpBox("每个状态只填写相对默认外观要改的属性。可先预览该状态，改节点，再点记录。", MessageType.Info);
                for (int i = 0; i < _variants.arraySize; i++)
                {
                    SerializedProperty variant = _variants.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.PropertyField(variant, true);
                    string state = variant.FindPropertyRelative("_state").stringValue;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("预览此状态"))
                    {
                        Undo.RecordObjects(element.Controller.GetComponentsInChildren<Component>(true), "Apply UI state");
                        element.Controller.SetState(state);
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button("将当前外观记录到此状态"))
                    {
                        Undo.RecordObject(element, "Capture UI variant");
                        element.CaptureVariant(state);
                        EditorUtility.SetDirty(element);
                        serializedObject.Update();
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                }
            }

            if (serializedObject.ApplyModifiedProperties() && element.Controller != null)
            {
                element.SyncVariantsToController();
                element.ApplyState(element.Controller.SelectedState);
            }
        }
    }
}
