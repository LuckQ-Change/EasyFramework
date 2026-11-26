using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace EasyFramework
{
    /// <summary>指定发行渠道的 HTTPS 入口，下载并校验渠道指向的完整发行记录。</summary>
    [CreateAssetMenu(fileName = "HotUpdateChannel", menuName = "EasyFramework/Hot Update Channel")]
    public sealed class HotUpdateChannelConfig : ScriptableObject
    {
        /// <summary>渠道服务对外提供的指针 JSON。</summary>
        [Serializable]
        private sealed class ChannelPointer
        {
            public int schemaVersion;
            public string channelId;
            public string releaseId;
            public string releaseUrl;
            public string releaseSha256;
        }

        /// <summary>在接收网络数据时限制 JSON 可占用的字节数。</summary>
        private sealed class BoundedDownload : DownloadHandlerScript
        {
            private readonly byte[] _buffer;
            private int _length;

            internal bool HasExceededLimit { get; private set; }

            internal BoundedDownload(int maxBytes) : base(new byte[8192])
            {
                _buffer = new byte[maxBytes];
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)_buffer.Length) HasExceededLimit = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (dataLength > _buffer.Length - _length) HasExceededLimit = true;
                if (HasExceededLimit || data == null || dataLength < 0 || dataLength > data.Length) return false;

                Buffer.BlockCopy(data, 0, _buffer, _length, dataLength);
                _length += dataLength;
                return true;
            }

            internal byte[] CopyBytes()
            {
                var bytes = new byte[_length];
                Buffer.BlockCopy(_buffer, 0, bytes, 0, _length);
                return bytes;
            }
        }

        private const int POINTER_SCHEMA_VERSION = 1;
        private const int MAX_CHANNEL_ID_LENGTH = 128;
        private const int MAX_TIMEOUT_SECONDS = 60;
        private const int MAX_POINTER_BYTES = 16 * 1024;
        private const int MAX_RELEASE_BYTES = 512 * 1024;
        private static readonly UTF8Encoding _utf8 = new UTF8Encoding(false, true);

        [SerializeField, Tooltip("同一渠道 ID 对应同一套本地确认和回退记录。")]
        private string _channelId;

        [SerializeField, Tooltip("发布渠道指针 JSON 的 HTTPS 地址。")]
        private string _channelUrl;

        [SerializeField, Range(1, MAX_TIMEOUT_SECONDS), Tooltip("渠道指针与发行记录各自的请求超时。")]
        private int _timeoutSeconds = 10;

        /// <summary>区分项目内各渠道的稳定标识。</summary>
        public string ChannelId => _channelId?.Trim();

        /// <summary>项目指定的渠道指针地址。</summary>
        public string ChannelUrl => _channelUrl?.Trim();

        #region 公共方法

        /// <summary>校验渠道标识、HTTPS 地址和超时时间。</summary>
        public void Validate() => GetChannelUri();

        /// <summary>生成写入同一服务器目录的发行字节、文件名和指针 JSON。</summary>
        /// <param name="release">已通过当前 Player 配置校验并明确构建类型的发行记录。</param>
        /// <returns>对应同一份字节和 SHA-256 摘要的发布文件。</returns>
        public (string ReleaseFileName, byte[] ReleaseBytes, string PointerJson) CreatePublication(
            HotUpdateReleaseRecord release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            Validate();
            RequireExplicitBuildKind(release);
            if (string.IsNullOrWhiteSpace(release.ReleaseId)) throw new ArgumentException("发行 ID 为空。", nameof(release));

            byte[] bytes = _utf8.GetBytes(release.ToJson(isPretty: true));
            if (bytes.Length > MAX_RELEASE_BYTES)
                throw new InvalidDataException($"发行记录超过 {MAX_RELEASE_BYTES} 字节。");

            string sha256 = ComputeSha256(bytes);
            string fileName = $"release-{sha256}.json";
            var pointer = new ChannelPointer
            {
                schemaVersion = POINTER_SCHEMA_VERSION,
                channelId = ChannelId,
                releaseId = release.ReleaseId,
                releaseUrl = fileName,
                releaseSha256 = sha256,
            };
            string json = JsonUtility.ToJson(pointer, true);
            if (_utf8.GetByteCount(json) > MAX_POINTER_BYTES)
                throw new InvalidDataException($"渠道指针超过 {MAX_POINTER_BYTES} 字节。");
            return (fileName, bytes, json);
        }

        #endregion

        #region 私有逻辑

        internal async Task<HotUpdateReleaseRecord> FetchReleaseAsync(CancellationToken token)
        {
            Uri channelUri = GetChannelUri();
            string channelId = ChannelId;
            int timeoutSeconds = _timeoutSeconds;
            byte[] pointerBytes = await DownloadAsync(channelUri, "渠道指针", MAX_POINTER_BYTES, timeoutSeconds, token);
            ChannelPointer pointer = ParsePointer(pointerBytes, channelId);
            Uri releaseUri = ResolveReleaseUri(channelUri, pointer.releaseUrl);

            // 指针只选一份发行记录。先校验实际下载字节，再读取版本和产物。
            byte[] releaseBytes = await DownloadAsync(releaseUri, $"发行 {pointer.releaseId}", MAX_RELEASE_BYTES,
                timeoutSeconds, token);
            if (!string.Equals(ComputeSha256(releaseBytes), pointer.releaseSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"发行「{pointer.releaseId}」的记录与指针中的 SHA-256 不匹配。");

            HotUpdateReleaseRecord release = HotUpdateReleaseRecord.Parse(DecodeUtf8(releaseBytes));
            if (!string.Equals(release.ReleaseId, pointer.releaseId, StringComparison.Ordinal))
                throw new InvalidDataException($"渠道指向发行「{pointer.releaseId}」，记录中是「{release.ReleaseId}」。");
            RequireExplicitBuildKind(release);
            return release;
        }

        private Uri GetChannelUri()
        {
            if (string.IsNullOrEmpty(ChannelId) || ChannelId.Length > MAX_CHANNEL_ID_LENGTH)
                throw new InvalidOperationException($"渠道 ID 必须为 1 至 {MAX_CHANNEL_ID_LENGTH} 个字符。");
            if (!Uri.TryCreate(ChannelUrl, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
                uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new InvalidOperationException("渠道指针必须使用无凭据、无片段的 HTTPS 地址。");
            if (_timeoutSeconds < 1 || _timeoutSeconds > MAX_TIMEOUT_SECONDS)
                throw new InvalidOperationException($"渠道请求超时必须在 1 至 {MAX_TIMEOUT_SECONDS} 秒之间。");
            return uri;
        }

        private static ChannelPointer ParsePointer(byte[] bytes, string channelId)
        {
            ChannelPointer pointer;
            try { pointer = JsonUtility.FromJson<ChannelPointer>(DecodeUtf8(bytes)); }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"渠道「{channelId}」的指针不是有效的 UTF-8 JSON。", exception);
            }
            if (pointer == null || pointer.schemaVersion != POINTER_SCHEMA_VERSION ||
                string.IsNullOrWhiteSpace(pointer.releaseId) || string.IsNullOrWhiteSpace(pointer.releaseUrl) ||
                !IsSha256(pointer.releaseSha256))
                throw new InvalidDataException($"渠道「{channelId}」的指针版本、发行 ID、地址或 SHA-256 无效。");
            if (!string.Equals(pointer.channelId, channelId, StringComparison.Ordinal))
                throw new InvalidDataException($"期望渠道「{channelId}」，指针属于「{pointer.channelId}」。");
            return pointer;
        }

        private static Uri ResolveReleaseUri(Uri channelUri, string location)
        {
            if (location.IndexOf('\\') >= 0 || location.StartsWith("//", StringComparison.Ordinal) ||
                !Uri.TryCreate(location, UriKind.Relative, out Uri relative) ||
                !Uri.TryCreate(channelUri, relative, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.Port != channelUri.Port ||
                !string.Equals(uri.IdnHost, channelUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new InvalidDataException("渠道指针的发行地址必须为同一 HTTPS 站点下的相对地址。");
            return uri;
        }

        private static async Task<byte[]> DownloadAsync(Uri uri, string item, int maxBytes, int timeoutSeconds,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var download = new BoundedDownload(maxBytes);
            using var request = new UnityWebRequest(uri.AbsoluteUri, UnityWebRequest.kHttpVerbGET, download, null)
            {
                disposeDownloadHandlerOnDispose = false,
                timeout = timeoutSeconds,
                redirectLimit = 0,
            };
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            double startedAt = Time.realtimeSinceStartupAsDouble;
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested || download.HasExceededLimit)
                {
                    request.Abort();
                    break;
                }
                if (Time.realtimeSinceStartupAsDouble - startedAt >= timeoutSeconds)
                {
                    request.Abort();
                    throw new TimeoutException($"{item}请求超过 {timeoutSeconds} 秒。");
                }
                await Task.Yield();
            }

            token.ThrowIfCancellationRequested();
            if (download.HasExceededLimit) throw new InvalidDataException($"{item}超过 {maxBytes} 字节。");
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"{item}请求失败（HTTP {request.responseCode}）。{request.error}");
            return download.CopyBytes();
        }

        private static void RequireExplicitBuildKind(HotUpdateReleaseRecord release)
        {
            if (release.BuildKind != HotUpdateBuildKind.Release && release.BuildKind != HotUpdateBuildKind.Development)
                throw new InvalidDataException("渠道发行必须指定正式或开发构建。");
        }

        private static string DecodeUtf8(byte[] bytes)
        {
            string decoded = _utf8.GetString(bytes);
            return decoded.Length != 0 && decoded[0] == '\uFEFF' ? decoded.Substring(1) : decoded;
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool IsSha256(string value)
        {
            if (value?.Length != 64) return false;
            foreach (char digit in value)
                if (!Uri.IsHexDigit(digit)) return false;
            return true;
        }

        #endregion
    }
}
