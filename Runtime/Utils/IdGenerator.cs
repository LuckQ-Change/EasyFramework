using System.Threading;

namespace EasyFramework
{
    /// <summary>生成进程内递增标识；不保证跨运行持久化。</summary>
    public static class IdGenerator
    {
        private static int _seed;

        public static int Next()
        {
            return Interlocked.Increment(ref _seed);
        }

        public static void Reset(int value = 0)
        {
            Interlocked.Exchange(ref _seed, value);
        }
    }
}
