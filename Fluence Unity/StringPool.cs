namespace Fluence.Unity
{
    /// <summary>
    /// A high-performance string interning cache designed to minimize string allocations during lexing.
    /// </summary>
    internal static class StringPool
    {
        private static readonly Dictionary<int, string> _pool = new Dictionary<int, string>();

        /// <summary>
        /// Retrieves an existing string instance for the given character span, or creates and caches a new one if it doesn't exist.
        /// </summary>
        /// <param name="span">The character span to intern.</param>
        /// <returns>The interned string instance.</returns>
        internal static string Intern(ReadOnlySpan<char> span)
        {
            // Djb2 hash algorithm.
            int hash = 5381;

            unchecked
            {
                for (int i = 0; i < span.Length; i++)
                {
                    hash = (hash << 5) + hash + span[i]; // hash * 33 + c.
                }
            }

            if (_pool.TryGetValue(hash, out string? interned))
            {
                if (interned != null && span.SequenceEqual(interned.AsSpan()))
                {
                    return interned;
                }
            }

            string newString = span.ToString();
            _pool[hash] = newString;
            return newString;
        }

        /// <summary>
        /// Clears the string pool, releasing all cached string references.
        /// </summary>
        internal static void Clear() => _pool.Clear();
    }
}