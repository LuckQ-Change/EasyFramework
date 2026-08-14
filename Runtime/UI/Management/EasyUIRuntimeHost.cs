using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>
    /// Minimal Unity adapter for the pure C# EasyUIManager service.
    /// </summary>
    [AddComponentMenu("EasyFramework/UI/Runtime Host")]
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class EasyUIRuntimeHost : MonoBehaviour
    {
        [Header("Canvas")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;

        [Header("Shared Background")]
        [SerializeField] private GameObject _backgroundPrefab;

        private EasyUIManager _manager;
        private readonly HashSet<IDisposable> _pendingDisposals = new HashSet<IDisposable>();

        public Vector2 ReferenceResolution => _referenceResolution;
        public float MatchWidthOrHeight => _matchWidthOrHeight;
        public GameObject BackgroundPrefab => _backgroundPrefab;
        public EasyUIManager Manager
        {
            get
            {
                if (_manager != null) return _manager;
                EasyUIManager.TryAttachHost(this, out _manager);
                return _manager;
            }
        }

        private void Awake()
        {
            if (!EasyUIManager.TryAttachHost(this, out _manager))
            {
                DestroyUnityObject(gameObject);
                return;
            }
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private void OnValidate() => _manager?.RefreshCanvasSettings(this);

        private void OnApplicationQuit() => EasyUIManager.NotifyApplicationQuit();

        private void OnDestroy()
        {
            foreach (IDisposable disposable in _pendingDisposals) disposable?.Dispose();
            _pendingDisposals.Clear();
            EasyUIManager manager = _manager;
            _manager = null;
            manager?.NotifyHostDestroyed(this);
        }

        internal void SetManager(EasyUIManager manager) => _manager = manager;

        internal void DisposeAfterFrame(IDisposable disposable)
        {
            if (disposable == null) return;
            if (isActiveAndEnabled)
            {
                _pendingDisposals.Add(disposable);
                StartCoroutine(DisposeNextFrame(disposable));
            }
            else disposable.Dispose();
        }

        private IEnumerator DisposeNextFrame(IDisposable disposable)
        {
            yield return null;
            if (_pendingDisposals.Remove(disposable)) disposable.Dispose();
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
