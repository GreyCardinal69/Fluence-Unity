using Fluence.Unity.RuntimeTypes;
using System.Collections.Generic;

namespace Fluence.Unity
{
    /// <summary>
    /// Manages a lexical scope, holding a table of symbols and a reference to its parent scope.
    /// </summary>
    internal sealed class FluenceScope
    {
        /// <summary>
        /// Gets the collection of symbols declared directly within this scope.
        /// </summary>
        internal readonly Dictionary<int, Symbol> Symbols = new Dictionary<int, Symbol>();

        /// <summary>
        /// Gets the name of this scope, primarily used for debugging and error messages.
        /// </summary>
        internal readonly string Name;

        /// <summary>Indicates whether this scope is the global scope.</summary>
        internal readonly bool IsTheGlobalScope;

        /// <summary>Indicates whether this scope is an intrinsic library scope.</summary>
        internal readonly bool IsIntrinsicScope;

        /// <summary>
        /// Keeps track of declared symbol names for name conflict detection.
        /// </summary>
        internal readonly HashSet<int> DeclaredSymbolNames = new HashSet<int>();

        /// <summary>
        /// Gets the parent scope in the hierarchy. This is null for the global scope.
        /// </summary>
        internal readonly FluenceScope ParentScope;

        /// <summary>
        /// The runtime storage for this scope's global variables.
        /// This is used by the VM to store the actual RuntimeValues.
        /// </summary>
        internal readonly Dictionary<int, RuntimeValue> RuntimeStorage = new Dictionary<int, RuntimeValue>();

        // Used in Tests. Might also be useful for other purposes.
        internal bool Contains(string name) => TryResolve(name.GetHashCode(), out _);
        internal bool ContainsLocal(int name) => TryGetLocalSymbol(name, out _);
        internal bool TryGetLocalSymbol(int hash, out Symbol symbol) => Symbols.TryGetValue(hash, out symbol!);

        internal FluenceScope(FluenceScope parentScope, string name, bool isIntrinsic, bool isTheGlobalScope = false)
        {
            IsIntrinsicScope = isIntrinsic;
            ParentScope = parentScope;
            Name = name;
            IsTheGlobalScope = isTheGlobalScope;
        }

        /// <summary>
        /// Declares a new symbol directly in this scope.
        /// </summary>
        /// <param name="name">The name of the symbol.</param>
        /// <param name="symbol">The symbol to declare.</param>
        /// <returns>True if the symbol was declared successfully; false if a symbol with the same name already exists in this scope.</returns>
        internal bool Declare(int hash, Symbol symbol)
        {
            if (Symbols.TryAdd(hash, symbol))
            {
                DeclaredSymbolNames.Add(hash);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to resolve a symbol by searching this scope and, if not found, recursively searching all parent scopes.
        /// </summary>
        /// <param name="name">The name of the symbol to find.</param>
        /// <param name="symbol">When this method returns, contains the found symbol, or null if it was not found.</param>
        /// <returns>True if the symbol was found in this scope or any parent scope; otherwise, false.</returns>
        internal bool TryResolve(int hash, out Symbol symbol)
        {
            FluenceScope? current = this;

            while (current != null)
            {
                if (current.Symbols.TryGetValue(hash, out symbol))
                    return true;

                current = current.ParentScope;
            }

            symbol = null!;
            return false;
        }

        public override string ToString() => $"Scope: {Name}";
    }
}