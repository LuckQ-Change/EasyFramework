using System;
using System.Collections;
using UnityEngine;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Transition")]
    [DisallowMultipleComponent]
    public sealed class EasyUITransition : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _openDuration = 0.15f;
        [SerializeField, Min(0f)] private float _closeDuration = 0.12f;
        [SerializeField] private bool _scale = true;
        [SerializeField, Range(0.5f, 1f)] private float _startScale = 0.92f;

        private CanvasGroup _group;
        private Coroutine _routine;

        public void PlayOpen()
        {
            StopCurrent();
            gameObject.SetActive(true);
            _routine = StartCoroutine(Animate(0f, 1f, _openDuration, null));
        }

        public void PlayClose(Action completed)
        {
            StopCurrent();
            _routine = StartCoroutine(Animate(CurrentAlpha, 0f, _closeDuration, completed));
        }

        private float CurrentAlpha => EnsureGroup().alpha;

        private IEnumerator Animate(float from, float to, float duration, Action completed)
        {
            CanvasGroup group = EnsureGroup();
            group.blocksRaycasts = to > from;
            if (duration <= 0f)
            {
                Apply(to);
                completed?.Invoke();
                yield break;
            }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                Apply(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            Apply(to);
            _routine = null;
            completed?.Invoke();
        }

        private void Apply(float alpha)
        {
            EnsureGroup().alpha = alpha;
            if (_scale)
            {
                float normalized = Mathf.Clamp01(alpha);
                transform.localScale = Vector3.one * Mathf.Lerp(_startScale, 1f, normalized);
            }
        }

        private CanvasGroup EnsureGroup() =>
            _group == null ? (_group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>()) : _group;

        private void StopCurrent()
        {
            if (_routine == null) return;
            StopCoroutine(_routine);
            _routine = null;
        }
    }
}
