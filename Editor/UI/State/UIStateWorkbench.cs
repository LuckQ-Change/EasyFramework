using System;
using System.Collections.Generic;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace EasyFramework.Editor.UI
{
    /// <summary>集中维护运行时初态、节点字段和所选状态的覆盖。</summary>
    internal sealed class UIStateWorkbench : EditorWindow
    {
        private const string STYLESHEET_GUID = "e14b985346614651a8467ae596ed781a";
        private static readonly (UIStateProperty Property, string Label)[] _fields =
        {
            (UIStateProperty.Active, "显示"),
            (UIStateProperty.Interactable, "交互"),
            (UIStateProperty.Color, "颜色"),
            (UIStateProperty.Alpha, "透明度"),
            (UIStateProperty.Sprite, "精灵"),
            (UIStateProperty.Text, "文本"),
            (UIStateProperty.Value, "数值"),
            (UIStateProperty.SpriteIndex, "图集索引"),
        };

        [SerializeField] private UIStateController _controller;
        private readonly List<NodeRow> _nodes = new();
        private readonly List<NodeRow> _filteredNodes = new();
        private readonly HashSet<GameObject> _selectedNodes = new();
        private readonly Dictionary<UIStateProperty, UIStateVariant> _batchValues = new();
        private string _stateId;
        private int _stateIndex;
        private UIStateProperty _batchFields = UIStateProperty.Active;
        private string _search = string.Empty;
        private string _message;
        private bool _hasError;
        private bool _isOverridesOnly;
        private bool _isRefreshQueued;
        private ListView _nodeList;
        private ScrollView _mainScroll;
        private Vector2 _mainScrollOffset;
        private VisualElement _details;

        /// <summary>节点列表中的路径与已有控制数据。</summary>
        private sealed class NodeRow
        {
            internal GameObject Target { get; }
            internal string Path { get; }
            internal UIStateTargetBinding Binding { get; }

            internal NodeRow(GameObject target, string path, UIStateTargetBinding binding)
            {
                Target = target;
                Path = path;
                Binding = binding;
            }
        }

        #region 生命周期

        [MenuItem("Tools/EasyFramework/UI/State Workbench", false, 20)]
        private static void OpenMenu()
        {
            GameObject selected = Selection.activeGameObject;
            if (UIStatePreview.IsPreviewObject(selected))
            {
                GetWindow<UIStateWorkbench>("UI 状态工作台").Show();
                return;
            }
            Open(selected == null ? null : selected.GetComponentInParent<UIStateController>(true));
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshAfterUndo;
            EditorApplication.hierarchyChanged += QueueRefresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshAfterUndo;
            EditorApplication.hierarchyChanged -= QueueRefresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (_nodeList != null) _nodeList.selectionChanged -= OnNodeSelectionChanged;
            if (UIStatePreview.IsOpenFor(_controller)) UIStatePreview.Stop();
        }

        /// <summary>加载工作台样式并初始化编辑窗口。</summary>
        public void CreateGUI()
        {
            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(STYLESHEET_GUID));
            if (style != null) rootVisualElement.styleSheets.Add(style);
            Rebuild();
        }

        private void OnPlayModeChanged(PlayModeStateChange state) => QueueRefresh();

        private void RefreshAfterUndo()
        {
            _batchValues.Clear();
            QueueRefresh();
        }

        #endregion

        #region 公共方法

        /// <summary>打开工作台并绑定指定控制器。</summary>
        /// <param name="controller">要编辑的控制器，可为空。</param>
        public static void Open(UIStateController controller)
        {
            var window = GetWindow<UIStateWorkbench>("UI 状态工作台");
            window.minSize = new Vector2(760f, 460f);
            window.SetController(controller);
            window.Show();
        }

        #endregion

        #region 私有逻辑

        private bool CanEdit => _controller != null &&
                                !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(_controller) &&
                                !UIStatePreview.IsPreviewObject(_controller.gameObject);

        private void SetController(UIStateController controller)
        {
            if (controller != null && UIStatePreview.IsPreviewObject(controller.gameObject))
            {
                ShowMessage("预览副本可在 Scene 视图编辑，工作台仍绑定源控制器。", false);
                return;
            }
            if (_controller != controller)
            {
                if (UIStatePreview.IsOpenFor(_controller)) UIStatePreview.Stop();
                _controller = controller;
                _stateId = null;
                _stateIndex = controller == null ? 0 : controller.InitialStateIndex;
                _selectedNodes.Clear();
                _batchValues.Clear();
                _search = string.Empty;
                _message = null;
                _mainScroll = null;
                _mainScrollOffset = Vector2.zero;
            }
            QueueRefresh();
        }

        private void QueueRefresh()
        {
            if (_isRefreshQueued) return;
            _isRefreshQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _isRefreshQueued = false;
                Rebuild();
            });
        }

        private void Rebuild()
        {
            VisualElement root = rootVisualElement;
            if (_mainScroll != null) _mainScrollOffset = _mainScroll.scrollOffset;
            if (_nodeList != null) _nodeList.selectionChanged -= OnNodeSelectionChanged;
            _nodeList = null;
            _mainScroll = null;
            _details = null;
            root.Clear();
            root.AddToClassList("state-workbench");
            root.EnableInClassList("state-workbench--dark", EditorGUIUtility.isProSkin);

            var header = Region("state-header");
            var controllerField = new ObjectField("控制器")
            {
                objectType = typeof(UIStateController),
                allowSceneObjects = true,
                value = _controller,
            };
            controllerField.AddToClassList("state-controller");
            controllerField.RegisterValueChangedCallback(change => SetController(change.newValue as UIStateController));
            header.Add(controllerField);
            root.Add(header);
            if (!string.IsNullOrEmpty(_message))
                root.Add(new HelpBox(_message, _hasError ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info));
            if (_controller == null)
            {
                root.Add(new HelpBox("选中场景或 Prefab Mode 中的 UIStateController。", HelpBoxMessageType.Info));
                return;
            }

            if (EditorUtility.IsPersistent(_controller))
            {
                string path = AssetDatabase.GetAssetPath(_controller);
                header.Add(Command("打开 Prefab", () => PrefabStageUtility.OpenPrefab(path)));
                root.Add(new HelpBox("在 Prefab Mode 中选择控制器后编辑。", HelpBoxMessageType.Info));
                return;
            }

            ResolveStateSelection();
            bool isPlaying = EditorApplication.isPlaying;
            if (_controller.States.Count != 0)
            {
                if (isPlaying)
                    header.Add(Command("应用到运行实例", () => _controller.SetState(_stateIndex)));
                else if (UIStatePreview.IsOpenFor(_controller))
                    header.Add(Command("结束预览", () => { UIStatePreview.Stop(); QueueRefresh(); }));
                else
                    header.Add(Command("打开隔离预览", () =>
                    {
                        bool hasOpened = UIStatePreview.Start(_controller, _stateIndex, out string error);
                        ShowMessage(error, !hasOpened);
                    }));
            }

            var layout = Region("state-layout");
            layout.Add(BuildStates());
            root.Add(layout);

            var panel = new ScrollView(ScrollViewMode.Vertical);
            _mainScroll = panel;
            panel.AddToClassList("state-main");
            layout.Add(panel);
            _details = Region("state-details");
            BuildNodeList(panel);
            panel.Add(_details);
            IReadOnlyList<string> conflicts = UIStateTimelineConflicts.Find(_controller);
            if (conflicts.Count > 0)
                panel.Add(new HelpBox(string.Join("\n", conflicts), HelpBoxMessageType.Warning));
            FilterNodes();
            Vector2 offset = _mainScrollOffset;
            panel.schedule.Execute(() => panel.scrollOffset = offset);
        }

        private VisualElement BuildStates()
        {
            var sidebar = Region("state-sidebar");
            var header = Region("state-toolbar");
            header.Add(Heading("状态"));
            header.Add(Command("添加", () => Mutate("添加 UI 状态", () =>
            {
                if (!_controller.AddState()) return false;
                SelectState(_controller.States.Count - 1);
                return true;
            }), CanEdit));
            sidebar.Add(header);

            var stateList = new ScrollView(ScrollViewMode.Vertical);
            stateList.AddToClassList("state-list");
            for (int index = 0; index < _controller.States.Count; index++)
            {
                int captured = index;
                string name = _controller.States[index];
                bool isInitial = index == _controller.InitialStateIndex;
                var button = Command(isInitial ? name + "  ·  初态" : name, () =>
                {
                    if (captured == _stateIndex) return;
                    if (UIStatePreview.IsOpenFor(_controller)) UIStatePreview.Stop();
                    SelectState(captured);
                    QueueRefresh();
                });
                button.AddToClassList("state-selector");
                button.EnableInClassList("state-selector--selected", index == _stateIndex);
                stateList.Add(button);
            }
            sidebar.Add(stateList);

            if (_controller.States.Count == 0 || !CanEdit) return sidebar;
            var initial = new DropdownField("运行时初态", _controller.States.ToList(), _controller.InitialStateIndex);
            initial.AddToClassList("state-initial");
            initial.RegisterValueChangedCallback(change =>
                Mutate("设置 UI 初态", () => _controller.SetInitialState(IndexOfStateName(change.newValue))));
            sidebar.Add(initial);
            var rename = new TextField("所选状态") { value = _controller.States[_stateIndex], isDelayed = true };
            rename.AddToClassList("state-initial");
            rename.RegisterValueChangedCallback(change => Mutate("更改 UI 状态名称", () =>
            {
                if (_controller.RenameState(_stateIndex, change.newValue)) return true;
                ShowMessage("状态名称不能为空，也不能与其他状态重复。", true);
                return false;
            }));
            sidebar.Add(rename);
            var actions = Region("state-toolbar");
            actions.Add(Command("上移", () => Mutate("上移 UI 状态", () => _controller.MoveState(_stateIndex, _stateIndex - 1)),
                _stateIndex > 0));
            actions.Add(Command("下移", () => Mutate("下移 UI 状态", () => _controller.MoveState(_stateIndex, _stateIndex + 1)),
                _stateIndex < _controller.States.Count - 1));
            actions.Add(Command("删除", () => Mutate("删除 UI 状态", () => _controller.RemoveState(_stateIndex)),
                _controller.States.Count > 1));
            sidebar.Add(actions);
            return sidebar;
        }

        private void BuildNodeList(VisualElement panel)
        {
            _nodes.Clear();
            _filteredNodes.Clear();
            var bindings = new Dictionary<GameObject, UIStateTargetBinding>();
            foreach (var binding in _controller.TargetBindings)
            {
                if (binding != null && binding.Target != null) bindings[binding.Target] = binding;
            }
            foreach (Transform node in _controller.GetComponentsInChildren<Transform>(true))
            {
                GameObject target = node.gameObject;
                if (!_controller.IsTargetInScope(target)) continue;
                bindings.TryGetValue(target, out UIStateTargetBinding binding);
                string path = target == _controller.gameObject ? target.name : AnimationUtility.CalculateTransformPath(node, _controller.transform);
                _nodes.Add(new NodeRow(target, path, binding));
            }

            var toolbar = Region("state-toolbar");
            toolbar.Add(Heading("节点"));
            var search = new ToolbarSearchField { value = _search };
            search.AddToClassList("state-search");
            search.tooltip = "按层级路径筛选节点";
            search.RegisterValueChangedCallback(change => { _search = change.newValue; FilterNodes(); });
            toolbar.Add(search);
            var overrides = new Toggle("仅看覆盖") { value = _isOverridesOnly };
            overrides.RegisterValueChangedCallback(change => { _isOverridesOnly = change.newValue; FilterNodes(); });
            toolbar.Add(overrides);
            toolbar.Add(Command("载入层级选中项", LoadHierarchySelection));
            panel.Add(toolbar);

            _nodeList = new ListView
            {
                itemsSource = _filteredNodes,
                selectionType = SelectionType.Multiple,
                fixedItemHeight = 24f,
                makeItem = () =>
                {
                    var row = Region("state-node");
                    row.Add(new Label { name = "path" });
                    row.Add(new Label { name = "usage" });
                    return row;
                },
                bindItem = (element, index) =>
                {
                    NodeRow row = _filteredNodes[index];
                    element.Q<Label>("path").text = row.Path;
                    element.Q<Label>("path").tooltip = row.Path;
                    var value = GetOverride(row);
                    element.Q<Label>("usage").text = value != null && value.Properties != UIStateProperty.None
                        ? "有覆盖" : row.Binding != null ? "已控制" : string.Empty;
                },
            };
            _nodeList.AddToClassList("state-nodes");
            _nodeList.selectionChanged += OnNodeSelectionChanged;
            panel.Add(_nodeList);
        }

        private void OnNodeSelectionChanged(IEnumerable<object> items)
        {
            _selectedNodes.Clear();
            foreach (object item in items)
                if (item is NodeRow row) _selectedNodes.Add(row.Target);
            BuildDetails();
        }

        private void FilterNodes()
        {
            _filteredNodes.Clear();
            foreach (NodeRow row in _nodes)
            {
                UIStateVariant value = GetOverride(row);
                bool hasOverride = value != null && value.Properties != UIStateProperty.None;
                if ((!_isOverridesOnly || hasOverride) &&
                    row.Path.IndexOf(_search ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filteredNodes.Add(row);
            }
            _selectedNodes.IntersectWith(_filteredNodes.Select(row => row.Target));
            // 筛选会改变行索引，按节点恢复选择，避免使用 ListView 重建前的索引。
            _nodeList.selectionChanged -= OnNodeSelectionChanged;
            try
            {
                _nodeList.Rebuild();
                SyncListSelection();
            }
            finally { _nodeList.selectionChanged += OnNodeSelectionChanged; }
        }

        private void LoadHierarchySelection()
        {
            _selectedNodes.Clear();
            var hierarchySelection = new HashSet<GameObject>(Selection.gameObjects);
            bool isPreviewing = UIStatePreview.IsOpenFor(_controller);
            foreach (NodeRow row in _filteredNodes)
            {
                if (hierarchySelection.Contains(row.Target) ||
                    (isPreviewing && hierarchySelection.Contains(UIStatePreview.GetClone(row.Target))))
                    _selectedNodes.Add(row.Target);
            }
            SyncListSelection();
        }

        private void SyncListSelection()
        {
            var selection = new List<int>();
            for (int index = 0; index < _filteredNodes.Count; index++)
                if (_selectedNodes.Contains(_filteredNodes[index].Target)) selection.Add(index);
            _nodeList.SetSelectionWithoutNotify(selection);
            BuildDetails();
        }

        private void BuildDetails()
        {
            if (_details == null) return;
            _details.Clear();
            List<NodeRow> selected = _filteredNodes.Where(row => _selectedNodes.Contains(row.Target)).ToList();
            if (selected.Count == 0)
            {
                _details.Add(new HelpBox("在节点列表中使用 Ctrl 或 Shift 多选，再选择要编辑的字段。", HelpBoxMessageType.Info));
                return;
            }

            if (CanEdit) DrawBatchEditor(selected);
            foreach (NodeRow row in selected)
                if (row.Binding != null) DrawNodeFields(row);
        }

        private void DrawBatchEditor(List<NodeRow> selected)
        {
            UIStateProperty controlled = UIStateProperty.None;
            UIStateProperty addable = UIStateProperty.None;
            foreach (NodeRow row in selected)
            {
                UIStateProperty bound = row.Binding?.Properties ?? UIStateProperty.None;
                controlled |= bound;
                addable |= UIStateController.CaptureTarget(row.Target).Properties & ~bound;
            }
            var card = Region("state-card");
            card.Add(Heading($"批量编辑 · {selected.Count} 个节点"));
            var fieldChoices = Region("state-field-choices");
            foreach (var option in _fields)
            {
                UIStateProperty property = option.Property;
                var toggle = new Toggle(option.Label) { value = (_batchFields & property) != 0 };
                toggle.SetEnabled(((controlled | addable) & property) != 0);
                toggle.RegisterValueChangedCallback(change =>
                {
                    _batchFields = change.newValue ? _batchFields | property : _batchFields & ~property;
                    rootVisualElement.schedule.Execute(BuildDetails);
                });
                fieldChoices.Add(toggle);
            }
            card.Add(fieldChoices);

            var values = Region("state-batch-values");
            foreach (var option in _fields)
            {
                UIStateProperty property = option.Property;
                if ((_batchFields & controlled & property) == 0) continue;
                if (!_batchValues.TryGetValue(property, out UIStateVariant value))
                {
                    NodeRow source = selected.First(row => row.Binding != null && (row.Binding.Properties & property) != 0);
                    value = UIStateVariantValues.Copy(EffectiveValue(source, property), property);
                    _batchValues[property] = value;
                }
                values.Add(CreateValueField(option.Label, property, value, entered => _batchValues[property] = entered));
            }
            card.Add(values);

            bool hasAddableFields = (_batchFields & addable) != 0;
            bool hasControlledFields = (_batchFields & controlled) != 0;
            var buttons = Region("state-toolbar");
            buttons.Add(Command("纳入控制", () => Mutate("批量加入 UIState 字段", () =>
                ApplyToNodes(selected, row => _controller.TryAddTarget(row.Target, _batchFields))), hasAddableFields));
            buttons.Add(Command("应用覆盖", () => Mutate("批量设置 UIState 覆盖", () =>
                ApplyToNodes(selected, ApplyBatchValues)), hasControlledFields));
            buttons.Add(Command("恢复基准", () => Mutate("批量清除 UIState 覆盖", () =>
                ApplyToNodes(selected, row => _controller.ClearOverride(row.Target, _stateIndex, _batchFields))), hasControlledFields));
            buttons.Add(Command("移除字段", () => Mutate("批量移除 UIState 字段", () =>
                ApplyToNodes(selected, row => _controller.RemoveTargetFields(row.Target, _batchFields))), hasControlledFields));
            if (UIStatePreview.IsOpenFor(_controller))
                buttons.Add(Command("记录预览覆盖", () => RecordPreview(selected), hasControlledFields));
            card.Add(buttons);
            _details.Add(card);
        }

        private void DrawNodeFields(NodeRow row)
        {
            var card = Region("state-card");
            card.Add(Heading(row.Path));
            UIStateVariant current = GetOverride(row);
            foreach (var option in _fields)
            {
                UIStateProperty property = option.Property;
                if ((row.Binding.Properties & property) == 0) continue;
                bool hasOverride = current != null && (current.Properties & property) != 0;
                var line = Region("state-property");
                line.EnableInClassList("state-property--override", hasOverride);
                var value = CreateValueField(option.Label, property, EffectiveValue(row, property), entered =>
                    Mutate("编辑 UIState 属性覆盖", () => _controller.SetOverride(row.Target, _stateIndex, entered)));
                value.SetEnabled(CanEdit);
                line.Add(value);
                var baseline = new Label("基准 " + FormatField(row.Binding.Baseline, property));
                baseline.AddToClassList("state-baseline");
                line.Add(baseline);
                if (CanEdit)
                    line.Add(Command("恢复基准", () => Mutate("清除 UIState 属性覆盖", () =>
                        _controller.ClearOverride(row.Target, _stateIndex, property)), hasOverride));
                card.Add(line);
            }
            _details.Add(card);
        }

        private bool ApplyBatchValues(NodeRow row)
        {
            if (row.Binding == null) return false;
            bool hasChanged = false;
            foreach (var option in _fields)
            {
                UIStateProperty property = option.Property;
                if ((_batchFields & row.Binding.Properties & property) != 0 &&
                    _batchValues.TryGetValue(property, out UIStateVariant value))
                    hasChanged |= _controller.SetOverride(row.Target, _stateIndex, value);
            }
            return hasChanged;
        }

        private void RecordPreview(List<NodeRow> selected)
        {
            var changes = new List<(GameObject Target, UIStateVariant Value)>();
            foreach (NodeRow row in selected)
            {
                if (row.Binding == null) continue;
                GameObject clone = UIStatePreview.GetClone(row.Target);
                if (clone == null) continue;
                UIStateVariant captured = UIStateController.CaptureTarget(clone);
                if (captured == null) continue;
                foreach (var option in _fields)
                {
                    UIStateProperty property = option.Property;
                    if ((_batchFields & row.Binding.Properties & captured.Properties & property) == 0) continue;
                    UIStateVariant updated = UIStateVariantValues.Copy(EffectiveValue(row, property), property);
                    if (UIStateVariantValues.Merge(updated, captured, property)) changes.Add((row.Target, updated));
                }
            }
            if (changes.Count == 0)
            {
                ShowMessage("所选预览字段没有变化。", false);
                return;
            }
            Mutate("记录 UIState 预览覆盖", () =>
            {
                bool hasChanged = false;
                foreach (var change in changes)
                    hasChanged |= _controller.SetOverride(change.Target, _stateIndex, change.Value);
                return hasChanged;
            });
        }

        private void Mutate(string name, Func<bool> change)
        {
            if (!CanEdit) return;
            _message = null;
            if (UIStatePreview.IsOpenFor(_controller)) UIStatePreview.Stop();
            if (_controller == null) return;

            // 所有字段写回同一个控制器。一次命令只登记一份 Undo 快照。
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            Undo.RecordObject(_controller, name);
            if (change())
            {
                EditorUtility.SetDirty(_controller);
                if (PrefabUtility.IsPartOfPrefabInstance(_controller))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(_controller);
                if (_controller.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(_controller.gameObject.scene);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            QueueRefresh();
        }

        private void ShowMessage(string message, bool hasError)
        {
            _message = message;
            _hasError = hasError;
            QueueRefresh();
        }

        private void ResolveStateSelection()
        {
            int index = _controller.IndexOfStateId(_stateId);
            if (index < 0) index = _controller.InitialStateIndex;
            SelectState(index);
        }

        private void SelectState(int index)
        {
            if (index != _stateIndex) _batchValues.Clear();
            _stateIndex = index;
            _stateId = index >= 0 && index < _controller.States.Count ? _controller.GetStateId(index) : null;
        }

        private int IndexOfStateName(string name)
        {
            for (int index = 0; index < _controller.States.Count; index++)
                if (_controller.States[index] == name) return index;
            return -1;
        }

        private UIStateVariant GetOverride(NodeRow row) =>
            row.Binding == null ? null : _controller.GetOverride(row.Binding, _stateIndex);

        private UIStateVariant EffectiveValue(NodeRow row, UIStateProperty property)
        {
            UIStateVariant value = GetOverride(row);
            return value != null && (value.Properties & property) != 0 ? value : row.Binding.Baseline;
        }

        private static VisualElement CreateValueField(string label, UIStateProperty property, UIStateVariant value,
            Action<UIStateVariant> apply)
        {
            UIStateVariant pending = UIStateVariantValues.Copy(value, property);
            VisualElement field = property switch
            {
                UIStateProperty.Active => BindField(new Toggle(label), value.Active, pending.SetActive),
                UIStateProperty.Interactable => BindField(new Toggle(label), value.Interactable, pending.SetInteractable),
                UIStateProperty.Color => BindField(new ColorField(label), value.Color, pending.SetColor),
                UIStateProperty.Alpha => BindField(new FloatField(label) { isDelayed = true }, value.Alpha,
                    number => pending.SetAlpha(Mathf.Clamp01(number))),
                UIStateProperty.Sprite => BindField<UnityEngine.Object>(new ObjectField(label)
                {
                    objectType = typeof(Sprite),
                    allowSceneObjects = false,
                }, value.Sprite, sprite => pending.SetSprite(sprite as Sprite)),
                UIStateProperty.Text => BindField(new TextField(label) { isDelayed = true }, value.Text ?? string.Empty, pending.SetText),
                UIStateProperty.Value => BindField(new FloatField(label) { isDelayed = true }, value.Value, pending.SetValue),
                UIStateProperty.SpriteIndex => BindField(new IntegerField(label) { isDelayed = true }, value.SpriteIndex, pending.SetSpriteIndex),
                _ => new Label(label),
            };
            field.AddToClassList("state-value");
            return field;

            VisualElement BindField<T>(BaseField<T> input, T initial, Action<T> assign)
            {
                input.SetValueWithoutNotify(initial);
                input.RegisterValueChangedCallback(change => { assign(change.newValue); apply(pending); });
                return input;
            }
        }

        private static string FormatField(UIStateVariant value, UIStateProperty property) => property switch
        {
            UIStateProperty.Active => value.Active ? "显示" : "隐藏",
            UIStateProperty.Interactable => value.Interactable ? "可交互" : "禁用",
            UIStateProperty.Color => "#" + ColorUtility.ToHtmlStringRGBA(value.Color),
            UIStateProperty.Alpha => value.Alpha.ToString("0.###"),
            UIStateProperty.Sprite => value.Sprite == null ? "空" : value.Sprite.name,
            UIStateProperty.Text => value.Text ?? string.Empty,
            UIStateProperty.Value => value.Value.ToString("0.###"),
            UIStateProperty.SpriteIndex => value.SpriteIndex.ToString(),
            _ => string.Empty,
        };

        private static bool ApplyToNodes(IEnumerable<NodeRow> nodes, Func<NodeRow, bool> apply)
        {
            bool hasChanged = false;
            foreach (NodeRow row in nodes) hasChanged |= apply(row);
            return hasChanged;
        }

        private static VisualElement Region(string className)
        {
            var region = new VisualElement();
            region.AddToClassList(className);
            return region;
        }

        private static Label Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("state-heading");
            return label;
        }

        private static Button Command(string label, Action action, bool isEnabled = true)
        {
            var button = new Button(action) { text = label };
            button.SetEnabled(isEnabled);
            return button;
        }

        #endregion
    }
}
