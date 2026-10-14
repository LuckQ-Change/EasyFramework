using System;
using System.Text;

namespace EasyFramework
{
    /// <summary>提供与框架生命周期无关的字符串辅助方法。</summary>
    public static class StringExtensions
    {
        public static bool IsNullOrEmpty(this string s) => string.IsNullOrEmpty(s);

        public static bool IsNullOrWhiteSpace(this string s) => string.IsNullOrWhiteSpace(s);

        public static string Or(this string s, string fallback)
        {
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        public static byte[] ToUtf8Bytes(this string s)
        {
            return s == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(s);
        }

        public static string FromUtf8(this byte[] bytes)
        {
            return bytes == null ? string.Empty : Encoding.UTF8.GetString(bytes);
        }
    }
}
