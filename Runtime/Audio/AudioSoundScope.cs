using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>页面或玩法的待播音效作用域。释放时取消仍在等待的请求。</summary>
    public sealed class AudioSoundScope : IDisposable
    {
        private readonly AudioModule _audio;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _disposed;

        internal AudioSoundScope(AudioModule audio) => _audio = audio;

        public async Task<long> PlayAsync(string address, float volume = 1f, bool feedback = false,
            CancellationToken token = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioSoundScope));
            using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token);
            try
            {
                return feedback
                    ? await _audio.PlayFeedbackAsync(address, volume, request.Token)
                    : await _audio.PlaySoundAsync(address, volume, request.Token);
            }
            catch (OperationCanceledException) when (_disposed && !token.IsCancellationRequested) { return 0; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
