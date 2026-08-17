using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [InitializeOnLoad]
    internal static class EasyUIValidator
    {
        static EasyUIValidator() => PrefabStage.prefabSaving += ValidateBeforeSave;

        public static bool ValidateAndReport(EasyUIDisplay display)
        {
            List<string> problems = Validate(display);
            if (problems.Count == 0)
            {
                EditorUtility.DisplayDialog("Easy UI", "Display 校验通过。", "确定");
                return true;
            }
            EditorUtility.DisplayDialog("Easy UI 校验", string.Join("\n", problems), "确定");
            return false;
        }

        public static List<string> Validate(EasyUIDisplay display)
        {
            var problems = new List<string>();
            if (display == null) { problems.Add("缺少 EasyUIDisplay。"); return problems; }
            if (string.IsNullOrWhiteSpace(display.ViewTypeName))
            {
                problems.Add("尚未选择 View 类脚本。");
            }
            else
            {
                Type viewType = Type.GetType(display.ViewTypeName, false);
                if (viewType == null)
                    problems.Add("记录的 View 类型无法加载，请重新选择 View 类脚本。");
                else if (!typeof(EasyUIObject).IsAssignableFrom(viewType))
                    problems.Add($"记录的类型“{viewType.FullName}”没有继承 EasyUIObject。");
                else if (display.ManagedAsView && !typeof(EasyUIView).IsAssignableFrom(viewType))
                    problems.Add($"记录的类型“{viewType.FullName}”没有继承 EasyUIView。");
            }
            if (display.ManagedAsView && string.IsNullOrWhiteSpace(display.PrefabLocation))
                problems.Add("View 缺少 Prefab Location。");
            if (!string.IsNullOrWhiteSpace(display.ViewScriptPath) &&
                !File.Exists(Path.GetFullPath(display.ViewScriptPath)))
                problems.Add("记录的业务脚本不存在，请重新生成。");
            UIBindingContext bindingContext = display.BindingContext;
            Type sourceType = UIBindingEditorReflection.ResolveSourceType(bindingContext);
            if (bindingContext != null)
            {
                foreach (UIBindingDefinition binding in bindingContext.Bindings)
                {
                    if (binding == null || binding.Target == null) problems.Add("存在空的响应绑定 Target。");
                    if (binding != null && string.IsNullOrWhiteSpace(binding.SourceKey)) problems.Add("存在空的响应绑定 Source。");
                    if (binding != null && sourceType != null &&
                        !UIBindingEditorReflection.HasBindableMember(sourceType, binding.SourceKey))
                        problems.Add($"业务数据源中不存在响应属性：{binding.SourceKey}。");
                }
            }

            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (EasyUIReference marker in display.GetComponentsInChildren<EasyUIReference>(true))
            {
                foreach (EasyUIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null) problems.Add($"{marker.name} 有空的 Reference Target。");
                    if (entry != null && string.IsNullOrWhiteSpace(entry.Key))
                        problems.Add($"{marker.name} 有空的 Reference Key。");
                    else if (entry != null && !properties.Add(entry.Key))
                        problems.Add($"Reference Key 重名：{entry.Key}。");
                }
            }

            foreach (EasyUIStateController controller in display.GetComponentsInChildren<EasyUIStateController>(true))
            {
                foreach (EasyUIElement element in controller.GetComponentsInChildren<EasyUIElement>(true))
                {
                    if (element.Controller != controller) continue;
                    foreach (UIStateVariant variant in element.Variants)
                        if (variant != null && !controller.States.Contains(variant.State))
                            problems.Add($"{element.name} 引用了未定义状态：{variant.State}。");
                }
            }
            return problems;
        }

        private static void ValidateBeforeSave(GameObject root)
        {
            EasyUIDisplay display = root == null ? null : root.GetComponent<EasyUIDisplay>();
            if (display == null) return;
            List<string> problems = Validate(display);
            if (problems.Count > 0)
                Debug.LogWarning($"[Easy UI] {root.name} 保存前校验发现 {problems.Count} 个问题：\n{string.Join("\n", problems)}", root);
        }
    }
}
