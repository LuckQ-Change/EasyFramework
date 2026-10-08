using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [InitializeOnLoad]
    internal static class UIValidator
    {
        static UIValidator() => PrefabStage.prefabSaving += ValidateBeforeSave;

        public static bool ValidateAndReport(UIDisplay display)
        {
            List<string> problems = Validate(display);
            if (problems.Count == 0)
            {
                EditorUtility.DisplayDialog("UI", "Display 校验通过。", "确定");
                return true;
            }
            EditorUtility.DisplayDialog("UI 校验", string.Join("\n", problems), "确定");
            return false;
        }

        public static List<string> Validate(UIDisplay display)
        {
            var problems = new List<string>();
            if (display == null) { problems.Add("缺少 UIDisplay。"); return problems; }
            if (string.IsNullOrWhiteSpace(display.ViewTypeName))
            {
                problems.Add("尚未选择 View 类脚本。");
            }
            else
            {
                Type viewType = Type.GetType(display.ViewTypeName, false);
                if (viewType == null)
                    problems.Add("记录的 View 类型无法加载，请重新选择 View 类脚本。");
                else if (!typeof(UIObject).IsAssignableFrom(viewType))
                    problems.Add($"记录的类型“{viewType.FullName}”没有继承 UIObject。");
                else if (display.ManagedAsView && !typeof(UIView).IsAssignableFrom(viewType))
                    problems.Add($"记录的类型“{viewType.FullName}”没有继承 UIView。");
                else if (display.ManagedAsView &&
                         !UIFactory.TryGetPrefabLocation(viewType, out _))
                    problems.Add($"View“{viewType.FullName}”缺少 UIPrefab 资源地址特性。");
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
                    if (binding != null && binding.TargetProperty == UIBindingProperty.Text &&
                        !IsValidFormat(binding.Format))
                        problems.Add($"响应绑定“{binding.SourceKey}”的 Format 格式无效。");
                }
            }

            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (UIReference marker in display.GetComponentsInChildren<UIReference>(true))
            {
                foreach (UIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null) problems.Add($"{marker.name} 有空的 Reference Target。");
                    if (entry != null && string.IsNullOrWhiteSpace(entry.Key))
                        problems.Add($"{marker.name} 有空的 Reference Key。");
                    else if (entry != null && !properties.Add(entry.Key))
                        problems.Add($"Reference Key 重名：{entry.Key}。");
                }
            }

            foreach (UIStateController controller in display.GetComponentsInChildren<UIStateController>(true))
            {
                foreach (UIElement element in controller.GetComponentsInChildren<UIElement>(true))
                {
                    if (element.BelongsTo(controller) == false) continue;
                    foreach (UIStateVariant variant in element.Variants)
                        if (variant != null && !controller.States.Contains(variant.State))
                            problems.Add($"{element.name} 引用了未定义状态：{variant.State}。");
                }
            }
            return problems;
        }

        private static bool IsValidFormat(string format)
        {
            if (string.IsNullOrEmpty(format)) return true;
            try
            {
                string.Format(CultureInfo.InvariantCulture, format, "value");
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static void ValidateBeforeSave(GameObject root)
        {
            UIDisplay display = root == null ? null : root.GetComponent<UIDisplay>();
            if (display == null) return;
            List<string> problems = Validate(display);
            if (problems.Count > 0)
                Debug.LogWarning($"[UI] {root.name} 保存前校验发现 {problems.Count} 个问题：\n{string.Join("\n", problems)}", root);
        }
    }
}
