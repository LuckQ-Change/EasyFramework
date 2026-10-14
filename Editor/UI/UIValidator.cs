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
    internal sealed class UIValidationIssue
    {
        public string Message { get; }
        public UnityEngine.Object Context { get; }

        public UIValidationIssue(string message, UnityEngine.Object context)
        {
            Message = message;
            Context = context;
        }
    }

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
            => FindIssues(display).Select(issue => issue.Message).ToList();

        internal static List<UIValidationIssue> FindIssues(UIDisplay display)
        {
            var problems = new List<UIValidationIssue>();
            if (display == null) { problems.Add(new UIValidationIssue("缺少 UIDisplay。", null)); return problems; }
            if (string.IsNullOrWhiteSpace(display.ViewTypeName))
            {
                problems.Add(new UIValidationIssue("尚未选择 View 类脚本。", display));
            }
            else
            {
                Type viewType = ResolveViewType(display.ViewTypeName);
                if (viewType == null)
                    problems.Add(new UIValidationIssue("记录的 View 类型无法加载，请重新选择 View 类脚本。", display));
                else if (!typeof(UIObject).IsAssignableFrom(viewType))
                    problems.Add(new UIValidationIssue($"记录的类型“{viewType.FullName}”没有继承 UIObject。", display));
                else if (display.ManagedAsView && !typeof(UIView).IsAssignableFrom(viewType))
                    problems.Add(new UIValidationIssue($"记录的类型“{viewType.FullName}”没有继承 UIView。", display));
                else if (display.ManagedAsView &&
                         !UIFactory.TryGetPrefabLocation(viewType, out _))
                    problems.Add(new UIValidationIssue($"View“{viewType.FullName}”缺少 UIPrefab 资源地址特性。", display));
            }
            if (display.ManagedAsView && string.IsNullOrWhiteSpace(display.PrefabLocation))
                problems.Add(new UIValidationIssue("View 缺少 Prefab Location。", display));
            if (!string.IsNullOrWhiteSpace(display.ViewScriptPath) &&
                !File.Exists(Path.GetFullPath(display.ViewScriptPath)))
                problems.Add(new UIValidationIssue("记录的业务脚本不存在，请重新生成。", display));
            foreach (Canvas canvas in display.GetComponentsInChildren<Canvas>(true))
            {
                Vector3 scale = canvas.transform.localScale;
                if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f))
                    problems.Add(new UIValidationIssue(
                        $"Canvas“{canvas.name}”的 X 或 Y 缩放为 0，页面内容不可见。", canvas));
                if (canvas.gameObject != display.gameObject &&
                    canvas.transform is RectTransform rect &&
                    rect.anchorMin == rect.anchorMax &&
                    (Mathf.Approximately(rect.sizeDelta.x, 0f) ||
                     Mathf.Approximately(rect.sizeDelta.y, 0f)))
                    problems.Add(new UIValidationIssue(
                        $"Canvas“{canvas.name}”宽或高为 0，需设置尺寸或拉伸锚点。", canvas));
            }
            UIBindingContext bindingContext = display.BindingContext;
            Type sourceType = UIBindingEditorReflection.ResolveSourceType(bindingContext);
            if (bindingContext != null)
            {
                foreach (UIBindingDefinition binding in bindingContext.Bindings)
                {
                    if (binding == null || binding.Target == null)
                        problems.Add(new UIValidationIssue("存在空的响应绑定 Target。", bindingContext));
                    if (binding != null && string.IsNullOrWhiteSpace(binding.SourceKey))
                        problems.Add(new UIValidationIssue("存在空的响应绑定 Source。", bindingContext));
                    if (binding != null && sourceType != null &&
                        !UIBindingEditorReflection.HasBindableMember(sourceType, binding.SourceKey))
                        problems.Add(new UIValidationIssue($"业务数据源中不存在响应属性：{binding.SourceKey}。", bindingContext));
                    if (binding != null && binding.TargetProperty == UIBindingProperty.Text &&
                        !IsValidFormat(binding.Format))
                        problems.Add(new UIValidationIssue($"响应绑定“{binding.SourceKey}”的 Format 格式无效。", bindingContext));
                }
            }

            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (UIReference marker in display.GetComponentsInChildren<UIReference>(true))
            {
                if (marker.GetComponentInParent<UIDisplay>(true) != display) continue;
                foreach (UIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null)
                        problems.Add(new UIValidationIssue($"{marker.name} 有空的 Reference Target。", marker));
                    if (entry != null && string.IsNullOrWhiteSpace(entry.Key))
                        problems.Add(new UIValidationIssue($"{marker.name} 有空的 Reference Key。", marker));
                    else if (entry != null && !properties.Add(entry.Key))
                        problems.Add(new UIValidationIssue($"Reference Key 重名：{entry.Key}。", marker));
                }
            }

            foreach (UIStateController controller in display.GetComponentsInChildren<UIStateController>(true))
            {
                foreach (UIElement element in controller.GetComponentsInChildren<UIElement>(true))
                {
                    if (element.BelongsTo(controller) == false) continue;
                    foreach (UIStateVariant variant in element.Variants)
                        if (variant != null && !controller.States.Contains(variant.State))
                            problems.Add(new UIValidationIssue($"{element.name} 引用了未定义状态：{variant.State}。", element));
                }
            }
            foreach (UITimeline timeline in display.GetComponentsInChildren<UITimeline>(true))
            {
                if (timeline.GetComponentInParent<UIDisplay>(true) != display) continue;
                foreach (UITimelineClip clip in timeline.Clips)
                {
                    if (clip == null || clip.Target == null)
                        problems.Add(new UIValidationIssue($"时间轴“{timeline.name}”存在未绑定目标的片段。", timeline));
                    else if (clip.Target.GetComponentInParent<UIDisplay>(true) != display)
                        problems.Add(new UIValidationIssue($"时间轴“{timeline.name}”的目标超出当前 Display。", clip.Target));
                }
            }
            return problems;
        }

        private static Type ResolveViewType(string typeName)
        {
            Type resolved = Type.GetType(typeName, false);
            if (resolved != null) return resolved;

            // 新生成的 View 在首次编译前只记录完整类名，没有程序集限定名。
            string fullName = typeName.Split(',')[0].Trim();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolved = assembly.GetType(fullName, false);
                if (resolved != null) return resolved;
            }
            return null;
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
