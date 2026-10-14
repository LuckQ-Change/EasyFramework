using EasyFramework.Tweening;
using EasyFramework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class UITimelineTests
    {
        [TearDown]
        public void TearDown() => TweenScheduler.KillAll();

        [Test]
        public void SeekMatchesRuntimeTick_AndCanMoveBackwards()
        {
            var root = new GameObject("Timeline", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(UITimeline));
            try
            {
                Image image = root.GetComponent<Image>();
                UITimeline timeline = root.GetComponent<UITimeline>();
                var serialized = new SerializedObject(timeline);
                var clips = serialized.FindProperty("_clips");
                clips.arraySize = 1;
                var clip = clips.GetArrayElementAtIndex(0);
                clip.FindPropertyRelative("_target").objectReferenceValue = image;
                clip.FindPropertyRelative("_property").enumValueIndex = (int)UITimelineProperty.Color;
                clip.FindPropertyRelative("_start").floatValue = 0.2f;
                clip.FindPropertyRelative("_duration").floatValue = 0.6f;
                clip.FindPropertyRelative("_fromColor").colorValue = Color.white;
                clip.FindPropertyRelative("_toColor").colorValue = Color.blue;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                TweenSequence preview = timeline.BuildSequence();
                preview.Seek(0.5f);
                Color midpoint = image.color;
                Assert.AreEqual(Color.Lerp(Color.white, Color.blue, 0.5f), midpoint);
                preview.Seek(0f);
                Assert.AreEqual(Color.white, image.color);
                preview.Seek(0.8f);
                Assert.AreEqual(Color.blue, image.color);
                preview.Kill();

                image.color = Color.white;
                TweenSequence playback = timeline.BuildSequence();
                playback.Play();
                TweenScheduler.Tick(0.5f, 0.5f);
                Assert.AreEqual(midpoint, image.color);
                playback.Kill();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
