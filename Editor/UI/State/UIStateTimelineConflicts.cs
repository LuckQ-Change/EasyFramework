using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>定位状态控制器与同一根节点内的时间轴同时声明的属性。</summary>
    internal static class UIStateTimelineConflicts
    {
        /// <summary>查找当前控制器与时间轴的属性冲突。</summary>
        /// <param name="controller">要检查的状态控制器。</param>
        /// <returns>时间轴、动画、目标路径和冲突字段。</returns>
        internal static IReadOnlyList<string> Find(UIStateController controller)
        {
            if (controller == null) return Array.Empty<string>();
            Dictionary<GameObject, UIStateProperty> targets = CollectTargets(controller);
            if (targets.Count == 0) return Array.Empty<string>();

            Transform root = controller.transform.root;
            var conflicts = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var accesses = new Dictionary<GameObject, UIStateTargetAccess>();
            foreach (UITimeline timeline in root.GetComponentsInChildren<UITimeline>(true))
            {
                if (timeline == null) continue;
                string timelinePath = DisplayPath(timeline.transform, root);
                foreach (UITimelineAnimation animation in timeline.Animations)
                {
                    if (animation == null) continue;
                    foreach (UITimelineClip clip in animation.Clips)
                        if (clip != null) AddConflict(clip.Binding, animation.Name, timelinePath);
                    foreach (UITimelineKeyframeTrack track in animation.Tracks)
                        if (track != null) AddConflict(track.Binding, animation.Name, timelinePath);
                }
            }

            return conflicts;

            void AddConflict(UITimelineBinding binding, string animation, string timelinePath)
            {
                Component component = binding.Target;
                if (component == null || !targets.TryGetValue(component.gameObject, out UIStateProperty mask)) return;
                (UIStateProperty stateField, string fieldName) = binding.Property switch
                {
                    UITimelineProperty.Color => (UIStateProperty.Color, "颜色"),
                    UITimelineProperty.Alpha => (UIStateProperty.Alpha, "透明度"),
                    UITimelineProperty.FillAmount => (UIStateProperty.Value, "填充量"),
                    _ => (UIStateProperty.None, null),
                };
                if ((mask & stateField) == 0) return;
                if (!accesses.TryGetValue(component.gameObject, out UIStateTargetAccess access))
                {
                    access = new UIStateTargetAccess(component.gameObject);
                    accesses.Add(component.gameObject, access);
                }
                if (access.GetComponent(stateField) != component) return;
                string targetPath = DisplayPath(component.transform, root);
                string entry = $"{timelinePath} / {animation} → {targetPath} · {fieldName}";
                if (unique.Add(entry)) conflicts.Add(entry);
            }
        }

        private static Dictionary<GameObject, UIStateProperty> CollectTargets(UIStateController controller)
        {
            var targets = new Dictionary<GameObject, UIStateProperty>();
            foreach (UIStateTargetBinding binding in controller.TargetBindings)
            {
                if (binding == null || binding.Properties == UIStateProperty.None ||
                    !controller.IsTargetInScope(binding.Target)) continue;

                targets.TryGetValue(binding.Target, out UIStateProperty existing);
                targets[binding.Target] = existing | binding.Properties;
            }
            return targets;
        }

        private static string DisplayPath(Transform target, Transform root) =>
            target == root ? root.name : AnimationUtility.CalculateTransformPath(target, root);
    }
}
