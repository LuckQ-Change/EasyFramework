using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>记录一个渠道及 Player 基线的整套发行尝试和已确认的回退点。</summary>
    internal sealed class HotUpdateReleaseJournal : IDisposable
    {
        private const int JOURNAL_SCHEMA_VERSION = 1;
        private const int CONFIRMED_CAPACITY = 2;
        private const int CHECKSUM_LENGTH = 32;
        private const int MAX_SNAPSHOT_BYTES = 8 * 1024 * 1024;
        private static readonly UTF8Encoding _utf8 = new UTF8Encoding(false, true);

        /// <summary>每次提交持久化的一致快照，发行按完整记录保存。</summary>
        [Serializable]
        private sealed class Snapshot
        {
            [SerializeField] private int _schemaVersion = JOURNAL_SCHEMA_VERSION;
            [SerializeField] private long _generation;
            [SerializeField] private string _pendingJson;
            [SerializeField] private string[] _confirmedJson;
            [SerializeField] private string[] _rejectedIds;
            private HashSet<string> _rejections;

            internal long Generation => _generation;
            internal string PendingJson => _pendingJson;
            internal string[] ConfirmedJson => _confirmedJson;
            internal string[] RejectedIds => _rejectedIds;
            internal HotUpdateReleaseRecord Pending { get; private set; }
            internal IReadOnlyList<HotUpdateReleaseRecord> Confirmed { get; private set; }

            internal Snapshot(long generation, string pendingJson, string[] confirmedJson, string[] rejectedIds)
            {
                _generation = generation;
                _pendingJson = pendingJson;
                _confirmedJson = confirmedJson;
                _rejectedIds = rejectedIds;
                ResolveRecords();
            }

            internal bool IsRejected(string releaseId) => _rejections.Contains(releaseId);

            internal void ResolveRecords()
            {
                if (_schemaVersion != JOURNAL_SCHEMA_VERSION || _generation < 0 ||
                    _confirmedJson == null || _confirmedJson.Length > CONFIRMED_CAPACITY || _rejectedIds == null)
                    throw new InvalidDataException("热更状态快照格式无效。");

                _rejections = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in _rejectedIds)
                    if (string.IsNullOrWhiteSpace(id) || !_rejections.Add(id))
                        throw new InvalidDataException("热更状态中存在无效或重复的拒绝发行 ID。");

                Pending = string.IsNullOrEmpty(_pendingJson) ? null : ParseRecord(_pendingJson);
                if (Pending != null && IsRejected(Pending.ReleaseId))
                    throw new InvalidDataException("待确认发行已经处于拒绝状态。");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                var confirmed = new List<HotUpdateReleaseRecord>(_confirmedJson.Length);
                foreach (string json in _confirmedJson)
                {
                    HotUpdateReleaseRecord release = ParseRecord(json);
                    if (!ids.Add(release.ReleaseId))
                        throw new InvalidDataException("热更状态中存在重复的已确认发行 ID。");
                    if (Pending != null && release.ReleaseId == Pending.ReleaseId && json != _pendingJson)
                        throw new InvalidDataException("待确认发行与已确认的同名发行内容不一致。");
                    if (!IsRejected(release.ReleaseId)) confirmed.Add(release);
                }
                Confirmed = confirmed.AsReadOnly();
            }

            private static HotUpdateReleaseRecord ParseRecord(string json)
            {
                HotUpdateReleaseRecord release = HotUpdateReleaseRecord.Parse(json);
                if (release == null || string.IsNullOrWhiteSpace(release.ReleaseId))
                    throw new InvalidDataException("热更状态中存在无效的发行记录。");
                return release;
            }
        }

        /// <summary>保留读取结果与各槽的校验错误，区分初次运行和已存状态损坏。</summary>
        private readonly struct Slot
        {
            internal Snapshot Snapshot { get; }
            internal Exception Error { get; }

            internal Slot(Snapshot snapshot, Exception error = null)
            {
                Snapshot = snapshot;
                Error = error;
            }
        }

        private readonly string _statePath;
        private readonly FileStream _lease;
        private Snapshot _snapshot;
        private int _activeSlot;
        private bool _isDisposed;

        #region 生命周期

        internal HotUpdateReleaseJournal(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("热更状态需要渠道和 Player 基线标识。", nameof(scope));
            string persistentPath = Application.persistentDataPath;
            if (string.IsNullOrWhiteSpace(persistentPath))
                throw new InvalidOperationException("当前平台没有可用的热更状态目录。");

            string directory = Path.Combine(persistentPath, "EasyFramework", "HotUpdate");
            Directory.CreateDirectory(directory);
            using (var sha = SHA256.Create())
                _statePath = Path.Combine(directory, BitConverter.ToString(sha.ComputeHash(_utf8.GetBytes(scope))).Replace("-", ""));

            try { _lease = new FileStream(_statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception)
            {
                throw new IOException("无法独占当前渠道及 Player 基线的热更状态。", exception);
            }

            try { Load(); }
            catch
            {
                _lease.Dispose();
                throw;
            }
        }

        /// <summary>释放此渠道及 Player 基线的独占状态句柄。未确认的发行仍保留。</summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _lease.Dispose();
        }

        #endregion

        #region 公共方法

        internal string PendingId => _snapshot.Pending?.ReleaseId;
        internal IReadOnlyList<HotUpdateReleaseRecord> ConfirmedReleases => _snapshot.Confirmed;

        internal bool IsRejected(string releaseId) => _snapshot.IsRejected(releaseId);

        internal void RejectInterruptedAttempt()
        {
            EnsureOpen();
            if (PendingId == null) return;

            // 把拒绝和清除旧尝试写入同一快照，避免重启时再选中未确认的发行。
            var rejected = new List<string>(_snapshot.RejectedIds) { PendingId };
            Commit(null, _snapshot.ConfirmedJson, rejected.ToArray());
        }

        internal void Begin(HotUpdateReleaseRecord release)
        {
            EnsureOpen();
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (IsRejected(release.ReleaseId))
                throw new InvalidOperationException($"发行「{release.ReleaseId}」因上次未确认而处于拒绝状态。");

            string json = release.ToJson();
            if (PendingId != null)
            {
                if (json == _snapshot.PendingJson) return;
                throw new InvalidOperationException($"本次启动已锁定发行「{PendingId}」。");
            }

            foreach (HotUpdateReleaseRecord known in ConfirmedReleases)
                if (known.ReleaseId == release.ReleaseId && known.ToJson() != json)
                    throw new InvalidOperationException($"已确认发行「{release.ReleaseId}」的内容发生变化。请使用新的发行 ID。");
            Commit(json, _snapshot.ConfirmedJson, _snapshot.RejectedIds);
        }

        internal void Confirm(HotUpdateReleaseRecord release)
        {
            EnsureOpen();
            if (release == null) throw new ArgumentNullException(nameof(release));
            string json = release.ToJson();
            if (PendingId == null || json != _snapshot.PendingJson)
                throw new InvalidOperationException($"发行「{release.ReleaseId}」与待确认记录不一致。");

            // 已确认指针和旧版本一并更新。保留最近一份不同且未被拒绝的成功记录。
            var confirmed = new List<string>(CONFIRMED_CAPACITY) { json };
            foreach (HotUpdateReleaseRecord previous in ConfirmedReleases)
            {
                if (previous.ReleaseId == release.ReleaseId) continue;
                confirmed.Add(previous.ToJson());
                break;
            }
            Commit(null, confirmed.ToArray(), _snapshot.RejectedIds);
        }

        #endregion

        #region 私有逻辑

        private void EnsureOpen()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(HotUpdateReleaseJournal));
        }

        private string SlotPath(int index) => _statePath + (index == 0 ? ".a" : ".b");

        private void Load()
        {
            Slot first = ReadSlot(0);
            Slot second = ReadSlot(1);
            if (first.Snapshot == null && second.Snapshot == null)
            {
                if (first.Error != null || second.Error != null)
                    throw new InvalidDataException("热更状态没有可校验的快照，无法恢复已确认发行。",
                        first.Error ?? second.Error);
                _snapshot = new Snapshot(0, null, Array.Empty<string>(), Array.Empty<string>());
                _activeSlot = 1;
                return;
            }

            _activeSlot = second.Snapshot != null &&
                          (first.Snapshot == null || second.Snapshot.Generation > first.Snapshot.Generation) ? 1 : 0;
            _snapshot = _activeSlot == 0 ? first.Snapshot : second.Snapshot;
        }

        private Slot ReadSlot(int index)
        {
            string path = SlotPath(index);
            byte[] bytes;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = stream.Length;
                if (length <= CHECKSUM_LENGTH || length > MAX_SNAPSHOT_BYTES)
                    return new Slot(null, new InvalidDataException($"热更快照长度无效（{path}）。"));

                bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int lengthRead = stream.Read(bytes, offset, bytes.Length - offset);
                    if (lengthRead == 0)
                        return new Slot(null, new InvalidDataException($"热更快照读取不完整（{path}）。"));
                    offset += lengthRead;
                }
            }
            catch (FileNotFoundException) { return default; }

            try
            {
                using var sha = SHA256.Create();
                byte[] checksum = sha.ComputeHash(bytes, CHECKSUM_LENGTH, bytes.Length - CHECKSUM_LENGTH);
                for (int i = 0; i < CHECKSUM_LENGTH; i++)
                    if (bytes[i] != checksum[i])
                        throw new InvalidDataException("热更快照的 SHA-256 校验失败。");

                string json = _utf8.GetString(bytes, CHECKSUM_LENGTH, bytes.Length - CHECKSUM_LENGTH);
                Snapshot snapshot = JsonUtility.FromJson<Snapshot>(json);
                if (snapshot == null || snapshot.Generation <= 0)
                    throw new InvalidDataException("热更快照序号无效。");
                snapshot.ResolveRecords();
                return new Slot(snapshot);
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException ||
                                               exception is InvalidOperationException || exception is FormatException)
            {
                return new Slot(null, new InvalidDataException($"无法读取热更快照（{path}）。", exception));
            }
        }

        private void Commit(string pendingJson, string[] confirmedJson, string[] rejectedIds)
        {
            var next = new Snapshot(checked(_snapshot.Generation + 1), pendingJson, confirmedJson, rejectedIds);
            byte[] bytes = _utf8.GetBytes(JsonUtility.ToJson(next));
            if (bytes.Length > MAX_SNAPSHOT_BYTES - CHECKSUM_LENGTH)
                throw new IOException("热更状态快照超过可保存大小。");

            using var sha = SHA256.Create();
            byte[] checksum = sha.ComputeHash(bytes);
            int nextSlot = 1 - _activeSlot;
            string temp = _statePath + ".writing";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(checksum, 0, checksum.Length);
                stream.Write(bytes, 0, bytes.Length);
                try { stream.Flush(flushToDisk: true); }
                catch (PlatformNotSupportedException) { stream.Flush(); }
            }

            // 只替换非活动槽。进程若在提交途中退出，上一份完整快照依然可读取。
            string destination = SlotPath(nextSlot);
            File.Delete(destination);
            File.Move(temp, destination);
            _snapshot = next;
            _activeSlot = nextSlot;
        }

        #endregion
    }
}
