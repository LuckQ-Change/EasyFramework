using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EasyFramework.UI
{
    [Serializable]
    public sealed class UIStateChangedEvent : UnityEvent<string> { }

    [AddComponentMenu("EasyFramework/UI/State Controller")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class EasyUIStateController : MonoBehaviour
    {
        [SerializeField] private List<string> _states = new List<string> { "Normal", "Disabled" };
        [SerializeField] private int _selectedIndex;
        [SerializeField] private bool _allowUndefinedState;
        [SerializeField] private UIStateChangedEvent _onStateChanged = new UIStateChangedEvent();

        private string _runtimeState;
        private bool _editorPreview;

        public event Action<string> StateChanged;
        public IReadOnlyList<string> States => _states;
        public bool AllowUndefinedState => _allowUndefinedState;
        public bool IsPreviewing => _editorPreview;
        public string SelectedState => string.IsNullOrEmpty(_runtimeState) ? GetStateAt(_selectedIndex) : _runtimeState;
        public int SelectedIndex => _selectedIndex;
        public UIStateChangedEvent OnStateChanged => _onStateChanged;

        private void Awake()
        {
            Normalize();
            _editorPreview = false;
            _runtimeState = GetStateAt(_selectedIndex);
            Refresh();
        }

        private void OnValidate() => Normalize();

        public bool RenameState(int index, string newName)
        {
            if (index < 0 || index >= _states.Count || string.IsNullOrWhiteSpace(newName)) return false;
            newName = newName.Trim();
            if (_states[index] == newName) return true;
            if (_states.Contains(newName)) return false;
            string previous = _states[index];
            _states[index] = newName;
            RelinkVariantName(previous, newName);
            SyncChildVariants();
            if (_selectedIndex == index) _runtimeState = newName;
            return true;
        }

        public bool AddState(string state = null)
        {
            Normalize();
            string name = string.IsNullOrWhiteSpace(state) ? CreateUniqueStateName() : state.Trim();
            if (_states.Contains(name)) return false;
            _states.Add(name);
            SyncChildVariants();
            return true;
        }

        public void RelinkStates(IReadOnlyList<string> previous)
        {
            if (previous == null) return;
            int count = Mathf.Min(previous.Count, _states.Count);
            for (int i = 0; i < count; i++)
            {
                string from = previous[i];
                string to = _states[i];
                if (string.IsNullOrEmpty(from) || from == to) continue;
                if (_states.Contains(from)) continue;
                RelinkVariantName(from, to);
            }
            if (_selectedIndex >= 0 && _selectedIndex < _states.Count)
                _runtimeState = _states[_selectedIndex];
            SyncChildVariants();
        }

        public bool SetState(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            int index = _states.IndexOf(state);
            if (index < 0 && !_allowUndefinedState)
            {
                Log.Warn($"[UI] {name}: state '{state}' is not defined.");
                return false;
            }

            string previous = SelectedState;
            _runtimeState = state;
            _editorPreview = !Application.isPlaying;
            if (index >= 0) _selectedIndex = index;
            Refresh();
            if (previous == state) return true;
            StateChanged?.Invoke(state);
            _onStateChanged?.Invoke(state);
            return true;
        }

        public bool SetState(int index)
        {
            if (index < 0 || index >= _states.Count) return false;
            return SetState(_states[index]);
        }

        public bool Preview(string state) => SetState(state);

        public void RestoreSerializedAppearance()
        {
            _runtimeState = null;
            _editorPreview = false;
            var elements = GetComponentsInChildren<EasyUIElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].BelongsTo(this)) elements[i].ApplyDefaultOnly();
            }
        }

        public void Refresh()
        {
            string state = SelectedState;
            var elements = GetComponentsInChildren<EasyUIElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].BelongsTo(this)) elements[i].ApplyState(state);
            }
        }

        public void SyncChildVariants()
        {
            var elements = GetComponentsInChildren<EasyUIElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].BelongsTo(this)) elements[i].SyncVariantsToController();
            }
        }

        public bool CaptureCurrentToSelectedState()
        {
            string state = SelectedState;
            if (string.IsNullOrEmpty(state)) return false;
            var elements = GetComponentsInChildren<EasyUIElement>(true);
            bool captured = false;
            for (int i = 0; i < elements.Length; i++)
            {
                if (!elements[i].BelongsTo(this)) continue;
                captured |= elements[i].CaptureVariant(state);
            }
            return captured;
        }

        public void ReplaceStates(IEnumerable<string> states, int selectedIndex = 0)
        {
            _states.Clear();
            if (states != null)
            {
                foreach (string state in states)
                {
                    if (!string.IsNullOrWhiteSpace(state) && !_states.Contains(state)) _states.Add(state);
                }
            }
            _selectedIndex = selectedIndex;
            _runtimeState = null;
            _editorPreview = false;
            Normalize();
            SyncChildVariants();
            if (Application.isPlaying)
            {
                _runtimeState = GetStateAt(_selectedIndex);
                Refresh();
            }
        }

        private void RelinkVariantName(string from, string to)
        {
            var elements = GetComponentsInChildren<EasyUIElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (!elements[i].BelongsTo(this)) continue;
                UIStateVariant variant = elements[i].FindVariant(from);
                if (variant != null) variant.SetState(to);
            }
        }

        private string GetStateAt(int index)
        {
            if (_states.Count == 0) return string.Empty;
            return _states[Mathf.Clamp(index, 0, _states.Count - 1)];
        }

        private void Normalize()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                string current = _states[i];
                bool duplicate = !string.IsNullOrWhiteSpace(current) && IndexOfState(current) != i;
                if (!string.IsNullOrWhiteSpace(current) && !duplicate) continue;
                _states[i] = CreateUniqueStateName(i);
            }
            if (_states.Count == 0) _states.Add("Normal");
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _states.Count - 1);
        }

        private string CreateUniqueStateName(int ignoreIndex = -1)
        {
            if (!NameExists("State", ignoreIndex)) return "State";
            int suffix = 2;
            while (NameExists("State" + suffix, ignoreIndex)) suffix++;
            return "State" + suffix;
        }

        private bool NameExists(string name, int ignoreIndex)
        {
            for (int i = 0; i < _states.Count; i++)
            {
                if (i == ignoreIndex) continue;
                if (_states[i] == name) return true;
            }
            return false;
        }

        private int IndexOfState(string state)
        {
            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i] == state) return i;
            }
            return -1;
        }
    }
}
