using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EasyFramework.UI
{
    /// <summary>将运行时状态名变化作为 UnityEvent 暴露给场景。</summary>
    [Serializable]
    public sealed class UIStateChangedEvent : UnityEvent<string> { }

    /// <summary>保存初始状态、稳定状态标识和目标覆盖，并在运行时应用最终值。</summary>
    [AddComponentMenu("EasyFramework/UI/State Controller")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class UIStateController : MonoBehaviour
    {
        private const string DEFAULT_STATE_ID = "normal";

        [SerializeField] private List<string> _states = new List<string> { "Normal", "Disabled" };
        [SerializeField] private UIStateChangedEvent _onStateChanged = new UIStateChangedEvent();
        [SerializeField] private List<string> _stateIds = new List<string> { DEFAULT_STATE_ID, "disabled" };
        [SerializeField] private string _initialStateId = DEFAULT_STATE_ID;
        [SerializeField] private List<UIStateTargetBinding> _targetBindings = new List<UIStateTargetBinding>();

        private string _runtimeState;
        private string _previewState;
        private Dictionary<GameObject, UIStateVariant> _previewValues;
        private int _refreshVersion;

        /// <summary>状态变更后派发运行时状态名称。</summary>
        public event Action<string> StateChanged;

        /// <summary>按工作台顺序排列的状态名称。</summary>
        public IReadOnlyList<string> States => _states;

        /// <summary>当前是否应用了临时编辑器预览。</summary>
        public bool IsPreviewing => _previewValues != null;

        /// <summary>控制器中的目标、基准和覆盖。</summary>
        public IReadOnlyList<UIStateTargetBinding> TargetBindings => _targetBindings;

        /// <summary>运行时启动时使用的状态位置。</summary>
        public int InitialStateIndex
        {
            get
            {
                if (_states.Count == 0) return -1;
                int index = IndexOfStateId(_initialStateId);
                return index >= 0 ? index : 0;
            }
        }

        /// <summary>运行时启动时使用的状态名称。</summary>
        public string InitialState => GetStateAt(InitialStateIndex);

        /// <summary>当前运行时或预览的状态名称。</summary>
        public string SelectedState => _previewState ?? _runtimeState ?? InitialState;

        /// <summary>当前状态在列表中的位置。</summary>
        public int SelectedIndex
        {
            get
            {
                int index = IndexOfState(SelectedState);
                return index >= 0 ? index : InitialStateIndex;
            }
        }

        /// <summary>运行时状态变更的序列化事件。</summary>
        public UIStateChangedEvent OnStateChanged => _onStateChanged;

        #region 生命周期

        private void Awake()
        {
            Normalize();
            _runtimeState = InitialState;
            Refresh();
        }

        private void OnValidate() => Normalize();

        #endregion

        #region 公共方法

        /// <summary>取得当前排序位置对应的稳定状态 ID。</summary>
        /// <param name="index">状态位置。</param>
        /// <returns>有效位置的标识，无效位置返回空。</returns>
        public string GetStateId(int index)
        {
            return index >= 0 && index < _stateIds.Count ? _stateIds[index] : null;
        }

        /// <summary>按状态 ID 查询当前排序。</summary>
        /// <param name="stateId">状态标识。</param>
        /// <returns>状态位置，缺失时返回 -1。</returns>
        public int IndexOfStateId(string stateId)
        {
            if (string.IsNullOrEmpty(stateId)) return -1;
            return _stateIds.IndexOf(stateId);
        }

        /// <summary>设置后续运行时的初始状态。</summary>
        /// <param name="index">状态位置。</param>
        /// <returns>初态发生改变时返回 true。</returns>
        public bool SetInitialState(int index)
        {
            if (index < 0 || index >= _states.Count || index == InitialStateIndex) return false;
            _initialStateId = GetStateId(index);
            return true;
        }

        /// <summary>重命名状态并保留稳定标识。</summary>
        /// <param name="index">状态位置。</param>
        /// <param name="newName">新的唯一名称。</param>
        /// <returns>名称有效时返回 true。</returns>
        public bool RenameState(int index, string newName)
        {
            if (index < 0 || index >= _states.Count || string.IsNullOrWhiteSpace(newName)) return false;
            newName = newName.Trim();
            string previous = _states[index];
            if (previous == newName) return true;
            if (_states.Contains(newName)) return false;

            _states[index] = newName;
            if (_runtimeState == previous) _runtimeState = newName;
            if (_previewState == previous) _previewState = newName;
            return true;
        }

        /// <summary>追加唯一状态名称。</summary>
        /// <param name="state">状态名称，留空时生成唯一名称。</param>
        /// <returns>追加成功时返回 true。</returns>
        public bool AddState(string state = null)
        {
            Normalize();
            string name = string.IsNullOrWhiteSpace(state) ? CreateUniqueStateName() : state.Trim();
            if (_states.Contains(name)) return false;
            _states.Add(name);
            _stateIds.Add(Guid.NewGuid().ToString("N"));
            return true;
        }

        /// <summary>调整状态顺序，沿用状态标识及当前初态。</summary>
        /// <param name="from">当前状态位置。</param>
        /// <param name="to">调整后的状态位置。</param>
        /// <returns>状态顺序发生改变时返回 true。</returns>
        public bool MoveState(int from, int to)
        {
            if (from < 0 || to < 0 || from >= _states.Count || to >= _states.Count || from == to) return false;
            string state = _states[from];
            _states.RemoveAt(from);
            _states.Insert(to, state);
            string id = _stateIds[from];
            _stateIds.RemoveAt(from);
            _stateIds.Insert(to, id);
            return true;
        }

        /// <summary>删除状态及其覆盖，最后一个状态始终保留。</summary>
        /// <param name="index">待删除的位置。</param>
        /// <returns>状态被删除时返回 true。</returns>
        public bool RemoveState(int index)
        {
            if (index < 0 || index >= _states.Count || _states.Count == 1) return false;
            string removed = _states[index];
            string id = _stateIds[index];
            foreach (UIStateTargetBinding binding in _targetBindings)
                binding?.ClearOverride(id, UIStateProperty.All);
            _stateIds.RemoveAt(index);
            _states.RemoveAt(index);
            if (_initialStateId == id) _initialStateId = _stateIds[Mathf.Min(index, _states.Count - 1)];
            if (_runtimeState == removed) _runtimeState = InitialState;
            if (_previewState == removed) _previewState = InitialState;
            return true;
        }

        /// <summary>切换运行时状态，编辑模式下以无事件的临时预览显示。</summary>
        /// <param name="state">目标状态名称。</param>
        /// <returns>接受该状态时返回 true。</returns>
        public bool SetState(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            if (!Application.isPlaying) return Preview(state);
            if (IndexOfState(state) < 0)
            {
                Log.Warn($"[UI] {name}: state '{state}' is not defined.");
                return false;
            }

            string previous = SelectedState;
            _runtimeState = state;
            Refresh();
            if (previous == state || _runtimeState != state) return true;
            StateChanged?.Invoke(state);
            _onStateChanged?.Invoke(state);
            return true;
        }

        /// <summary>按排序位置切换状态。</summary>
        /// <param name="index">目标状态位置。</param>
        /// <returns>接受该状态时返回 true。</returns>
        public bool SetState(int index) => index >= 0 && index < _states.Count && SetState(_states[index]);

        /// <summary>临时显示指定状态并保留可还原的原始外观。</summary>
        /// <param name="state">状态名称。</param>
        /// <returns>进入预览时返回 true。</returns>
        public bool Preview(string state)
        {
            if (Application.isPlaying || string.IsNullOrWhiteSpace(state) || IndexOfState(state) < 0) return false;

            _previewValues ??= new Dictionary<GameObject, UIStateVariant>();
            _previewState = state;
            Refresh();
            return true;
        }

        /// <summary>按排序位置临时预览状态。</summary>
        /// <param name="index">状态位置。</param>
        /// <returns>进入预览时返回 true。</returns>
        public bool PreviewState(int index) => index >= 0 && index < _states.Count && Preview(_states[index]);

        /// <summary>停止预览并恢复进入预览前的外观。</summary>
        public void ClearPreview()
        {
            if (_previewValues == null) return;
            var values = _previewValues;
            _previewValues = null;
            _previewState = null;
            foreach (var entry in values)
                if (entry.Key != null) new UIStateTargetAccess(entry.Key).Restore(entry.Value);
        }

        /// <summary>撤销或重做后更新仍在预览的状态。</summary>
        public void RefreshEditorPreviewAfterUndo()
        {
            if (!Application.isPlaying && IsPreviewing) Refresh();
        }

        /// <summary>恢复临时预览之前的序列化外观。</summary>
        public void RestoreSerializedAppearance() => ClearPreview();

        /// <summary>把当前选中的状态应用到所属目标。</summary>
        public void Refresh()
        {
            int version = ++_refreshVersion;
            string id = GetStateId(SelectedIndex);
            if (IsPreviewing)
                foreach (UIStateTargetBinding binding in _targetBindings)
                    if (binding != null && IsTargetInScope(binding.Target)) SavePreviewValues(binding.Target, binding.Properties);

            // 先隐藏最终应隐藏的节点，避免启用其父级时触发一次多余启用。
            for (int i = 0; i < _targetBindings.Count && version == _refreshVersion; i++)
            {
                UIStateTargetBinding binding = _targetBindings[i];
                if (binding != null && IsTargetInScope(binding.Target)) binding.ApplyInactive(id);
            }
            for (int i = 0; i < _targetBindings.Count && version == _refreshVersion; i++)
            {
                UIStateTargetBinding binding = _targetBindings[i];
                if (binding != null && IsTargetInScope(binding.Target)) binding.ApplyFields(id);
            }

            // 启用可能派发 OnEnable 并重入状态切换。重入后旧状态不能再写目标。
            bool hasActivatedTarget = false;
            for (int i = 0; i < _targetBindings.Count && version == _refreshVersion; i++)
            {
                UIStateTargetBinding binding = _targetBindings[i];
                if (binding != null && IsTargetInScope(binding.Target)) hasActivatedTarget |= binding.ApplyActive(id);
            }
            if (!hasActivatedTarget) return;

            // UIImage 启用时会按索引设置图像，恢复状态中显式声明的图像优先级。
            for (int i = 0; i < _targetBindings.Count && version == _refreshVersion; i++)
            {
                UIStateTargetBinding binding = _targetBindings[i];
                if (binding != null && IsTargetInScope(binding.Target)) binding.ReapplySprite(id);
            }
        }

        /// <summary>采集当前画布外观到所选状态。</summary>
        /// <returns>至少一个目标产生覆盖时返回 true。</returns>
        public bool CaptureCurrentToSelectedState()
        {
            string id = GetStateId(SelectedIndex);
            if (id == null) return false;
            bool hasCaptured = false;
            foreach (UIStateTargetBinding binding in _targetBindings)
                if (binding != null && IsTargetInScope(binding.Target))
                    hasCaptured |= binding.SetOverride(id, CaptureTarget(binding.Target));
            return hasCaptured;
        }

        /// <summary>替换状态列表并指定初态，同名状态保留既有标识。</summary>
        /// <param name="states">新状态名称。</param>
        /// <param name="selectedIndex">新初态位置。</param>
        public void ReplaceStates(IEnumerable<string> states, int selectedIndex = 0)
        {
            var replacement = new List<string>();
            if (states != null)
                foreach (string state in states)
                    if (!string.IsNullOrWhiteSpace(state) && !replacement.Contains(state)) replacement.Add(state);
            if (replacement.Count == 0) replacement.Add("Normal");

            var ids = new List<string>(replacement.Count);
            foreach (string state in replacement)
                ids.Add(GetStateId(IndexOfState(state)) ?? Guid.NewGuid().ToString("N"));
            foreach (string id in _stateIds)
                if (!ids.Contains(id))
                    foreach (UIStateTargetBinding binding in _targetBindings)
                        binding?.ClearOverride(id, UIStateProperty.All);

            ClearPreview();
            _states = replacement;
            _stateIds = ids;
            _initialStateId = ids[Mathf.Clamp(selectedIndex, 0, ids.Count - 1)];
            _runtimeState = null;
            if (Application.isPlaying) Refresh();
        }

        /// <summary>判断节点是否属于本控制器，独立的嵌套控制器拥有各自的节点。</summary>
        /// <param name="target">待检查的节点。</param>
        /// <returns>节点位于本控制器且无嵌套所有者时返回 true。</returns>
        public bool IsTargetInScope(GameObject target) => target != null && target.transform.IsChildOf(transform) &&
                                                           target.GetComponentInParent<UIStateController>(true) == this;

        /// <summary>读取节点当前可由状态系统控制的值。</summary>
        /// <param name="target">待读取的节点。</param>
        /// <returns>包含可用字段掩码和字段值的独立快照。</returns>
        public static UIStateVariant CaptureTarget(GameObject target) =>
            target == null ? new UIStateVariant() : new UIStateTargetAccess(target).Capture();

        /// <summary>把目标支持的指定字段加入控制器，保留其他基准数据。</summary>
        /// <param name="target">受控节点。</param>
        /// <param name="fields">需添加的属性掩码。</param>
        /// <returns>增加至少一个字段时返回 true。</returns>
        public bool TryAddTarget(GameObject target, UIStateProperty fields)
        {
            if (!IsTargetInScope(target)) return false;
            UIStateVariant values = CaptureTarget(target);
            UIStateTargetBinding binding = FindTarget(target);
            UIStateProperty additional = values.Properties & fields & ~(binding?.Properties ?? UIStateProperty.None);
            if (additional == UIStateProperty.None) return false;
            if (binding == null) _targetBindings.Add(new UIStateTargetBinding(target, UIStateVariantValues.Copy(values, additional)));
            else UIStateVariantValues.Merge(binding.Baseline, values, additional);
            return true;
        }

        /// <summary>移除目标的部分字段及相应覆盖。</summary>
        /// <param name="target">已绑定节点。</param>
        /// <param name="fields">待移除的属性掩码。</param>
        /// <returns>移除至少一个字段时返回 true。</returns>
        public bool RemoveTargetFields(GameObject target, UIStateProperty fields)
        {
            UIStateTargetBinding binding = FindTarget(target);
            if (binding == null || !binding.RemoveFields(fields)) return false;
            if (binding.Properties == UIStateProperty.None) _targetBindings.Remove(binding);
            return true;
        }

        /// <summary>移除控制器对指定节点的管理。</summary>
        /// <param name="target">已绑定节点。</param>
        /// <returns>移除成功时返回 true。</returns>
        public bool RemoveTarget(GameObject target)
        {
            UIStateTargetBinding binding = FindTarget(target);
            return binding != null && _targetBindings.Remove(binding);
        }

        /// <summary>读取指定目标和状态的覆盖。</summary>
        /// <param name="binding">属于本控制器的目标记录。</param>
        /// <param name="stateIndex">状态位置。</param>
        /// <returns>有覆盖时返回对应记录。</returns>
        public UIStateVariant GetOverride(UIStateTargetBinding binding, int stateIndex)
        {
            string id = GetStateId(stateIndex);
            return id == null || binding == null ? null : binding.FindOverride(id);
        }

        /// <summary>合并指定目标的状态覆盖，其他字段保持原值。</summary>
        /// <param name="target">已绑定节点。</param>
        /// <param name="stateIndex">状态位置。</param>
        /// <param name="value">包含要更新字段掩码的值。</param>
        /// <returns>覆盖发生改变时返回 true。</returns>
        public bool SetOverride(GameObject target, int stateIndex, UIStateVariant value)
        {
            string stateId = GetStateId(stateIndex);
            UIStateTargetBinding binding = FindTarget(target);
            return stateId != null && value != null && binding != null && binding.SetOverride(stateId, value);
        }

        /// <summary>清空指定字段的状态覆盖，使其沿用基准值。</summary>
        /// <param name="target">已绑定节点。</param>
        /// <param name="stateIndex">状态位置。</param>
        /// <param name="fields">待清除的属性掩码。</param>
        /// <returns>覆盖发生改变时返回 true。</returns>
        public bool ClearOverride(GameObject target, int stateIndex, UIStateProperty fields)
        {
            string id = GetStateId(stateIndex);
            UIStateTargetBinding binding = FindTarget(target);
            return id != null && binding != null && binding.ClearOverride(id, fields);
        }

        /// <summary>把画布中的现有字段值记录为状态覆盖。</summary>
        /// <param name="target">已绑定节点。</param>
        /// <param name="stateIndex">状态位置。</param>
        /// <param name="fields">待记录的属性掩码。</param>
        /// <returns>覆盖发生改变时返回 true。</returns>
        public bool CaptureOverride(GameObject target, int stateIndex, UIStateProperty fields) =>
            SetOverride(target, stateIndex, UIStateVariantValues.Copy(CaptureTarget(target), fields));

        #endregion

        #region 私有逻辑

        private void SavePreviewValues(GameObject target, UIStateProperty fields)
        {
            if (_previewValues == null || fields == UIStateProperty.None) return;
            _previewValues.TryGetValue(target, out UIStateVariant snapshot);
            UIStateProperty missing = fields & ~(snapshot?.Properties ?? UIStateProperty.None);
            if (missing == UIStateProperty.None) return;
            UIStateVariant current = CaptureTarget(target);
            if (snapshot == null) _previewValues.Add(target, UIStateVariantValues.Copy(current, missing));
            else UIStateVariantValues.Merge(snapshot, current, missing & current.Properties);
        }

        private UIStateTargetBinding FindTarget(GameObject target)
        {
            if (ReferenceEquals(target, null)) return null;
            foreach (UIStateTargetBinding binding in _targetBindings)
                if (binding != null && binding.Target == target) return binding;
            return null;
        }

        private string GetStateAt(int index) => index >= 0 && index < _states.Count ? _states[index] : string.Empty;

        private void Normalize()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                string current = _states[i];
                bool isDuplicate = !string.IsNullOrWhiteSpace(current) && IndexOfState(current) != i;
                if (string.IsNullOrWhiteSpace(current) || isDuplicate) _states[i] = CreateUniqueStateName(i);
            }
            if (_states.Count == 0) _states.Add("Normal");
            if (_stateIds.Count > _states.Count) _stateIds.RemoveRange(_states.Count, _stateIds.Count - _states.Count);
            while (_stateIds.Count < _states.Count) _stateIds.Add(Guid.NewGuid().ToString("N"));
            for (int i = 0; i < _stateIds.Count; i++)
                if (string.IsNullOrWhiteSpace(_stateIds[i]) || _stateIds.IndexOf(_stateIds[i]) != i)
                    _stateIds[i] = Guid.NewGuid().ToString("N");
            if (IndexOfStateId(_initialStateId) < 0) _initialStateId = _stateIds[0];
        }

        private string CreateUniqueStateName(int ignoreIndex = -1)
        {
            const string BASE_NAME = "State";
            if (!NameExists(BASE_NAME, ignoreIndex)) return BASE_NAME;
            int suffix = 2;
            while (NameExists(BASE_NAME + suffix, ignoreIndex)) suffix++;
            return BASE_NAME + suffix;
        }

        private bool NameExists(string name, int ignoreIndex)
        {
            for (int i = 0; i < _states.Count; i++)
                if (i != ignoreIndex && _states[i] == name) return true;
            return false;
        }

        private int IndexOfState(string state) => _states.IndexOf(state);

        #endregion
    }
}
