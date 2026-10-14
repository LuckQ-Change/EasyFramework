using System;
using EasyFramework.Tweening;
using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Tests
{
    public sealed class TweenTests
    {
        [TearDown]
        public void TearDown() => TweenScheduler.KillAll();

        [Test]
        public void Tween_UsesSelectedClock_AndCompletesOnce()
        {
            float scaled = -1f;
            float unscaled = -1f;
            int completions = 0;
            TweenFactory.To(0f, 10f, 1f, value => scaled = value)
                .OnComplete(() => completions++).Play();
            TweenFactory.To(0f, 10f, 1f, value => unscaled = value)
                .SetTimeMode(TweenTimeMode.Unscaled).Play();

            TweenScheduler.Tick(0.25f, 0.5f);
            Assert.AreEqual(2.5f, scaled, 0.0001f);
            Assert.AreEqual(5f, unscaled, 0.0001f);
            TweenScheduler.Tick(0.75f, 0.5f);
            TweenScheduler.Tick(1f, 1f);
            Assert.AreEqual(10f, scaled, 0.0001f);
            Assert.AreEqual(10f, unscaled, 0.0001f);
            Assert.AreEqual(1, completions);
            Assert.AreEqual(0, TweenScheduler.ActiveCount);
        }

        [Test]
        public void Tween_KillDoesNotComplete_AndRestartReplays()
        {
            float value = -1f;
            int completions = 0;
            var tween = TweenFactory.To(0f, 1f, 1f, next => value = next)
                .OnComplete(() => completions++);
            tween.Play();
            TweenScheduler.Tick(0.5f, 0.5f);
            tween.Pause();
            TweenScheduler.Tick(1f, 1f);
            Assert.AreEqual(0.5f, value, 0.0001f);
            tween.Restart();
            TweenScheduler.Tick(1f, 1f);
            Assert.AreEqual(1, completions);
            tween.Restart();
            tween.Complete();
            Assert.AreEqual(2, completions);
            tween.Kill();
            Assert.AreEqual(2, completions);
        }

        [Test]
        public void Tween_ZeroDurationAndKillHaveDistinctResults()
        {
            float value = -1f;
            int completed = 0;
            TweenFactory.To(0f, 3f, 0f, next => value = next)
                .OnComplete(() => completed++).Play();
            Assert.AreEqual(3f, value);
            Assert.AreEqual(1, completed);

            var canceled = TweenFactory.To(0f, 5f, 1f, next => value = next)
                .OnComplete(() => completed++).Play();
            canceled.Kill();
            TweenScheduler.Tick(2f, 2f);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void Sequence_AppendsAndJoins_WithIndependentChildCompletion()
        {
            float first = -1f;
            float parallel = -1f;
            float last = -1f;
            int childCompletions = 0;
            int sequenceCompletions = 0;
            var sequence = TweenFactory.Sequence()
                .Append(TweenFactory.To(0f, 10f, 1f, value => first = value)
                    .OnComplete(() => childCompletions++))
                .Join(TweenFactory.To(0f, 20f, 1f, value => parallel = value))
                .Append(TweenFactory.To(0f, 30f, 1f, value => last = value));
            sequence.OnComplete(() => sequenceCompletions++).Play();

            TweenScheduler.Tick(0.5f, 0.5f);
            Assert.AreEqual(5f, first, 0.0001f);
            Assert.AreEqual(10f, parallel, 0.0001f);
            TweenScheduler.Tick(0.5f, 0.5f);
            Assert.AreEqual(1, childCompletions);
            Assert.AreEqual(0f, last, 0.0001f);
            TweenScheduler.Tick(1f, 1f);
            Assert.AreEqual(30f, last, 0.0001f);
            Assert.AreEqual(1, sequenceCompletions);
        }

        [Test]
        public void Sequence_ZeroDurationChildAtStart_CompletesOnce()
        {
            int childCompletions = 0;
            var sequence = TweenFactory.Sequence()
                .Append(TweenFactory.To(0f, 1f, 0f, _ => { })
                    .OnComplete(() => childCompletions++))
                .AppendInterval(1f);

            sequence.Play();
            TweenScheduler.Tick(0.1f, 0.1f);
            TweenScheduler.Tick(0.1f, 0.1f);
            Assert.AreEqual(1, childCompletions);
        }

        [Test]
        public void Sequence_SeekAndResume_KeepCallbacksPredictable()
        {
            float first = -1f;
            float last = -1f;
            int completed = 0;
            var sequence = TweenFactory.Sequence()
                .Append(TweenFactory.To(0f, 1f, 1f, value => first = value))
                .Append(TweenFactory.To(0f, 2f, 1f, value => last = value));
            sequence.OnComplete(() => completed++);

            sequence.Seek(1.5f);
            Assert.AreEqual(1f, first, 0.0001f);
            Assert.AreEqual(1f, last, 0.0001f);
            Assert.AreEqual(0, completed);
            sequence.Play();
            TweenScheduler.Tick(0.5f, 0.5f);
            Assert.AreEqual(2f, last, 0.0001f);
            Assert.AreEqual(1, completed);

            sequence.Restart();
            TweenScheduler.Tick(0.5f, 0.5f);
            sequence.Pause();
            Assert.Throws<InvalidOperationException>(() => sequence.Play().Seek(0.25f));
            sequence.Pause();
            sequence.Seek(0.25f);
            sequence.Play();
            TweenScheduler.Tick(1.75f, 1.75f);
            Assert.AreEqual(2, completed);
        }

        [Test]
        public void Scheduler_DefersTweenCreatedByCompletion()
        {
            int childCompletions = 0;
            TweenFactory.To(0f, 1f, 1f, _ => { })
                .OnComplete(() => TweenFactory.To(0f, 1f, 1f, _ => { })
                    .OnComplete(() => childCompletions++).Play())
                .Play();
            TweenScheduler.Tick(1f, 1f);
            Assert.AreEqual(0, childCompletions);
            Assert.AreEqual(1, TweenScheduler.ActiveCount);
            TweenScheduler.Tick(1f, 1f);
            Assert.AreEqual(1, childCompletions);
        }

        [Test]
        public void Tween_KillsWhenOwnerIsDestroyed()
        {
            var owner = new GameObject("Tween Owner");
            int completed = 0;
            var tween = TweenFactory.To(0f, 1f, 1f, _ => { })
                .SetOwner(owner).OnComplete(() => completed++).Play();
            Object.DestroyImmediate(owner);
            TweenScheduler.Tick(1f, 1f);
            Assert.IsTrue(tween.IsKilled);
            Assert.AreEqual(0, completed);
        }

        [Test]
        public void TweenModule_ShutdownKillsActiveTweens_WithoutCompleting()
        {
            var modules = new ModuleManager();
            modules.Register<TweenModule>();
            modules.InitAll();
            int completed = 0;
            var tween = TweenFactory.To(0f, 1f, 1f, _ => { })
                .OnComplete(() => completed++).Play();
            try
            {
                Assert.AreEqual(1, TweenScheduler.ActiveCount);
                modules.ShutdownAll();
                Assert.IsTrue(tween.IsKilled);
                Assert.AreEqual(0, completed);
                Assert.AreEqual(0, TweenScheduler.ActiveCount);
            }
            finally
            {
                modules.ShutdownAll();
            }
        }

        [Test]
        public void Tween_CallbackFailureDoesNotPreventFollowingCallbacks()
        {
            Action<string, Exception> previous = Log.ExceptionHandler;
            int errors = 0;
            int following = 0;
            try
            {
                Log.ExceptionHandler = (_, __) => errors++;
                TweenFactory.To(0f, 1f, 0f, _ => { })
                    .OnComplete(() => throw new InvalidOperationException("test"))
                    .OnComplete(() => following++)
                    .Play();
                Assert.AreEqual(1, errors);
                Assert.AreEqual(1, following);
            }
            finally { Log.ExceptionHandler = previous; }
        }

        [Test]
        public void Sequence_RejectsCycles_AndSchedulerRejectsInvalidTime()
        {
            var outer = TweenFactory.Sequence();
            var inner = TweenFactory.Sequence();
            outer.Append(inner);
            Assert.Throws<InvalidOperationException>(() => inner.Append(outer));
            Assert.Throws<ArgumentOutOfRangeException>(() => TweenScheduler.Tick(float.NaN, 0f));
        }

        [Test]
        public void UITransition_RestoresOriginalScale_AndClosesOnce()
        {
            var gameObject = new GameObject("Panel", typeof(RectTransform), typeof(UITransition));
            try
            {
                gameObject.transform.localScale = new Vector3(2f, 3f, 1f);
                var transition = gameObject.GetComponent<UITransition>();
                transition.PlayOpen();
                Assert.AreEqual(new Vector3(2f, 3f, 1f), gameObject.transform.localScale);
                int closes = 0;
                transition.PlayClose(() => closes++);
                Assert.AreEqual(1, closes);
                transition.PlayOpen();
                Assert.AreEqual(new Vector3(2f, 3f, 1f), gameObject.transform.localScale);
            }
            finally { Object.DestroyImmediate(gameObject); }
        }
    }
}
