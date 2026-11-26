using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework
{
    public enum AudioChannel { Master, Music, Sound }

    /// <summary>管理 BGM 和一次性音效，播放期间持有资源租约。</summary>
    public sealed class AudioModule : Module<AudioModule>, IFrameUpdate
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public AssetLease<AudioClip> Lease;
            public long Id;
            public float Gain;
            public int StartFrame;
            public bool Feedback;
        }

        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly List<Voice> _sounds = new();
        private readonly Stack<Voice> _idle = new();
        private readonly float[] _volumes = { 1f, 1f, 1f };
        private readonly bool[] _muted = new bool[3];
        private CancellationTokenSource _soundRequests = new();
        private CancellationTokenSource _musicRequest;
        private GameObject _root;
        private Voice _music;
        private long _nextSoundId;
        private int _pendingSounds;
        private int _pendingFeedback;
        private int _maxSoundVoices = 16;
        private bool _stopped;

        /// <summary>同时播放的音效上限，也用于限制普通音效的等待队列。</summary>
        public int MaxSoundVoices
        {
            get => _maxSoundVoices;
            set
            {
                VerifyRunning();
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
                _maxSoundVoices = value;
                while (_idle.Count > 0 && _idle.Count + _sounds.Count > value) DestroySource(_idle.Pop());
            }
        }

        public float GetVolume(AudioChannel channel) => _volumes[ChannelIndex(channel)];
        public bool IsMuted(AudioChannel channel) => _muted[ChannelIndex(channel)];

        public void SetVolume(AudioChannel channel, float volume)
        {
            VerifyRunning();
            _volumes[ChannelIndex(channel)] = NormalizeVolume(volume);
            ApplyChannel(channel);
        }

        public void SetMuted(AudioChannel channel, bool muted)
        {
            VerifyRunning();
            _muted[ChannelIndex(channel)] = muted;
            ApplyChannel(channel);
        }

        /// <summary>新曲就绪后替换当前曲目。被更新的请求或 StopMusic 取消时返回 false。</summary>
        public async Task<bool> PlayMusicAsync(string address, bool loop = true, CancellationToken cancellationToken = default)
        {
            VerifyRunning();
            cancellationToken.ThrowIfCancellationRequested();
            AssetModule assets = RequireAssets();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            CancellationTokenSource previous = _musicRequest;
            _musicRequest = request;
            previous?.Cancel();

            AssetLease<AudioClip> lease = null;
            try
            {
                lease = await AcquireClipAsync(assets, address, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (_music?.Source != null && _music.Lease?.IsValid == true &&
                    _music.Source.clip == lease.Asset && _music.Source.loop == loop &&
                    (_music.Source.isPlaying || AudioListener.pause))
                    return true;

                _music ??= new Voice();
                Release(_music);
                if (_music.Source == null) _music.Source = CreateSource("Music");
                Begin(_music, lease, AudioChannel.Music, 1f, loop);
                lease = null;
                return true;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                if (ReferenceEquals(_musicRequest, request)) _musicRequest = null;
                lease?.Dispose();
            }
        }

        /// <summary>取消待播曲目并停止当前曲目。</summary>
        public void StopMusic()
        {
            VerifyRunning();
            StopMusicCore();
        }

        public AudioSoundScope CreateSoundScope()
        {
            VerifyRunning();
            return new AudioSoundScope(this);
        }

        /// <summary>播放一次性音效并返回播放 ID。已满或被 StopSounds 取消时返回 0。</summary>
        public Task<long> PlaySoundAsync(string address, float volume = 1f, CancellationToken cancellationToken = default) =>
            PlaySoundCoreAsync(address, volume, false, cancellationToken);

        /// <summary>播放点击等即时反馈。反馈可在播放已满时替换最早的普通音效。</summary>
        public Task<long> PlayFeedbackAsync(string address, float volume = 1f, CancellationToken cancellationToken = default) =>
            PlaySoundCoreAsync(address, volume, true, cancellationToken);

        private async Task<long> PlaySoundCoreAsync(string address, float volume, bool feedback, CancellationToken cancellationToken)
        {
            VerifyRunning();
            cancellationToken.ThrowIfCancellationRequested();
            volume = NormalizeVolume(volume);
            if (feedback ? _pendingFeedback != 0 : _sounds.Count >= _maxSoundVoices || _pendingSounds >= _maxSoundVoices)
                return 0;
            AssetModule assets = RequireAssets();

            CancellationTokenSource generation = _soundRequests;
            using var request = CancellationTokenSource.CreateLinkedTokenSource(generation.Token, cancellationToken);
            if (feedback) _pendingFeedback++;
            else _pendingSounds++;
            AssetLease<AudioClip> lease = null;
            try
            {
                lease = await AcquireClipAsync(assets, address, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (_sounds.Count >= _maxSoundVoices)
                {
                    if (!feedback || _sounds.Count > _maxSoundVoices) return 0;
                    int oldest = _sounds.FindIndex(static voice => !voice.Feedback);
                    if (oldest < 0) return 0;
                    ReleaseSoundAt(oldest);
                }

                Voice voice = _idle.Count == 0 ? new Voice() : _idle.Pop();
                try
                {
                    if (voice.Source == null) voice.Source = CreateSource("Sound");
                    Begin(voice, lease, AudioChannel.Sound, volume, loop: false);
                }
                catch
                {
                    Recycle(voice);
                    throw;
                }
                lease = null;
                voice.Id = ++_nextSoundId;
                voice.Feedback = feedback;
                _sounds.Add(voice);
                return voice.Id;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
            finally
            {
                if (ReferenceEquals(_soundRequests, generation))
                {
                    if (feedback) _pendingFeedback--;
                    else _pendingSounds--;
                }
                lease?.Dispose();
            }
        }

        public bool StopSound(long id)
        {
            VerifyRunning();
            for (int i = 0; i < _sounds.Count; i++)
            {
                if (_sounds[i].Id != id) continue;
                ReleaseSoundAt(i);
                return true;
            }
            return false;
        }

        /// <summary>停止所有音效并取消等待加载的音效。</summary>
        public void StopSounds()
        {
            VerifyRunning();
            var previous = _soundRequests;
            _soundRequests = new CancellationTokenSource();
            _pendingSounds = 0;
            _pendingFeedback = 0;
            previous.Cancel();
            previous.Dispose();
            for (int i = _sounds.Count - 1; i >= 0; i--) ReleaseSoundAt(i);
        }

        void IFrameUpdate.Tick(FrameTime frameTime)
        {
            if ((_music?.Lease == null && _sounds.Count == 0) || AudioListener.pause) return;
            if (_music?.Lease != null && Finished(_music)) Release(_music);
            for (int i = _sounds.Count - 1; i >= 0; i--)
                if (Finished(_sounds[i])) ReleaseSoundAt(i);
        }

        protected override void OnShutdown()
        {
            _stopped = true;
            StopMusicCore();
            _soundRequests.Cancel();
            _soundRequests.Dispose();
            foreach (Voice voice in _sounds) Release(voice);
            _sounds.Clear();
            _idle.Clear();
            DestroyObject(_root);
            _root = null;
            _music = null;
        }

        private void StopMusicCore()
        {
            CancellationTokenSource request = _musicRequest;
            _musicRequest = null;
            request?.Cancel();
            if (_music != null) Release(_music);
        }

        private static async Task<AssetLease<AudioClip>> AcquireClipAsync(AssetModule assets, string address, CancellationToken token)
        {
            AssetLease<AudioClip> lease = await assets.AcquireAsync<AudioClip>(address, token);
            try
            {
                token.ThrowIfCancellationRequested();
                AudioClip clip = lease.Asset;
#if UNITY_EDITOR || (!UNITY_WEBGL && !WEIXINMINIGAME && !UNITY_WECHATMINIGAME && !UNITY_MINIGAME)
                if (clip.loadState == AudioDataLoadState.Unloaded && clip.loadType != AudioClipLoadType.Streaming &&
                    !clip.LoadAudioData() && clip.loadState != AudioDataLoadState.Loading)
                    throw new InvalidOperationException($"音频数据加载失败：{address}。");
#endif
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (clip.loadState != AudioDataLoadState.Loading) break;
                    await Task.Yield();
                }
                if (clip.loadState == AudioDataLoadState.Failed)
                    throw new InvalidOperationException($"音频数据加载失败：{address}。");
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        private void Begin(Voice voice, AssetLease<AudioClip> lease, AudioChannel channel, float gain, bool loop)
        {
            try
            {
                voice.Gain = gain;
                voice.Source.clip = lease.Asset;
                voice.Source.loop = loop;
                ApplyVolume(voice, channel);
                voice.Source.Play();
                voice.StartFrame = Time.frameCount;
                voice.Lease = lease;
            }
            catch
            {
                Release(voice);
                throw;
            }
        }

        private static bool Finished(Voice voice) =>
            voice.Source == null || (Time.frameCount != voice.StartFrame && !voice.Source.isPlaying &&
                (voice.Source.clip == null || voice.Source.clip.loadState != AudioDataLoadState.Loading));

        private void ReleaseSoundAt(int index)
        {
            Voice voice = _sounds[index];
            _sounds.RemoveAt(index);
            Recycle(voice);
        }

        private void Recycle(Voice voice)
        {
            Release(voice);
            if (voice.Source == null) return;
            if (_idle.Count + _sounds.Count < _maxSoundVoices) _idle.Push(voice);
            else DestroySource(voice);
        }

        private static void Release(Voice voice)
        {
            if (voice.Source != null)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
                voice.Source.loop = false;
            }
            voice.Lease?.Dispose();
            voice.Lease = null;
        }

        private void ApplyChannel(AudioChannel channel)
        {
            if (channel != AudioChannel.Sound && _music?.Source != null) ApplyVolume(_music, AudioChannel.Music);
            if (channel == AudioChannel.Music) return;
            foreach (Voice sound in _sounds)
                if (sound.Source != null) ApplyVolume(sound, AudioChannel.Sound);
        }

        private void ApplyVolume(Voice voice, AudioChannel channel)
        {
            int index = (int)channel;
            voice.Source.volume = _volumes[0] * _volumes[index] * voice.Gain;
            voice.Source.mute = _muted[0] || _muted[index];
        }

        private AudioSource CreateSource(string name)
        {
            if (_root == null)
            {
                _root = new GameObject("[Audio]");
                if (Application.isPlaying) Object.DontDestroyOnLoad(_root);
            }
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        private static void DestroySource(Voice voice)
        {
            if (voice.Source != null) DestroyObject(voice.Source.gameObject);
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        private static float NormalizeVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
            return Mathf.Clamp01(volume);
        }

        private static int ChannelIndex(AudioChannel channel)
        {
            if ((uint)channel > (uint)AudioChannel.Sound) throw new ArgumentOutOfRangeException(nameof(channel));
            return (int)channel;
        }

        private AssetModule RequireAssets() =>
            Host.Get<AssetModule>() ?? throw new InvalidOperationException("请为当前宿主配置音频资源包。");

        private void VerifyRunning()
        {
            if (_thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("请在 Unity 主线程调用音频模块。");
            if (_stopped) throw new ObjectDisposedException(nameof(AudioModule));
        }
    }
}
