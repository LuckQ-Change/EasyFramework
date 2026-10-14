using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public sealed class UIAdaptationTests
    {
        [Test]
        public void SafeArea_MapsPixelInsetsToAnchors_AndKeepsDisabledEdges()
        {
            var min = new Vector2(0.1f, 0.2f);
            var max = new Vector2(0.9f, 0.8f);
            UISafeArea.CalculateAnchors(new Rect(44f, 80f, 1082f, 2332f),
                1170, 2532, true, false, true, false, ref min, ref max);
            Assert.AreEqual(44f / 1170f, min.x, 0.0001f);
            Assert.AreEqual(0.2f, min.y);
            Assert.AreEqual(0.9f, max.x);
            Assert.AreEqual(2412f / 2532f, max.y, 0.0001f);
        }
    }
}
