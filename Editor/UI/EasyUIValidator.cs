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
            if (display.GetComponent<UIBindingContext>() == null) problems.Add("缺少 UIBindingContext。");
            if (string.IsNullOrWhiteSpace(display.Scripts.RecordId)) problems.Add("尚未生成 Display 脚本记录。");
            if (display.ManagedAsView && string.IsNullOrWhiteSpace(display.PrefabLocation))
                problems.Add("View 缺少 Prefab Location。");
            if (!string.IsNullOrWhiteSpace(display.Scripts.LogicScriptPath) &&
                !File.Exists(Path.GetFullPath(display.Scripts.LogicScriptPath)))
                problems.Add("记录的业务脚本不存在，请重新生成。");
            if (!string.IsNullOrWhiteSpace(display.Scripts.BindingScriptPath) &&
                !File.Exists(Path.GetFullPath(display.Scripts.BindingScriptPath)))
                problems.Add("记录的 Binding 脚本不存在，请重新生成。");
            if (!string.IsNullOrWhiteSpace(display.Scripts.DisplaySignature) &&
                display.Scripts.DisplaySignature != EasyUIScriptGenerator.ComputeSignature(display))
                problems.Add("Display 层级或绑定配置已改变，生成的 Binding 已过期。");

            Type sourceType = UIBindingEditorReflection.ResolveSourceType(display.BindingContext);
            foreach (UIBindingDefinition binding in display.BindingContext.Bindings)
            {
                if (binding == null || binding.Target == null) problems.Add("存在空的响应绑定 Target。");
                if (binding != null && string.IsNullOrWhiteSpace(binding.SourceKey)) problems.Add("存在空的响应绑定 Source。");
                if (binding != null && sourceType != null &&
                    !UIBindingEditorReflection.HasBindableMember(sourceType, binding.SourceKey))
                    problems.Add($"业务数据源中不存在响应属性：{binding.SourceKey}。");
            }

            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (EasyUIReference marker in display.GetComponentsInChildren<EasyUIReference>(true))
            {
                foreach (EasyUIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null) problems.Add($"{marker.name} 有空的 Reference Target。");
                    if (entry != null && string.IsNullOrWhiteSpace(entry.PropertyName))
                        problems.Add($"{marker.name} 有空的 Property Name。");
                    else if (entry != null && !properties.Add(entry.PropertyName))
                        problems.Add($"Reference Property 重名：{entry.PropertyName}。");
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
