using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace EasyFramework.Editor
{
    /// <summary>锁定项目内的发行 ID 并按顺序导出发行记录和渠道指针。</summary>
    internal static class HotUpdateReleaseExport
    {
        private const int HISTORY_SCHEMA_VERSION = 1;
        private const int MAX_RELEASE_BYTES = 512 * 1024;
        private const string HISTORY_PATH = "ProjectSettings/EasyFrameworkReleaseIds.json";
        private const string LOCK_PATH = "Library/EasyFrameworkReleaseIds.lock";
        private const string RELEASE_FILE_PREFIX = "release-";
        private const string JSON_EXTENSION = ".json";

        private static readonly UTF8Encoding _utf8 = new(false, true);

        /// <summary>存储本项目已锁定的发行 ID 及内容摘要。</summary>
        [Serializable]
        private sealed class ReleaseIdHistory
        {
            public int schemaVersion;
            public ReleaseIdEntry[] entries;
        }

        /// <summary>记录一个发行 ID 对应的唯一摘要。</summary>
        [Serializable]
        private sealed class ReleaseIdEntry
        {
            public string releaseId;
            public string sha256;
        }

        #region 公共方法

        internal static void ValidateReleaseId(HotUpdateReleaseRecord record, byte[] releaseBytes)
        {
            string releaseId = RequireReleaseId(record);
            string digest = ComputeSha256(releaseBytes);
            string historyPath = GetProjectPath(HISTORY_PATH);
            ValidateHistoryEntry(ReadHistory(historyPath), releaseId, digest, historyPath);
        }

        internal static void WritePublication(string pointerPath, HotUpdateReleaseRecord record,
            (string ReleaseFileName, byte[] ReleaseBytes, string PointerJson) publication)
        {
            string releaseId = RequireReleaseId(record);
            string digest = ComputeSha256(publication.ReleaseBytes);
            if (publication.ReleaseFileName != $"{RELEASE_FILE_PREFIX}{digest}{JSON_EXTENSION}")
                throw new InvalidDataException("发行记录文件名与内容的 SHA-256 不一致。");

            string historyPath = GetProjectPath(HISTORY_PATH);
            string lockPath = GetProjectPath(LOCK_PATH);
            pointerPath = Path.GetFullPath(pointerPath);
            if (IsReleaseFileName(Path.GetFileName(pointerPath)) ||
                string.Equals(pointerPath, historyPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pointerPath, lockPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择独立的渠道指针路径，保留发行记录和 ID 锁定记录。");

            string directory = Path.GetDirectoryName(pointerPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || Directory.Exists(pointerPath))
                throw new IOException($"渠道指针的输出路径无效：{pointerPath}。");
            string releasePath = Path.Combine(directory, publication.ReleaseFileName);
            byte[] pointerBytes = _utf8.GetBytes(publication.PointerJson);

            // 锁定摘要后才写发行和指针，导出中断后可用相同内容重试。
            using FileStream exportLock = OpenExportLock(lockPath);
            ReleaseIdHistory history = ReadHistory(historyPath);
            bool hasRecord = ValidateHistoryEntry(history, releaseId, digest, historyPath);
            ValidateOutputDirectory(directory, releaseId, digest);

            if (!hasRecord)
            {
                int index = history.entries.Length;
                Array.Resize(ref history.entries, index + 1);
                history.entries[index] = new ReleaseIdEntry { releaseId = releaseId, sha256 = digest };
                WriteAtomic(historyPath, _utf8.GetBytes(JsonUtility.ToJson(history, true)), canReplace: true);
            }

            if (File.Exists(releasePath))
            {
                if (new FileInfo(releasePath).Length != publication.ReleaseBytes.Length ||
                    ComputeSha256(File.ReadAllBytes(releasePath)) != digest)
                    throw new InvalidDataException($"已有发行记录与本次内容不同：{releasePath}。");
            }
            else WriteAtomic(releasePath, publication.ReleaseBytes, canReplace: false);
            WriteAtomic(pointerPath, pointerBytes, canReplace: true);
        }

        #endregion

        #region 私有逻辑

        private static string RequireReleaseId(HotUpdateReleaseRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(record.ReleaseId)) throw new InvalidOperationException("发行 ID 为空。");
            return record.ReleaseId;
        }

        private static string GetProjectPath(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));

        private static FileStream OpenExportLock(string path)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception)
            {
                throw new IOException($"无法获取发行导出锁。请等待其他导出完成或检查目录权限：{path}。", exception);
            }
        }

        private static ReleaseIdHistory ReadHistory(string path)
        {
            if (!File.Exists(path))
                return new ReleaseIdHistory { schemaVersion = HISTORY_SCHEMA_VERSION, entries = Array.Empty<ReleaseIdEntry>() };

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var reader = new StreamReader(stream, _utf8);
                var history = JsonUtility.FromJson<ReleaseIdHistory>(reader.ReadToEnd());
                if (history == null || history.schemaVersion != HISTORY_SCHEMA_VERSION || history.entries == null)
                    throw new InvalidDataException("格式版本或条目无效。");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ReleaseIdEntry entry in history.entries)
                    if (entry == null || string.IsNullOrWhiteSpace(entry.releaseId) ||
                        entry.releaseId != entry.releaseId.Trim() || !IsSha256(entry.sha256) || !ids.Add(entry.releaseId))
                        throw new InvalidDataException("存在无效或重复的发行 ID 条目。");
                return history;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException)
            {
                throw new InvalidDataException($"无法读取发行 ID 锁定记录：{path}。{exception.Message}", exception);
            }
        }

        private static bool ValidateHistoryEntry(ReleaseIdHistory history, string releaseId, string digest, string path)
        {
            foreach (ReleaseIdEntry entry in history.entries)
            {
                if (entry.releaseId != releaseId) continue;
                EnsureSameDigest(releaseId, digest, entry.sha256, path);
                return true;
            }
            return false;
        }

        private static void ValidateOutputDirectory(string directory, string releaseId, string digest)
        {
            foreach (string path in Directory.EnumerateFiles(directory, $"{RELEASE_FILE_PREFIX}*{JSON_EXTENSION}"))
            {
                string fileName = Path.GetFileName(path);
                if (!IsReleaseFileName(fileName)) continue;
                string expectedDigest = fileName.Substring(RELEASE_FILE_PREFIX.Length, 64);
                HotUpdateReleaseRecord existing;
                try
                {
                    if (new FileInfo(path).Length > MAX_RELEASE_BYTES)
                        throw new InvalidDataException($"发行记录超过 {MAX_RELEASE_BYTES} 字节。");
                    byte[] bytes = File.ReadAllBytes(path);
                    if (!string.Equals(expectedDigest, ComputeSha256(bytes), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("文件名与内容的 SHA-256 不一致。");
                    existing = HotUpdateReleaseRecord.Parse(_utf8.GetString(bytes));
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                                  exception is FormatException || exception is ArgumentException)
                {
                    throw new InvalidDataException($"无法校验输出目录中的发行记录：{path}。{exception.Message}", exception);
                }

                if (existing.ReleaseId == releaseId) EnsureSameDigest(releaseId, digest, expectedDigest, path);
            }
        }

        private static bool IsReleaseFileName(string fileName) =>
            fileName.Length == RELEASE_FILE_PREFIX.Length + 64 + JSON_EXTENSION.Length &&
            fileName.StartsWith(RELEASE_FILE_PREFIX, StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(JSON_EXTENSION, StringComparison.OrdinalIgnoreCase) &&
            IsSha256(fileName.Substring(RELEASE_FILE_PREFIX.Length, 64));

        private static bool IsSha256(string digest)
        {
            if (digest == null || digest.Length != 64) return false;
            foreach (char value in digest)
                if (!Uri.IsHexDigit(value)) return false;
            return true;
        }

        private static void EnsureSameDigest(string releaseId, string digest, string knownDigest, string path)
        {
            if (!string.Equals(digest, knownDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"发行 ID「{releaseId}」已绑定其他内容：{path}。内容变更后请使用新的发行 ID。");
        }

        private static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using SHA256 sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteAtomic(string path, byte[] bytes, bool canReplace)
        {
            string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            bool hasTemporary = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    hasTemporary = true;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                if (canReplace && File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (hasTemporary && File.Exists(temporary)) File.Delete(temporary);
            }
        }

        #endregion
    }
}
