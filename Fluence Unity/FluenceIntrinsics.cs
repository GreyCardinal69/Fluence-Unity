using Fluence.Unity.Extensions;
using Fluence.Unity.Global;
using static Fluence.Unity.FluenceInterpreter;

namespace Fluence.Unity
{
    /// <summary>
    /// Manages the registration of standard library modules (intrinsics) for Fluence.
    /// It handles on-demand loading of libraries when 'use' statements are encountered by the parser.
    /// </summary>
    internal sealed class FluenceIntrinsics
    {
        /// <summary>
        /// A dictionary mapping namespace names to their registration actions.
        /// </summary>
        private readonly Dictionary<int, Action<FluenceScope>> _libraryRegistry = new();
        private readonly FluenceParser _parser;

        private readonly TextOutputMethod _outputLine;
        private readonly TextOutputMethod _output;
        private readonly TextInputMethod _input;
        private readonly TextOutputMethod _errorOutput;

        internal FluenceIntrinsics(FluenceParser parser, TextOutputMethod outputLine, TextInputMethod input, TextOutputMethod output, TextOutputMethod errorOutput)
        {
            _parser = parser;
            _outputLine = outputLine;
            _input = input;
            _output = output;

            // Pre-register all known standard libraries.
            _libraryRegistry[FluenceMath.NamespaceName.GetHashCode()] = FluenceMath.Register;
            _libraryRegistry[FluenceIO.NamespaceName.GetHashCode()] = FluenceIO.Register;

            _libraryRegistry[FluenceDiagnostics.NamespaceName.GetHashCode()] = (scope) =>
            {
                FluenceDiagnostics.Register(scope, _outputLine, _input, errorOutput);
            };
        }

        /// <summary>
        /// Registers the core global functions that are always available.
        /// This should be called once when the parser is initialized.
        /// </summary>
        internal void RegisterCoreGlobals()
        {
            GlobalLibrary.Register(_parser.CurrentParserStateGlobalScope, _outputLine, _input, _output);
        }

        internal void RegisterCustomIntrinsics(Dictionary<string, Action<LibraryBuilder>> libs)
        {
            if (libs == null) return;

            foreach (KeyValuePair<string, Action<LibraryBuilder>> lib in libs)
            {
                string libraryName = lib.Key;
                Action<LibraryBuilder> userDefinedAction = lib.Value;

                Action<FluenceScope> internalAction = (internalScope) =>
                {
                    LibraryBuilder builder = new LibraryBuilder(internalScope);
                    userDefinedAction(builder);
                };

                _libraryRegistry[libraryName.GetHashCode()] = internalAction;
            }
        }

        /// <summary>
        /// Attempts to find and register a standard library namespace.
        /// This method is called by the parser when it encounters a 'use' statement.
        /// </summary>
        /// <param name="namespaceName">The name of the namespace to load.</param>
        /// <returns>The newly created and populated scope if the library was found, otherwise null.</returns>
        internal FluenceScope? Use(string namespaceName)
        {
            int hash = namespaceName.GetHashCode();

            if (_libraryRegistry.TryGetValue(hash, out Action<FluenceScope>? registrationAction))
            {
                if (_parser.CurrentParseState.NameSpaces.TryGetValue(hash, out FluenceScope? scope))
                {
                    return scope;
                }

                FluenceScope newNamespaceScope = new FluenceScope(_parser.CurrentParserStateGlobalScope, namespaceName, true);
                registrationAction(newNamespaceScope);
                _parser.AddNameSpace(newNamespaceScope);

                return newNamespaceScope;
            }

            return null;
        }
    }
}