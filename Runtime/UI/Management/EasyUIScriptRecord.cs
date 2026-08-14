using System;
using UnityEngine;

namespace EasyFramework.UI
{
    [Serializable]
    public sealed class EasyUIScriptRecord
    {
        [SerializeField] private string _recordId;
        [SerializeField] private string _logicTypeName;
        [SerializeField] private string _bindingTypeName;
        [SerializeField] private string _logicBaseTypeName;
        [SerializeField] private string _logicScriptPath;
        [SerializeField] private string _bindingScriptPath;
        [SerializeField] private string _displaySignature;

        public string RecordId => _recordId;
        public string LogicTypeName => _logicTypeName;
        public string BindingTypeName => _bindingTypeName;
        public string LogicBaseTypeName => _logicBaseTypeName;
        public string LogicScriptPath => _logicScriptPath;
        public string BindingScriptPath => _bindingScriptPath;
        public string DisplaySignature => _displaySignature;

        public void Set(
            string recordId,
            string logicTypeName,
            string bindingTypeName,
            string logicBaseTypeName,
            string logicScriptPath,
            string bindingScriptPath,
            string displaySignature)
        {
            _recordId = recordId;
            _logicTypeName = logicTypeName;
            _bindingTypeName = bindingTypeName;
            _logicBaseTypeName = logicBaseTypeName;
            _logicScriptPath = logicScriptPath;
            _bindingScriptPath = bindingScriptPath;
            _displaySignature = displaySignature;
        }
    }
}
