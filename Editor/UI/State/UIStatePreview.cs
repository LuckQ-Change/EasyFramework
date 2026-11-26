using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    /// <summary>在独立的 Scene 视图阶段预览状态，并提供源节点到副本的映射。</summary>
    internal static class UIStatePreview
    {
        private static PreviewStage _stage;

        internal static bool Start(UIStateController controller, int stateIndex, out string error)
        {
            error = null;
            if (controller == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                error = "请在编辑模式选择有效的状态控制器。";
                return false;
            }

            if (IsOpenFor(controller))
                return _stage.TryApply(stateIndex, out error);

            Stop();
            if (_stage != null)
            {
                error = "请先退出当前 UIState 预览阶段。";
                return false;
            }

            var stage = ScriptableObject.CreateInstance<PreviewStage>();
            stage.Configure(controller, stateIndex);
            _stage = stage;
            StageUtility.GoToStage(stage, false);
            if (stage == null || StageUtility.GetCurrentStage() != stage)
            {
                error = stage.OpenError ?? "未能打开 UIState 预览阶段。";
                if (ReferenceEquals(_stage, stage)) _stage = null;
                return false;
            }

            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.quitting += Stop;
            GameObject controllerCopy = stage.GetClone(controller.gameObject);
            if (controllerCopy != null) Selection.activeGameObject = controllerCopy;
            SceneView.RepaintAll();
            return true;
        }

        internal static bool IsOpenFor(UIStateController controller) =>
            controller != null && _stage != null && _stage.Source == controller && StageUtility.GetCurrentStage() == _stage;

        internal static GameObject GetClone(GameObject source) =>
            source != null && _stage != null && StageUtility.GetCurrentStage() == _stage ? _stage.GetClone(source) : null;

        internal static bool IsPreviewObject(GameObject gameObject) =>
            gameObject != null && _stage != null && gameObject.scene == _stage.scene;

        internal static void Stop()
        {
            if (_stage != null && StageUtility.GetCurrentStage() == _stage)
                StageUtility.GoBackToPreviousStage();
        }

        private static void OnStageClosed(PreviewStage stage)
        {
            if (!ReferenceEquals(_stage, stage)) return;
            _stage = null;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.delayCall -= Stop;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.quitting -= Stop;
        }

        private static void OnUndoRedo()
        {
            // 等待 Unity 完成这次撤销后再切回原阶段。
            EditorApplication.delayCall -= Stop;
            EditorApplication.delayCall += Stop;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Stop();
        }

        /// <summary>承载临时画布和状态控制器副本的独立场景阶段。</summary>
        private sealed class PreviewStage : PreviewSceneStage
        {
            private const float CANVAS_SCALE = 0.01f;
            private readonly Dictionary<GameObject, GameObject> _copies = new();
            private UIStateController _controller;
            private UIStateController _cloneController;
            private RectTransform _canvas;
            private int _stateIndex;

            internal UIStateController Source => _controller;
            internal string OpenError { get; private set; }

            #region 生命周期

            internal void Configure(UIStateController controller, int stateIndex)
            {
                _controller = controller;
                _stateIndex = stateIndex;
            }

            protected override bool OnOpenStage()
            {
                if (!base.OnOpenStage()) return false;
                try
                {
                    BuildCanvas();
                    bool hasApplied = TryApply(_stateIndex, out string error);
                    OpenError = error;
                    return hasApplied;
                }
                catch (Exception exception)
                {
                    OpenError = exception.Message;
                    return false;
                }
            }

            protected override void OnFirstTimeOpenStageInSceneView(SceneView sceneView)
            {
                if (sceneView == null || _canvas == null) return;
                // 预览画布使用世界坐标，正视图以画布边界取景。
                sceneView.in2DMode = true;
                Vector3 center = _canvas.TransformPoint(_canvas.rect.center);
                Vector3 size = new(_canvas.rect.width * CANVAS_SCALE, _canvas.rect.height * CANVAS_SCALE, 0.1f);
                sceneView.Frame(new Bounds(center, size), false);
            }

            protected override void OnCloseStage()
            {
                _copies.Clear();
                OnStageClosed(this);
                base.OnCloseStage();
            }

            protected override GUIContent CreateHeaderContent() => new("UIState 预览");

            #endregion

            #region 私有逻辑

            internal GameObject GetClone(GameObject source) =>
                _copies.TryGetValue(source, out GameObject clone) ? clone : null;

            internal bool TryApply(int stateIndex, out string error)
            {
                error = null;
                if (_cloneController == null || !_cloneController.PreviewState(stateIndex))
                {
                    error = "无法在预览副本上应用选定状态。";
                    return false;
                }
                SceneView.RepaintAll();
                return true;
            }

            private void BuildCanvas()
            {
                if (_controller == null) throw new InvalidOperationException("预览源控制器已不存在。");
                Transform source = GetCanvasRoot(_controller.transform);
                Vector2 size = source is RectTransform sourceRect ? sourceRect.rect.size : Vector2.zero;
                if (size.x <= 0f || size.y <= 0f) size = new Vector2(1920f, 1080f);

                var previewRoot = EditorUtility.CreateGameObjectWithHideFlags("UIState 预览画布",
                    HideFlags.HideAndDontSave, typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(previewRoot, scene);
                previewRoot.hideFlags = HideFlags.None;
                previewRoot.SetActive(false);
                if (!previewRoot.TryGetComponent(out Canvas canvas) || previewRoot.transform is not RectTransform previewRect)
                    throw new InvalidOperationException("预览画布初始化失败。");
                canvas.renderMode = RenderMode.WorldSpace;
                _canvas = previewRect;
                _canvas.sizeDelta = size;
                _canvas.localScale = Vector3.one * CANVAS_SCALE;

                // 直接在隔离画布下实例化，使序列化的内部对象引用映射到副本。
                GameObject clone = Object.Instantiate(source.gameObject, _canvas, false);
                clone.name = source.name;
                if (clone.transform is RectTransform cloneRect)
                {
                    cloneRect.anchorMin = cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
                    cloneRect.anchoredPosition3D = Vector3.zero;
                    cloneRect.localRotation = Quaternion.identity;
                    cloneRect.localScale = Vector3.one;
                    cloneRect.sizeDelta = size;
                }

                // 副本支持编辑图形和布局，禁止不相关的编辑模式脚本运行。
                foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    Type type = behaviour.GetType();
                    if (behaviour is UIBehaviour && (type.Assembly == typeof(Graphic).Assembly ||
                        type.Namespace == "TMPro" || behaviour is UIImage)) continue;
                    if (Attribute.IsDefined(type, typeof(ExecuteAlways), true) ||
                        Attribute.IsDefined(type, typeof(ExecuteInEditMode), true)) behaviour.enabled = false;
                }

                MapHierarchy(source, clone.transform);
                GameObject controllerCopy = GetClone(_controller.gameObject);
                if (controllerCopy == null || !controllerCopy.TryGetComponent(out _cloneController))
                    throw new InvalidOperationException("预览副本中未找到状态控制器。");
                previewRoot.SetActive(true);
            }

            private static Transform GetCanvasRoot(Transform target)
            {
                PrefabStage stage = PrefabStageUtility.GetPrefabStage(target.gameObject);
                Transform prefabRoot = stage != null && stage.prefabContentsRoot != null
                    ? stage.prefabContentsRoot.transform : null;
                Canvas canvas = target.GetComponentInParent<Canvas>(true);
                if (canvas != null && canvas.rootCanvas != null)
                {
                    Transform canvasRoot = canvas.rootCanvas.transform;
                    return prefabRoot == null || canvasRoot.IsChildOf(prefabRoot) ? canvasRoot : prefabRoot;
                }
                while (target != prefabRoot && target.parent is RectTransform) target = target.parent;
                return target;
            }

            private void MapHierarchy(Transform source, Transform copy)
            {
                _copies.Add(source.gameObject, copy.gameObject);
                for (int i = 0; i < source.childCount; i++)
                    MapHierarchy(source.GetChild(i), copy.GetChild(i));
            }

            #endregion
        }
    }
}
