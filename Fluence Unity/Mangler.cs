using System.Runtime.CompilerServices;

namespace Fluence.Unity
{
    /// <summary> Provides static helper methods for name mangling and demangling. </summary>
    internal static class Mangler
    {
        /// <summary> The constant separator used to distinguish the base name from the mangled arity suffix.</summary>
        private const string _separator = "__";

        private static readonly Dictionary<(string, int), string> _cache = new();

        /// <summary>
        /// Mangles a base function name by appending a special separator and its arity.
        /// </summary>
        /// <param name="name">The base name of the function.</param>
        /// <param name="arity">The number of arguments the function takes.</param>
        /// <returns>The unique mangled name string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Mangle(string name, int arity)
        {
            (string name, int arity) key = (name, arity);

            if (_cache.TryGetValue(key, out string? cached))
            {
                return cached;
            }

            string mangled = $"{name}__{arity}";
            _cache[key] = mangled;

            return mangled;
        }

        /// <summary> Demangles a name, separating it back into its base name and arity.</summary>
        internal static string Demangle(string mangledName, out int arity)
        {
            int sepIndex = mangledName.LastIndexOf(_separator, StringComparison.Ordinal);
            if (sepIndex > 0 && int.TryParse(mangledName[(sepIndex + _separator.Length)..], out arity))
            {
                return mangledName[..sepIndex];
            }

            arity = -1;
            return mangledName;
        }

        /// <summary>
        /// Demangles a name, returning only the base name and discarding the arity.
        /// </summary>
        /// <param name="mangledName">The full mangled name.</param>
        /// <returns>The original base name.</returns>
        internal static string Demangle(string mangledName) => Demangle(mangledName, out _);
    }
}