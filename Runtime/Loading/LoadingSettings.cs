using System;
using UnityEngine;

namespace EasyFramework
{
    [Serializable]
    /// <summary>保存全局加载画面的外观和资源配置。</summary>
    public sealed class LoadingSettings
    {
        [Tooltip("留空表示不创建全局 Loading 界面。")]
        [SerializeField] private GameObject _prefab;
        [Tooltip("Loading Canvas 的排序值，通常应高于普通 UI。")]
        [SerializeField] private int _sortingOrder = 10000;

        public GameObject Prefab => _prefab;
        public int SortingOrder => _sortingOrder;
    }
}
