using System;
using UnityEngine;

namespace EasyFramework
{
    [Serializable]
    public sealed class EasyLoadingSettings
    {
        public const string DefaultPrefabResourcePath = "LoadingView";

        [SerializeField] private GameObject _prefab;
        [SerializeField] private string _layerName = "UI";
        [SerializeField] private int _sortingOrder = 10000;
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;

        public GameObject Prefab
        {
            get
            {
                if (_prefab != null) return _prefab;
                return Application.isPlaying
                    ? Resources.Load<GameObject>(DefaultPrefabResourcePath)
                    : null;
            }
        }
        public string LayerName => string.IsNullOrWhiteSpace(_layerName) ? "UI" : _layerName;
        public int SortingOrder => _sortingOrder;
        public Vector2 ReferenceResolution => new Vector2(
            Mathf.Max(1f, _referenceResolution.x),
            Mathf.Max(1f, _referenceResolution.y));
        public float MatchWidthOrHeight => Mathf.Clamp01(_matchWidthOrHeight);
    }
}
