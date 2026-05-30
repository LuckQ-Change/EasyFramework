using System.Threading;

namespace EasyFramework
{
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
