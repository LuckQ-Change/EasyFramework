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

        public event Action<string> StateChanged;
        public IReadOnlyList<string> States => _states;
        public string SelectedState => string.IsNullOrEmpty(_runtimeState) ? GetStateAt(_selectedIndex) : _runtimeState;
        public int SelectedIndex => _selectedIndex;
        public UIStateChangedEvent OnStateChanged => _onStateChanged;

        private void Awake()
        {
            Normalize();
            _runtimeState = GetStateAt(_selectedIndex);
            Refresh();
        }

        private void OnValidate() => Normalize();

        public bool SetState(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            int index = _states.IndexOf(state);
            if (index < 0 && !_allowUndefinedState)
            {
                Log.Warn($"[UI] {name}: state '{state}' is not defined.");
                return false;
            }
            if (SelectedState == state) return true;

            _runtimeState = state;
            if (index >= 0) _selectedIndex = index;
            Refresh();
            StateChanged?.Invoke(state);
            _onStateChanged?.Invoke(state);
            return true;
        }

        public bool SetState(int index)
        {
            if (index < 0 || index >= _states.Count) return false;
            return SetState(_states[index]);
        }

        public void Refresh()
        {
            string state = SelectedState;
            var elements = GetComponentsInChildren<EasyUIElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].Controller == this) elements[i].ApplyState(state);
            }
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
            Normalize();
            if (Application.isPlaying)
            {
                _runtimeState = GetStateAt(_selectedIndex);
                Refresh();
            }
        }

        private string GetStateAt(int index)
        {
            if (_states.Count == 0) return string.Empty;
            return _states[Mathf.Clamp(index, 0, _states.Count - 1)];
        }

        private void Normalize()
        {
            for (int i = _states.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(_states[i]) || _states.IndexOf(_states[i]) != i) _states.RemoveAt(i);
            }
            if (_states.Count == 0) _states.Add("Normal");
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _states.Count - 1);
        }
    }
}
