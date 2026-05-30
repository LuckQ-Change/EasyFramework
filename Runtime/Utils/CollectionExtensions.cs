using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public static class CollectionExtensions
    {
        private static readonly Random _rand = new Random();

        public static bool IsNullOrEmpty<T>(this ICollection<T> source)
        {
            return source == null || source.Count == 0;
        }

        public static T RandomItem<T>(this IList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[_rand.Next(list.Count)];
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rand.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static TValue GetOrAdd<TKey, TValue>(
            this Dictionary<TKey, TValue> dict,
            TKey key,
            Func<TKey, TValue> factory)
        {
            if (dict.TryGetValue(key, out var v)) return v;
            v = factory(key);
            dict[key] = v;
            return v;
        }
    }
}
