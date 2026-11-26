using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>从渠道或本地完整记录选择发行，并持有该渠道在本进程的启动日志。</summary>
    internal sealed class HotUpdateReleaseSelection : IDisposable
    {
        private readonly FrameworkStartupConfig _startup;
        private readonly HotUpdateStartupConfig _bundled;
        private readonly HotUpdateChannelConfig _channel;
        private HotUpdateReleaseJournal _journal;
        private HotUpdateReleaseRecord _lockedRelease;
        private RuntimePlatform _platform;
        private HotUpdateBuildKind _buildKind;

        internal string ChannelId => _channel.ChannelId;
        internal HotUpdateReleaseSource Source { get; private set; }
        internal string FallbackReason { get; private set; }

        internal HotUpdateReleaseSelection(FrameworkStartupConfig startup, HotUpdateStartupConfig bundled,
            HotUpdateChannelConfig channel)
        {
            _startup = startup;
            _bundled = bundled;
            _channel = channel;
        }

        internal async Task<HotUpdatePlan> SelectAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FallbackReason = null;
            if (string.IsNullOrWhiteSpace(ChannelId))
                throw new InvalidOperationException("启用发行渠道时须填写渠道 ID。");
            if (string.IsNullOrWhiteSpace(_startup.PlayerBaselineId))
                throw new InvalidOperationException("启用发行渠道时须填写 Player 基线 ID。");

            _platform = Application.isEditor ? _bundled.TargetPlatform : Application.platform;
            _buildKind = Debug.isDebugBuild ? HotUpdateBuildKind.Development : HotUpdateBuildKind.Release;
            _journal ??= new HotUpdateReleaseJournal(JsonUtility.ToJson(new JournalScope(
                Application.identifier, Application.isEditor, ChannelId, _startup.PlayerBaselineId, _platform, _buildKind)));

            string pending = _journal.PendingId;
            if (!string.IsNullOrEmpty(pending))
            {
                // 先持久化未确认发行的拒绝状态。即使回退途中再次退出，下次也不会选回它。
                _journal.RejectInterruptedAttempt();
                return SelectFallback($"发行「{pending}」上次启动未确认");
            }

            try
            {
                HotUpdateReleaseRecord release = await _channel.FetchReleaseAsync(token);
                token.ThrowIfCancellationRequested();
                return Lock(release, HotUpdateReleaseSource.Channel);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                return SelectFallback($"渠道「{ChannelId}」选择发行失败。{exception.Message}");
            }
        }

        internal void Begin() => _journal.Begin(_lockedRelease);

        internal void Confirm() => _journal.Confirm(_lockedRelease);

        /// <summary>释放当前渠道的启动日志和进程间锁。</summary>
        public void Dispose() => _journal?.Dispose();

        private HotUpdatePlan SelectFallback(string reason)
        {
            FallbackReason = reason;
            var problems = new List<string> { reason };
            foreach (HotUpdateReleaseRecord release in _journal.ConfirmedReleases)
            {
                try
                {
                    HotUpdatePlan plan = Lock(release, HotUpdateReleaseSource.Confirmed);
                    Log.Warn($"[HotUpdate] {reason}，选用已确认发行「{plan.ReleaseId}」。");
                    return plan;
                }
                catch (Exception exception) { problems.Add($"已确认发行「{release.ReleaseId}」。{exception.Message}"); }
            }

            try
            {
                HotUpdatePlan plan = Lock(_bundled.CreateReleaseRecord(_buildKind), HotUpdateReleaseSource.Bundled);
                Log.Warn($"[HotUpdate] {reason}，选用随包发行「{plan.ReleaseId}」。");
                return plan;
            }
            catch (Exception exception) { problems.Add($"随包发行。{exception.Message}"); }
            throw new InvalidOperationException($"没有可启动的兼容发行。{string.Join("\n", problems)}");
        }

        private HotUpdatePlan Lock(HotUpdateReleaseRecord release, HotUpdateReleaseSource source)
        {
            if (_journal.IsRejected(release.ReleaseId))
                throw new InvalidOperationException($"发行「{release.ReleaseId}」曾启动未确认，当前渠道须提供不同的发行 ID。");
            if (release.BuildKind == HotUpdateBuildKind.Any)
                throw new InvalidOperationException($"渠道发行「{release.ReleaseId}」须指定构建类型。");

            // 相同 ID 对应不可变的整套记录，防止错误发布覆盖已经确认或随包归档的发行。
            string json = release.ToJson();
            if (source == HotUpdateReleaseSource.Channel && release.ReleaseId == _bundled.ReleaseId &&
                json != _bundled.CreateReleaseRecord(_buildKind).ToJson())
                throw new InvalidOperationException($"渠道发行「{release.ReleaseId}」与同名的随包发行内容不一致。");
            foreach (HotUpdateReleaseRecord confirmed in _journal.ConfirmedReleases)
                if (confirmed.ReleaseId == release.ReleaseId && confirmed.ToJson() != json)
                    throw new InvalidOperationException($"发行 ID「{release.ReleaseId}」已确认过不同内容。");

            HotUpdatePlan plan = _bundled.BuildPlan(_startup, _platform, _buildKind, release);
            plan.RuntimeProvider.ValidateRuntime(plan.AotMetadata.Count > 0);
            _lockedRelease = release;
            Source = source;
            return plan;
        }

        /// <summary>限定同一安装中的渠道、Player 兼容范围和编辑器状态。</summary>
        [Serializable]
        private sealed class JournalScope
        {
            [SerializeField] private string _application;
            [SerializeField] private bool _isEditor;
            [SerializeField] private string _channel;
            [SerializeField] private string _baseline;
            [SerializeField] private RuntimePlatform _platform;
            [SerializeField] private HotUpdateBuildKind _buildKind;

            internal JournalScope(string application, bool isEditor, string channel, string baseline,
                RuntimePlatform platform, HotUpdateBuildKind buildKind)
            {
                _application = application;
                _isEditor = isEditor;
                _channel = channel;
                _baseline = baseline;
                _platform = platform;
                _buildKind = buildKind;
            }
        }
    }
}
