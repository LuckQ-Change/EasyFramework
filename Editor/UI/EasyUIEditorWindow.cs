using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal sealed class EasyUIEditorWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("Tools/EasyFramework/UI/Easy UI Editor", false, 0)]
        private static void Open()
        {
            var window = GetWindow<EasyUIEditorWindow>("Easy UI Editor");
            window.minSize = new Vector2(480f, 360f);
            window.Show();
        }

        private void OnSelectionChange() => Repaint();

        private void OnGUI()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Easy UI Editor", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            EditorGUILayout.LabelField(
                "管理选中层级中的 UGUI / Easy UI 组件；替换会保留序列化属性、对象引用和 Undo。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSelectionSummary();
            EditorGUILayout.Space(8f);
            DrawRecommendedConversion();
            EditorGUILayout.Space(8f);
            DrawLegacyCleanup();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSelectionSummary()
        {
            int selectedRoots = Selection.gameObjects.Length;
            int imageCount = EasyUGUIComponentConverter.CountSelection(true);
            int easyCount = EasyUGUIComponentConverter.CountSelection(false);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("当前选择", EditorStyles.boldLabel);
                if (selectedRoots == 0)
                {
                    EditorGUILayout.HelpBox("请在 Hierarchy 中选择一个或多个根节点。", MessageType.Info);
                    return;
                }
                EditorGUILayout.LabelField("根节点", selectedRoots.ToString());
                EditorGUILayout.LabelField("可转换的原生 Image", imageCount.ToString());
                EditorGUILayout.LabelField("可还原的 Easy 包装组件", easyCount.ToString());
            }
        }

        private static void DrawRecommendedConversion()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("推荐能力：Sprite 列表", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "只有需要 Sprite 列表或按索引切图时，才把原生 Image 转为 EasyImage。状态机和 Binding 可直接使用原生 UGUI。",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(4f);
                using (new EditorGUI.DisabledScope(EasyUGUIComponentConverter.CountSelection(true) == 0))
                {
                    if (GUILayout.Button("将选中层级的 Image 转为 EasyImage", GUILayout.Height(32f)))
                        EasyUGUIComponentConverter.ConvertSelection(true);
                }
                if (GUILayout.Button("扫描转换影响并输出引用明细")) ShowImpact(true);
            }
        }

        private static void DrawLegacyCleanup()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("兼容组件清理", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "EasyButton、EasyText 等旧包装组件没有额外行为，因此不再提供正向替换入口；已有 Prefab 可在这里还原为原生 UGUI。EasyImage 也会一并还原。",
                    MessageType.None);
                using (new EditorGUI.DisabledScope(EasyUGUIComponentConverter.CountSelection(false) == 0))
                {
                    if (GUILayout.Button("还原选中层级的 Easy 组件", GUILayout.Height(32f)))
                        EasyUGUIComponentConverter.ConvertSelection(false);
                }
                if (GUILayout.Button("扫描还原影响并输出引用明细")) ShowImpact(false);
            }
        }

        private static void ShowImpact(bool toEasy)
        {
            EasyUGUIComponentConverter.GetSelectionImpact(
                toEasy,
                out int convertibleCount,
                out int referenceCount,
                true);
            EditorUtility.DisplayDialog(
                "Easy UI 组件影响",
                $"可处理组件：{convertibleCount}\n" +
                $"已加载场景 / 当前 Prefab 上下文引用：{referenceCount}\n\n" +
                "引用明细已输出到 Console。未加载的 Prefab 不在扫描范围内。",
                "确定");
        }
    }
}
