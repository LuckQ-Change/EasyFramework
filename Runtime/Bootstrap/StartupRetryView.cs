using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EasyFramework
{
    /// <summary>启动资源失败时显示的本地重试入口。</summary>
    internal sealed class StartupRetryView
    {
        private readonly GameObject _root;

        internal StartupRetryView(Transform parent, FrameworkStartupConfig config, Action retry)
        {
            _root = new GameObject("[Startup Retry]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _root.transform.SetParent(parent, false);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = config.ReferenceResolution;
            scaler.matchWidthOrHeight = config.MatchWidthOrHeight;

            var card = new GameObject("Startup error", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)card.transform;
            rect.SetParent(_root.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(500f, 190f);
            card.GetComponent<Image>().color = new Color(0.09f, 0.12f, 0.17f, 0.98f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            AddLabel(rect, font, "启动资源加载失败", 29, new Vector2(0f, 42f), new Vector2(470f, 44f));

            var button = new GameObject("Retry", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRect = (RectTransform)button.transform;
            buttonRect.SetParent(rect, false);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(0f, -35f);
            buttonRect.sizeDelta = new Vector2(190f, 58f);
            var image = button.GetComponent<Image>();
            image.color = new Color(0.12f, 0.36f, 0.72f);
            var action = button.GetComponent<Button>();
            action.targetGraphic = image;
            action.onClick.AddListener(() => retry());
            AddLabel(buttonRect, font, "重试", 27, Vector2.zero, buttonRect.sizeDelta);

            if (EventSystem.current == null && UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var input = new GameObject("Startup EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                input.transform.SetParent(_root.transform, false);
            }
        }

        internal void Show() => _root.SetActive(true);

        internal void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private static void AddLabel(RectTransform parent, Font font, string content, int fontSize,
            Vector2 position, Vector2 size)
        {
            var obj = new GameObject(content, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = obj.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
