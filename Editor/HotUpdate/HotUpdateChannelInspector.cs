using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor
{
    /// <summary>为一个渠道校验并导出可一起发布的发行记录和指针。</summary>
    [CustomEditor(typeof(HotUpdateChannelConfig))]
    internal sealed class HotUpdateChannelInspector : UnityEditor.Editor
    {
        private FrameworkStartupConfig _startup;
        private HotUpdateStartupConfig _release;
        private string _receiptPath;
        private string _buildOutputDirectory;

        /// <summary>绘制渠道字段及生成本地发布文件的操作。</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("发行预检与导出", EditorStyles.boldLabel);
            _startup = (FrameworkStartupConfig)EditorGUILayout.ObjectField("Player 启动配置", _startup,
                typeof(FrameworkStartupConfig), false);
            _release = (HotUpdateStartupConfig)EditorGUILayout.ObjectField("要发布的发行", _release,
                typeof(HotUpdateStartupConfig), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                string receiptLabel = string.IsNullOrEmpty(_receiptPath) ? "未选择" : Path.GetFileName(_receiptPath);
                EditorGUILayout.LabelField(new GUIContent("Player 基线记录"), new GUIContent(receiptLabel, _receiptPath));
                if (GUILayout.Button("选择", GUILayout.Width(56f)))
                {
                    string selected = EditorUtility.OpenFilePanel("选择 Player 基线记录", string.Empty, "json");
                    if (!string.IsNullOrEmpty(selected)) _receiptPath = selected;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                string folderLabel = string.IsNullOrEmpty(_buildOutputDirectory) ? "未选择" : _buildOutputDirectory;
                EditorGUILayout.LabelField(new GUIContent("YooAsset 待上传目录"), new GUIContent(folderLabel, _buildOutputDirectory));
                if (GUILayout.Button("选择", GUILayout.Width(56f)))
                {
                    string selected = EditorUtility.OpenFolderPanel("选择 YooAsset 待上传目录", _buildOutputDirectory ?? string.Empty, string.Empty);
                    if (!string.IsNullOrEmpty(selected)) _buildOutputDirectory = selected;
                }
            }

            var channel = (HotUpdateChannelConfig)target;
            bool canValidate = _startup != null && _release != null && !string.IsNullOrEmpty(_receiptPath) &&
                               !string.IsNullOrEmpty(_buildOutputDirectory);
            using (new EditorGUI.DisabledScope(!canValidate))
            {
                if (GUILayout.Button("预检发行")) Preview(channel);
                if (GUILayout.Button("导出发行记录和渠道指针")) Export(channel);
            }
        }

        private void Preview(HotUpdateChannelConfig channel)
        {
            try
            {
                var result = HotUpdateReleasePreflight.Validate(channel, _startup, _release, _receiptPath, _buildOutputDirectory);
                var publication = channel.CreatePublication(result.record);
                HotUpdateReleaseExport.ValidateReleaseId(result.record, publication.ReleaseBytes);
                EditorUtility.DisplayDialog("发行预检通过", result.report, "确定");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("发行预检失败", exception.Message, "确定");
            }
        }

        private void Export(HotUpdateChannelConfig channel)
        {
            try
            {
                channel.Validate();
                string channelFileName = Path.GetFileNameWithoutExtension(new Uri(channel.ChannelUrl).AbsolutePath);
                if (string.IsNullOrEmpty(channelFileName)) channelFileName = "channel";
                string pointerPath = EditorUtility.SaveFilePanel("导出渠道指针", string.Empty, channelFileName, "json");
                if (string.IsNullOrEmpty(pointerPath)) return;
                if (string.Equals(Path.GetFullPath(pointerPath), Path.GetFullPath(_receiptPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请选择独立的渠道指针文件，保留 Player 基线记录。");
                var result = HotUpdateReleasePreflight.Validate(channel, _startup, _release, _receiptPath, _buildOutputDirectory);
                var publication = channel.CreatePublication(result.record);
                HotUpdateReleaseExport.WritePublication(pointerPath, result.record, publication);
                EditorUtility.DisplayDialog("已导出热更发布文件",
                    $"发行 {result.record.ReleaseId} · {result.record.TargetPlatform}\n{publication.ReleaseFileName}\n{Path.GetFileName(pointerPath)}",
                    "确定");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("导出热更发布文件失败", exception.Message, "确定");
            }
        }

    }
}
