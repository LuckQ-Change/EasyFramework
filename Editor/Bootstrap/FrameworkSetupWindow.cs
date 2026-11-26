using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor
{
    /// <summary>选择入口场景与框架方案，预览并应用启动配置。</summary>
    internal sealed class FrameworkSetupWindow : EditorWindow
    {
        private static readonly string[] _profileNames = { "最小本地", "标准 UI", "在线更新" };

        [SerializeField] private FrameworkSetupProfile _profile;
        [SerializeField] private DefaultAsset _outputFolder;
        [SerializeField] private List<AssetPackageConfig> _packages = new();
        [SerializeField] private HotUpdateStartupConfig _release;
        [SerializeField] private HotUpdateChannelConfig _channel;
        [SerializeField] private string _baselineId = string.Empty;

        private readonly List<Scene> _scenes = new();
        private Scene _scene;
        private FrameworkSetupPlan _plan;
        private FrameworkStartupConfig _result;
        private DropdownField _sceneField;
        private VisualElement _form;
        private VisualElement _onlineFields;
        private VisualElement _packageRows;
        private Label _profileDescription;
        private HelpBox _contextNotice;
        private HelpBox _status;
        private Button _previewButton;
        private Button _applyButton;
        private Button _locateButton;
        private Button _createPageButton;
        private bool _isContextRefreshQueued;

        #region 生命周期

        [MenuItem("Tools/EasyFramework/Setup Wizard", false, 0)]
        private static void Open()
        {
            var window = GetWindow<FrameworkSetupWindow>("EasyFramework 安装向导");
            window.minSize = new Vector2(480f, 380f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += QueueContextRefresh;
            EditorApplication.projectChanged += QueueContextRefresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= QueueContextRefresh;
            EditorApplication.projectChanged -= QueueContextRefresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
        }

        private void OnFocus()
        {
            if (_sceneField != null) RefreshContext();
        }

        private void OnPlayModeChanged(PlayModeStateChange _) => QueueContextRefresh();

        private void OnSceneSaved(Scene _) => QueueContextRefresh();

        #endregion

        #region 公共方法

        /// <summary>创建方案表单、预览结果和安装操作按钮。</summary>
        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.flexGrow = 1;
            body.contentContainer.style.paddingLeft = 12;
            body.contentContainer.style.paddingRight = 12;
            body.contentContainer.style.paddingTop = 12;
            body.contentContainer.style.paddingBottom = 12;
            root.Add(body);

            _contextNotice = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            body.Add(_contextNotice);
            _form = BuildForm();
            body.Add(_form);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 12;
            _previewButton = new Button(Preview) { text = "预览改动" };
            _applyButton = new Button(Apply) { text = "应用配置" };
            actions.Add(_previewButton);
            actions.Add(_applyButton);
            body.Add(actions);

            _status = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            _status.style.marginTop = 8;
            body.Add(_status);
            var resultActions = new VisualElement();
            resultActions.style.flexDirection = FlexDirection.Row;
            resultActions.style.justifyContent = Justify.FlexEnd;
            _locateButton = new Button(LocateConfig) { text = "定位启动配置" };
            resultActions.Add(_locateButton);
            _createPageButton = new Button(CreatePage) { text = "创建页面" };
            resultActions.Add(_createPageButton);
            body.Add(resultActions);

            UpdateProfile();
            ClearResult();
            RefreshContext();
        }

        #endregion

        #region 私有逻辑

        private VisualElement BuildForm()
        {
            var form = new VisualElement();
            _sceneField = new DropdownField("入口场景");
            _sceneField.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            _sceneField.RegisterValueChangedCallback(change =>
            {
                int index = _sceneField.choices.IndexOf(change.newValue);
                if (index < 0) return;
                _scene = _scenes[index];
                ClearResult();
            });
            form.Add(_sceneField);

            var profiles = new DropdownField("安装方案", new List<string>(_profileNames), (int)_profile);
            profiles.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            profiles.RegisterValueChangedCallback(change =>
            {
                _profile = (FrameworkSetupProfile)Array.IndexOf(_profileNames, change.newValue);
                UpdateProfile();
                ClearResult();
            });
            form.Add(profiles);

            _profileDescription = new Label();
            _profileDescription.style.whiteSpace = WhiteSpace.Normal;
            _profileDescription.style.marginTop = 4;
            _profileDescription.style.marginBottom = 8;
            form.Add(_profileDescription);

            var folder = AssetField("输出目录", typeof(DefaultAsset), _outputFolder);
            folder.tooltip = "选择 Assets 下的目录，留空时使用 Assets。";
            folder.RegisterValueChangedCallback(change =>
            {
                _outputFolder = change.newValue as DefaultAsset;
                ClearResult();
            });
            form.Add(folder);

            _onlineFields = BuildOnlineFields();
            form.Add(_onlineFields);
            return form;
        }

        private VisualElement BuildOnlineFields()
        {
            var panel = new VisualElement();
            panel.style.marginTop = 12;
            var release = AssetField("随包发行", typeof(HotUpdateStartupConfig), _release);
            release.RegisterValueChangedCallback(change =>
            {
                _release = change.newValue as HotUpdateStartupConfig;
                ClearResult();
            });
            panel.Add(release);

            var channel = AssetField("发行渠道（可选）", typeof(HotUpdateChannelConfig), _channel);
            channel.RegisterValueChangedCallback(change =>
            {
                _channel = change.newValue as HotUpdateChannelConfig;
                ClearResult();
            });
            panel.Add(channel);

            var baseline = new TextField("Player 基线 ID") { value = _baselineId };
            baseline.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            baseline.RegisterValueChangedCallback(change =>
            {
                _baselineId = change.newValue;
                ClearResult();
            });
            panel.Add(baseline);

            _packageRows = new VisualElement();
            _packageRows.style.marginTop = 8;
            panel.Add(_packageRows);
            RebuildPackageRows();

            var addPackage = new Button(() =>
            {
                _packages.Add(null);
                RebuildPackageRows();
                ClearResult();
            }) { text = "添加资源包" };
            addPackage.style.alignSelf = Align.FlexEnd;
            panel.Add(addPackage);
            return panel;
        }

        private void RebuildPackageRows()
        {
            _packageRows.Clear();
            for (int index = 0; index < _packages.Count; index++)
            {
                int packageIndex = index;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                ObjectField field = AssetField($"资源包 {index + 1}", typeof(AssetPackageConfig), _packages[index]);
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(change =>
                {
                    _packages[packageIndex] = change.newValue as AssetPackageConfig;
                    ClearResult();
                });
                row.Add(field);
                row.Add(new Button(() =>
                {
                    _packages.RemoveAt(packageIndex);
                    RebuildPackageRows();
                    ClearResult();
                }) { text = "移除" });
                _packageRows.Add(row);
            }
        }

        private void UpdateProfile()
        {
            _profileDescription.text = _profile switch
            {
                FrameworkSetupProfile.MinimalLocal => "本地启动与场景管理。",
                FrameworkSetupProfile.StandardUI => "启动配置、页面清单与直接引用资源包。",
                FrameworkSetupProfile.OnlineUpdate => "关联已有资源包和热更发行，应用前校验配置。",
                _ => string.Empty,
            };
            _onlineFields.style.display = _profile == FrameworkSetupProfile.OnlineUpdate
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void QueueContextRefresh()
        {
            if (_sceneField == null || _isContextRefreshQueued) return;
            _isContextRefreshQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _isContextRefreshQueued = false;
                if (_plan != null)
                {
                    _plan = null;
                    SetStatus("场景或项目状态已变化，请重新预览。", HelpBoxMessageType.Info);
                }
                RefreshContext();
            });
        }

        private void RefreshContext()
        {
            _scenes.Clear();
            var labels = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                _scenes.Add(scene);
                labels.Add(string.IsNullOrEmpty(scene.path)
                    ? $"{scene.name}（未保存，场景 {i + 1}）"
                    : scene.path);
            }

            int selected = _scenes.IndexOf(_scene);
            if (selected < 0) selected = _scenes.IndexOf(SceneManager.GetActiveScene());
            if (selected < 0 && _scenes.Count > 0) selected = 0;
            _scene = selected < 0 ? default : _scenes[selected];
            _sceneField.choices = labels;
            _sceneField.SetValueWithoutNotify(selected < 0 ? string.Empty : labels[selected]);

            string contextMessage = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) contextMessage = "退出 Play Mode 后可配置场景。";
            else if (PrefabStageUtility.GetCurrentPrefabStage() != null) contextMessage = "退出 Prefab Mode 后选择入口场景。";
            else if (_scenes.Count == 0) contextMessage = "打开要配置的入口场景。";
            _contextNotice.text = contextMessage ?? string.Empty;
            _contextNotice.style.display = contextMessage == null ? DisplayStyle.None : DisplayStyle.Flex;
            _form.SetEnabled(contextMessage == null);
            _previewButton.SetEnabled(contextMessage == null);
            RefreshResultActions();
        }

        private FrameworkSetupRequest CreateRequest()
        {
            return new FrameworkSetupRequest
            {
                Scene = _scene,
                Folder = _outputFolder != null ? AssetDatabase.GetAssetPath(_outputFolder) : "Assets",
                Profile = _profile,
                Packages = _packages.ToArray(),
                Release = _release,
                Channel = _channel,
                BaselineId = _baselineId,
            };
        }

        private void Preview()
        {
            ClearResult();
            try
            {
                _plan = FrameworkSetupInstaller.Preview(CreateRequest());
                SetStatus(_plan.Summary, HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, HelpBoxMessageType.Error);
            }
            RefreshResultActions();
        }

        private void Apply()
        {
            FrameworkSetupPlan plan = _plan;
            if (plan == null || !plan.HasChanges) return;
            ClearResult();
            try
            {
                _result = FrameworkSetupInstaller.Apply(CreateRequest(), plan);
                SetStatus($"已关联 {AssetDatabase.GetAssetPath(_result)}。请保存入口场景。", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, HelpBoxMessageType.Error);
            }
            RefreshResultActions();
        }

        private void LocateConfig()
        {
            FrameworkStartupConfig config = DisplayedConfig;
            if (config == null) return;
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        private void CreatePage()
        {
            FrameworkStartupConfig config = DisplayedConfig;
            if (config != null && config.PageManifest != null) UI.UICreationWizard.OpenPage(config);
        }

        private void ClearResult()
        {
            _plan = null;
            _result = null;
            SetStatus(null, HelpBoxMessageType.Info);
            RefreshResultActions();
        }

        private void RefreshResultActions()
        {
            _applyButton.SetEnabled(_previewButton.enabledSelf && _plan?.HasChanges == true);
            FrameworkStartupConfig config = DisplayedConfig;
            _locateButton.style.display = config != null ? DisplayStyle.Flex : DisplayStyle.None;
            _createPageButton.style.display = config != null && config.PageManifest != null
                ? DisplayStyle.Flex : DisplayStyle.None;
            _createPageButton.SetEnabled(_previewButton.enabledSelf);
        }

        private FrameworkStartupConfig DisplayedConfig => _result != null ? _result : _plan?.ExistingConfig;

        private void SetStatus(string message, HelpBoxMessageType type)
        {
            _status.text = message ?? string.Empty;
            _status.messageType = type;
            _status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static ObjectField AssetField(string label, Type type, Object value)
        {
            var field = new ObjectField(label) { objectType = type, allowSceneObjects = false, value = value };
            field.AddToClassList(BaseField<Object>.alignedFieldUssClassName);
            return field;
        }

        #endregion
    }
}
