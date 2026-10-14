using System.Collections;
using System.Linq;
using System.Reflection;
using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EasyFramework.Tests.PlayMode
{
    public sealed class UIVirtualListPlayModeTests
    {
        [UnityTest]
        public IEnumerator Scroll_ReusesVisibleItems_AndDisableReleasesThem()
        {
            var root = new GameObject("List", typeof(RectTransform), typeof(ScrollRect));
            var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(root.transform, false);
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            var template = new GameObject("Template", typeof(RectTransform)).GetComponent<RectTransform>();
            template.SetParent(content, false);
            template.gameObject.SetActive(false);
            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            var list = root.AddComponent<UIVirtualList>();
            SetField(list, "_scrollRect", scroll);
            SetField(list, "_content", content);
            SetField(list, "_itemTemplate", template);
            list.SetCount(1000);
            Assert.LessOrEqual(list.LiveItemCount, 6);
            Assert.Greater(list.LiveItemCount, 0);
            int created = list.CreatedItemCount;
            int[] ids = content.GetComponentsInChildren<RectTransform>(true)
                .Where(rect => rect != content && rect != template).Select(rect => rect.GetInstanceID()).ToArray();
            list.ScrollTo(200);
            CollectionAssert.AreEquivalent(ids, content.GetComponentsInChildren<RectTransform>(true)
                .Where(rect => rect != content && rect != template).Select(rect => rect.GetInstanceID()).ToArray());
            Assert.AreEqual(created, list.CreatedItemCount);
            root.SetActive(false);
            Assert.AreEqual(0, list.LiveItemCount);
            Object.Destroy(root);
            yield return null;
        }

        private static void SetField(UIVirtualList list, string field, object value) =>
            typeof(UIVirtualList).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(list, value);
    }
}
