using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace EasyFramework
{
    public enum LoadingBackgroundMode
    {
        ImageRotation,
        VideoPlaylist,
    }

    [AddComponentMenu("EasyFramework/Loading Presenter")]
    [DisallowMultipleComponent]
    public sealed class EasyLoadingPresenter : MonoBehaviour
    {
        [Header("进度")]
        [SerializeField] private Image _progressBar;
        [SerializeField] private Text _progressLabel;
        [SerializeField] private Text _messageLabel;

        [Header("提示文本")]
        [SerializeField] private Text _tipLabel;
        [SerializeField] private string[] _tips = Array.Empty<string>();
        [SerializeField, Min(0.1f)] private float _tipInterval = 4f;

        [Header("背景")]
        [SerializeField]
        private LoadingBackgroundMode _backgroundMode;

        [SerializeField] private Image _imageBackground;
        [SerializeField] private Sprite[] _backgrounds = Array.Empty<Sprite>();
        [SerializeField, Min(0.1f)] private float _backgroundInterval = 6f;

        [Header("视频列表")]
        [SerializeField]
        private RawImage _videoBackground;

        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private VideoClip[] _videos = Array.Empty<VideoClip>();
        [SerializeField] private bool _loopPlaylist = true;

        private Coroutine _rotation;
        private int _backgroundIndex = -1;
        private int _videoIndex = -1;
        private int _tipIndex = -1;
        private bool _usingVideo;

        private void OnEnable()
        {
            if (LoadingModule.Instance != null)
            {
                LoadingModule.Instance.Changed += Apply;
                Apply(LoadingModule.Instance.State);
            }

            ConfigureBackground();
            _rotation = StartCoroutine(RotateContent());
        }

        private void OnDisable()
        {
            if (LoadingModule.Instance != null) LoadingModule.Instance.Changed -= Apply;
            if (_rotation != null) StopCoroutine(_rotation);
            _rotation = null;
            StopVideo();
        }

        private void LateUpdate()
        {
            if (_usingVideo && _videoBackground != null && _videoPlayer?.texture != null)
                _videoBackground.texture = _videoPlayer.texture;
        }

        private void Apply(LoadingState state)
        {
            if (_progressBar != null) _progressBar.fillAmount = state.Progress;
            if (_progressLabel != null) _progressLabel.text = $"{Mathf.RoundToInt(state.Progress * 100f)}%";
            if (_messageLabel != null) _messageLabel.text = state.Message;
        }

        private IEnumerator RotateContent()
        {
            float backgroundRemaining = Mathf.Max(0.1f, _backgroundInterval);
            float tipRemaining = 0f;
            while (true)
            {
                float delta = Time.unscaledDeltaTime;
                if (!_usingVideo && (backgroundRemaining -= delta) <= 0f)
                {
                    ShowNextImage();
                    backgroundRemaining = Mathf.Max(0.1f, _backgroundInterval);
                }

                if ((tipRemaining -= delta) <= 0f)
                {
                    ShowNextTip();
                    tipRemaining = Mathf.Max(0.1f, _tipInterval);
                }

                yield return null;
            }
        }

        private void ConfigureBackground()
        {
            _usingVideo = _backgroundMode == LoadingBackgroundMode.VideoPlaylist &&
                          _videoPlayer != null && _videos.Length > 0;
            if (_imageBackground != null) _imageBackground.enabled = !_usingVideo;
            if (_videoBackground != null) _videoBackground.enabled = _usingVideo;
            if (_usingVideo) PlayNextVideo(_videoPlayer);
            else ShowNextImage();
        }

        private void ShowNextImage()
        {
            if (_imageBackground == null || _backgrounds.Length == 0) return;
            _backgroundIndex = (_backgroundIndex + 1) % _backgrounds.Length;
            _imageBackground.sprite = _backgrounds[_backgroundIndex];
        }

        private void PlayNextVideo(VideoPlayer player)
        {
            if (player == null || _videos.Length == 0) return;
            player.loopPointReached -= PlayNextVideo;
            _videoIndex++;
            if (_videoIndex >= _videos.Length)
            {
                if (!_loopPlaylist)
                {
                    player.Stop();
                    return;
                }

                _videoIndex = 0;
            }

            player.playOnAwake = false;
            player.isLooping = false;
            player.renderMode = VideoRenderMode.APIOnly;
            player.clip = _videos[_videoIndex];
            player.loopPointReached += PlayNextVideo;
            player.Play();
        }

        private void StopVideo()
        {
            if (_videoPlayer == null) return;
            _videoPlayer.loopPointReached -= PlayNextVideo;
            _videoPlayer.Stop();
            if (_videoBackground != null) _videoBackground.texture = null;
        }

        private void ShowNextTip()
        {
            if (_tipLabel == null || _tips.Length == 0) return;
            _tipIndex = (_tipIndex + 1) % _tips.Length;
            _tipLabel.text = _tips[_tipIndex] ?? string.Empty;
        }

        private void OnValidate()
        {
            _tipInterval = Mathf.Max(0.1f, _tipInterval);
            _backgroundInterval = Mathf.Max(0.1f, _backgroundInterval);
            if (_tips == null) _tips = Array.Empty<string>();
            if (_backgrounds == null) _backgrounds = Array.Empty<Sprite>();
            if (_videos == null) _videos = Array.Empty<VideoClip>();
        }
    }
}
