using Fluence.Unity.Exceptions;
using Fluence.Unity.Extensions;
using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceParser;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    /// <summary>
    /// Provides commands for the execution of Fluence scripts and control of the Virtual Machine.
    /// </summary>
    public sealed class FluenceInterpreter
    {
        private ParseState _parseState;
        private FluenceIntrinsics _intrinsicsInstance;
        private List<InstructionLine> _byteCode;
        private FluenceVirtualMachine _vm;

        /// <summary>
        /// Defines the signature for a method that can receive output text from the Fluence VM.
        /// </summary>
        /// <param name="text">The text to be written.</param>
        public delegate void TextOutputMethod(string text);

        /// <summary>
        /// Defines the signature for a method that can provide input text to the Fluence VM.
        /// </summary>
        /// <returns>A line of text read from the input source.</returns>
        public delegate string TextInputMethod();

        /// <summary>
        /// Gets or sets the method used by the 'printl' family of functions to write text.
        /// Defaults to Console.WriteLine.
        /// </summary>
        public TextOutputMethod OnOutputLine { get; set; } = Console.WriteLine;

        private VirtualMachineConfiguration _vmConfiguration = new VirtualMachineConfiguration();

        /// <summary>
        /// Gets the configuration object that defines the runtime behavior and settings for this
        /// virtual machine instance.
        /// </summary>
        public VirtualMachineConfiguration Configuration
        {
            get => _vmConfiguration;
            private set
            {
                _vmConfiguration = value;
            }
        }

        /// <summary>
        /// Gets or sets the method used by the 'print' family of functions for non-newline output.
        /// Defaults to Console.Write.
        /// </summary>
        public TextOutputMethod OnOutput { get; set; } = Console.Write;

        /// <summary>
        /// Gets or sets the method used by the 'input()' function to read text.
        /// Defaults to Console.ReadLine.
        /// </summary>
        public TextInputMethod OnInput { get; set; } = Console.ReadLine!;

        /// <summary>
        /// A collection of the standard library names that are permitted to be loaded by a script.
        /// If this set is empty, all standard libraries are allowed. If it is populated, only the
        /// libraries whose names are in this set can be imported via the 'use' statement.
        /// This acts as a security whitelist for sandboxing script execution.
        /// </summary>
        public HashSet<string> AllowedLibraries { get; private set; } = new HashSet<string>();

        /// <summary>
        /// A collection of the standard library names that are not permitted to be loaded by the script.
        /// libraries whose names are in this set can not be imported via the 'use' statement.
        /// This acts as a security blacklist for sandboxing script execution.
        /// </summary>
        public HashSet<string> DisallowedLibraries { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Gets or sets the output method to report errors and exceptions.
        /// </summary>
        public TextOutputMethod OnErrorOutput { get; set; } = Console.Error.WriteLine;

        internal ParseState ParseState => _parseState;

        /// <summary>
        /// Gets the current execution state of the virtual machine.
        /// </summary>
        public FluenceVMState State => _vm?.State ?? FluenceVMState.NotStarted;

        /// <summary>
        /// Gets a value indicating whether the script has finished execution.
        /// </summary>
        public bool IsDone => _vm.State == FluenceVMState.Finished;

        internal FluenceVirtualMachine VM => _vm;

        private readonly Dictionary<string, Action<LibraryBuilder>> _customLibraries = new();

        public void RegisterLibrary(string namespaceName, Action<LibraryBuilder> registrar) => _customLibraries[namespaceName] = registrar;

        /// <summary>
        /// Initializes a new instance of the <see cref="FluenceInterpreter"/>.
        /// </summary>
        public FluenceInterpreter() { }

        /// <summary>
        /// Configures the execution frequency of the runtime timeout check. The code must be compiled successfully before the timeout can be set.
        /// </summary>
        /// <remarks>
        /// The Virtual Machine checks the <see cref="VirtualMachineConfiguration.ExecutionTimeout"/> every <paramref name="interval"/> instructions.
        /// <para>
        /// <b>Lower values</b> increase timeout precision (pausing closer to the limit) but add CPU overhead.
        /// <b>Higher values</b> improve execution speed but may result in the script running slightly longer than the requested timeout.
        /// </para>
        /// At the default interval (100,000) and typical execution speeds, the timeout precision is within ~0.5 to 1.0 milliseconds.
        /// For strict frame-time budgeting in game engines, a value of 25,000 is recommended. Minimum value is clamped to 100. Upper limit is 1,000,000.
        /// </remarks>
        /// <param name="interval">The number of instructions to execute between time checks.</param>
        public void SetElapsedTimeCheckInterval(int interval)
        {
            if (_byteCode == null)
            {
                throw new FluenceException("Code must be compiled successfully before the time elapsed check interval can be adjusted.");
            }

            _vm.SetElapsedTimeCheckInterval(Math.Clamp(interval, 100, 1000000));
        }

        /// <summary>
        /// Calls a specific function defined in the global scope of the script by its name.
        /// </summary>
        /// <remarks>
        /// Arguments are limited to primitives.
        /// </remarks>
        /// <param name="functionName">The unmangled name of the function.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        /// <returns>The value returned by the function.</returns>
        public object? CallFunction(string functionName, params object[] args)
        {
            if (_byteCode == null)
            {
                throw new InvalidOperationException("Code must be compiled successfully before a function can be called.");
            }

            _vm.InitializeGlobals();

            string mangledName = Mangler.Mangle(functionName, args.Length);

            if (!_parseState.GlobalScope.TryGetLocalSymbol(mangledName.GetHashCode(), out Symbol symbol) || symbol is not FunctionSymbol funcSymbol)
            {
                ConstructAndThrowException(new FluenceException($"Function '{functionName}' with {args.Length} arguments not found in the global scope."));
                return null;
            }

            return TryExecuteFunctionDirectly(args, funcSymbol);
        }

        /// <summary>
        /// Calls a specific function defined in the given namespace of the script by its name.
        /// </summary>
        /// <remarks>
        /// Arguments are limited to primitives.
        /// </remarks>
        /// <param name="functionName">The unmangled name of the function.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        /// <returns>The value returned by the function.</returns>
        public object? CallFunction(string functionName, string nameSpace, params object[] args)
        {
            if (_byteCode == null)
            {
                throw new InvalidOperationException("Code must be compiled successfully before a function can be called.");
            }

            _vm.InitializeGlobals();

            string mangledName = Mangler.Mangle(functionName, args.Length);

            if (!_parseState.NameSpaces.TryGetValue(nameSpace.GetHashCode(), out FluenceScope namespaceScope))
            {
                ConstructAndThrowException(new FluenceException($"Scope '{nameSpace}' not found in the script."));
                return null;
            }

            if (!namespaceScope.TryGetLocalSymbol(mangledName.GetHashCode(), out Symbol symbol) || symbol is not FunctionSymbol funcSymbol)
            {
                ConstructAndThrowException(new FluenceException($"Function '{functionName}' with {args.Length} arguments not found in the {nameSpace} scope."));
                return null;
            }

            return TryExecuteFunctionDirectly(args, funcSymbol);
        }

        private object? TryExecuteFunctionDirectly(object[] args, FunctionSymbol funcSymbol)
        {
            RuntimeValue[] vmArgs = new RuntimeValue[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                vmArgs[i] = ConvertToRuntimeValue(args[i], _vm);
            }

            try
            {
                RuntimeValue result = _vm.ExecuteFunctionDirect(funcSymbol, vmArgs);
                return ConvertToObject(result, this);
            }
            catch (FluenceException ex)
            {
                ConstructAndThrowException(ex);
                return null;
            }
        }

        /// <summary>
        /// Recursively converts a standard C# object into a Fluence RuntimeValue.
        /// </summary>
        internal static RuntimeValue ConvertToRuntimeValue(object? value, FluenceVirtualMachine vm) => value switch
        {
            null => RuntimeValue.Nil,

            int intVal => new RuntimeValue(intVal),
            long longVal => new RuntimeValue(longVal),
            double doubleVal => new RuntimeValue(doubleVal),
            float floatVal => new RuntimeValue(floatVal),
            bool boolVal => new RuntimeValue(boolVal),

            string stringVal => vm.ResolveStringObjectRuntimeValue(stringVal),
            char charVal => vm.ResolveCharObjectRuntimeValue(charVal),

            System.Collections.IDictionary dict => PackDictionary(dict, vm),

            System.Collections.IEnumerable list => PackList(list, vm),

            Wrapper wrapper => new RuntimeValue(wrapper),

            _ => new RuntimeValue(new Wrapper(value, new Dictionary<string, IntrinsicRuntimeMethod>()))
        };

        private static RuntimeValue PackList(System.Collections.IEnumerable enumerable, FluenceVirtualMachine vm)
        {
            ListObject listObj = new ListObject();
            foreach (object? item in enumerable)
            {
                listObj.Elements.Add(ConvertToRuntimeValue(item, vm));
            }
            return new RuntimeValue(listObj);
        }

        private static RuntimeValue PackDictionary(System.Collections.IDictionary dict, FluenceVirtualMachine vm)
        {
            DictionaryObject dictObj = new DictionaryObject();
            foreach (System.Collections.DictionaryEntry kvp in dict)
            {
                RuntimeValue key = ConvertToRuntimeValue(kvp.Key, vm);
                RuntimeValue val = ConvertToRuntimeValue(kvp.Value, vm);
                dictObj.Dictionary[key] = val;
            }
            return new RuntimeValue(dictObj);
        }

        internal static object? ConvertToObject(RuntimeValue val, FluenceInterpreter interpreter) => val.Type switch
        {
            RuntimeValueType.Nil => null,
            RuntimeValueType.Boolean => val.IntValue != 0,
            RuntimeValueType.Number => val.NumberType switch
            {
                RuntimeNumberType.Int => val.IntValue,
                RuntimeNumberType.Long => val.LongValue,
                RuntimeNumberType.Float => val.FloatValue,
                RuntimeNumberType.Double => val.DoubleValue,
                _ => 0d
            },
            RuntimeValueType.Object => val.ObjectReference switch
            {
                StringObject str => str.Value,
                CharObject chr => chr.Value,

                ListObject list => list.Elements.Select(e => ConvertToObject(e, interpreter)).ToList(),

                DictionaryObject dict => dict.Dictionary.ToDictionary(
                    kvp => ConvertToObject(kvp.Key, interpreter)!,
                    kvp => ConvertToObject(kvp.Value, interpreter)
                ),

                Wrapper wrapper => wrapper.Instance,

                InstanceObject inst => new FluenceStructAccessor(inst, interpreter),

                _ => val.ObjectReference
            },

            _ => null,
        };

        /// <summary>
        /// Configures the library sandbox with a specific set of allowed and disallowed standard libraries.
        /// This method first clears both lists before applying the new rules.
        /// </summary>
        /// <param name="allowed">An optional collection of library names to add to the whitelist.</param>
        /// <param name="disallowed">An optional collection of library names to add to the blacklist.</param>
        public void ConfigureLibrarySandbox(IEnumerable<string>? allowed = null, IEnumerable<string>? disallowed = null)
        {
            AllowedLibraries.Clear();
            DisallowedLibraries.Clear();

            if (allowed != null)
            {
                foreach (string lib in allowed) AllowedLibraries.Add(lib);
            }

            if (disallowed != null)
            {
                foreach (string lib in disallowed) DisallowedLibraries.Add(lib);
            }
        }

        /// <summary>
        /// Compiles a Fluence source code script into executable bytecode.
        /// This must be called before any of the Run methods.
        /// </summary>
        /// <param name="sourceCode">The Fluence script to compile.</param>
        /// <param name="partialCode">Whether to allow compilation of partial code, that is code without functions, or Main entry point.</param>
        /// <returns>True if compilation was successful, false otherwise.</returns>
        public bool Compile(string sourceCode, bool partialCode = false)
        {
            try
            {
                FluenceLexer lexer = new FluenceLexer(sourceCode);
                FluenceParser parser = new FluenceParser(lexer, _vmConfiguration, OnOutputLine, OnOutput, OnInput, OnErrorOutput);
                _intrinsicsInstance = parser.Intrinsics;
                _intrinsicsInstance.RegisterCustomIntrinsics(_customLibraries);
                parser.Parse(partialCode);

                if (_vmConfiguration.ExecutionEndPoint == VirtualMachineConfiguration.ExecutionPipelineEndpoint.StopAtLexer)
                {
#if DEBUG
                    parser.Lexer.DumpTokenStream("Token Stream [StopAtLexer]", OnOutputLine);
#endif
                    return true;
                }

#if DEBUG
                FluenceDebug.DumpSymbolTables(parser.CurrentParseState, OnOutputLine);
#endif

                _byteCode = parser.CompiledCode;
                _parseState = parser.CurrentParseState;
                if (_vm == null || _vm.State == FluenceVMState.NotStarted)
                {
                    _vm = new FluenceVirtualMachine(_byteCode, _vmConfiguration, _parseState, OnOutput, OnOutputLine, OnInput);
                }
                return true;
            }
            catch (FluenceException ex)
            {
                ConstructAndThrowException(ex);
                return false;
            }
        }

        /// <summary>
        /// Compiles a Fluence project from a directory with .fl code scripts into executable bytecode.
        /// This must be called before any of the Run methods.
        /// </summary>
        /// <param name="sourceCode">The Fluence script to compile.</param>
        /// <param name="partialCode">Whether to allow compilation of partial code, that is code without functions, or Main entry point.</param>
        /// <returns>True if compilation was successful, false otherwise.</returns>
        public bool CompileProject(string rootDir, bool partialCode = false)
        {
            try
            {
                FluenceParser parser = new FluenceParser(rootDir, _vmConfiguration, OnOutputLine, OnOutput, OnInput, OnErrorOutput);
                _intrinsicsInstance = parser.Intrinsics;
                _intrinsicsInstance.RegisterCustomIntrinsics(_customLibraries);
                parser.Parse(partialCode);

#if DEBUG
                FluenceDebug.DumpSymbolTables(parser.CurrentParseState, OnOutputLine);
#endif
                _byteCode = parser.CompiledCode;
                _parseState = parser.CurrentParseState;
                if (_vm == null || _vm.State == FluenceVMState.NotStarted)
                {
                    _vm = new FluenceVirtualMachine(_byteCode, _vmConfiguration, _parseState, OnOutput, OnOutputLine, OnInput);
                }
                return true;
            }
            catch (FluenceException ex)
            {
                ConstructAndThrowException(ex);
                return false;
            }
        }

        /// <summary>
        /// Runs the compiled script to completion.
        /// If the script was previously paused, execution will resume and run to completion.
        /// If the script was finished, it will be reset and run again from the beginning.
        /// </summary>
        /// <exception cref="FluenceException">Thrown if no code has been compiled.</exception>
        public void RunUntilDone() => RunFor(TimeSpan.MaxValue);

        /// <summary>
        /// Runs the compiled script for a given number of seconds.
        /// If the script was previously paused, execution will resume and run for the given time.
        /// If the script was finished, it will be reset and run again from the beginning.
        /// </summary>
        /// <exception cref="FluenceException">Thrown if no code has been compiled.</exception>
        public void RunForSeconds(int seconds) => RunFor(TimeSpan.FromSeconds(seconds));

        /// <summary>
        /// Runs the compiled script for a given number of milliseconds.
        /// If the script was previously paused, execution will resume and run for the given time.
        /// If the script was finished, it will be reset and run again from the beginning.
        /// </summary>
        /// <exception cref="FluenceException">Thrown if no code has been compiled.</exception>
        public void RunForMilliseconds(double milliseconds) => RunFor(TimeSpan.FromMilliseconds(milliseconds));

        /// <summary>
        /// Runs or resumes the compiled script for a specified maximum duration.
        /// If the duration is reached before the script finishes, the VM state will be 'Paused'.
        /// </summary>
        /// <param name="duration">The maximum time to run before pausing.</param>
        /// <exception cref="FluenceException">Thrown if no code has been compiled.</exception>
        public void RunFor(TimeSpan duration)
        {
            if (_vmConfiguration.ExecutionEndPoint == VirtualMachineConfiguration.ExecutionPipelineEndpoint.StopAtLexer)
            {
                return;
            }

            if (_byteCode == null)
            {
                throw new FluenceException("Code must be compiled successfully before it can be run.");
            }

            if (_vmConfiguration.ExecutionEndPoint == VirtualMachineConfiguration.ExecutionPipelineEndpoint.StopAtParser)
            {
                return;
            }

            try
            {
                if (_vm.State is FluenceVMState.Finished or FluenceVMState.Error)
                {
                    _vm = new FluenceVirtualMachine(_byteCode, _vmConfiguration, _parseState, OnOutput, OnOutputLine, OnInput);
                }

                _vm.SetIntrinsicLibraryWhiteAndBlackLists(AllowedLibraries, DisallowedLibraries);
                _vm.RunFor(duration);
#if DEBUG
                _vm.DumpPerformanceProfile();
#endif
            }
            catch (FluenceException ex)
            {
                ConstructAndThrowException(ex);
                _vm.Stop();
            }
        }

        private void EnsureVMInitialized()
        {
            if (_vm == null || _vm.State == FluenceVMState.Error)
            {
                _vm = new FluenceVirtualMachine(_byteCode, Configuration, _parseState, OnOutput, OnOutputLine, OnInput);
                return;
            }

            if (_vm.State == FluenceVMState.Finished)
            {
                if (Configuration.ExecutionMode == FluenceExecutionMode.Stateless)
                {
                    _vm = new FluenceVirtualMachine(_byteCode, Configuration, _parseState, OnOutput, OnOutputLine, OnInput);
                }
                else
                {
                    // Do nothing.
                }
            }
        }

        /// <summary>
        /// Initializes the Virtual Machine's global state without executing the Main function.
        /// This allows the host to set globals or inspect initial state before running the script.
        /// </summary>
        public void InitializeGlobals()
        {
            if (_byteCode == null) ConstructAndThrowException(new FluenceException("The code must be compiled successfully before the global state can be initialized."));

            EnsureVMInitialized();
            _vm.InitializeGlobals();
        }

        /// <summary>
        /// Forcibly resets the Virtual Machine, clearing all global state and resetting the instruction pointer to 0.
        /// Use this if you are in <see cref="FluenceExecutionMode.Persistent"/> mode but want to restart the script from scratch.
        /// </summary>
        public void Restart()
        {
            if (_byteCode == null) return;
            _vm = new FluenceVirtualMachine(_byteCode, Configuration, _parseState, OnOutput, OnOutputLine, OnInput);
        }

        /// <summary>
        /// Resets the interpreter, clearing the compiled bytecode and the virtual machine instance.
        /// The interpreter must be re-initialized.
        /// </summary>
        public void Reset()
        {
            _vm = null!;
            _parseState = null!;
            _byteCode = null!;
        }

        /// <summary>
        /// Signals the running script to pause execution at the next available opportunity (before the next instruction).
        /// </summary>
        public void Stop() => _vm.Stop();

        /// <summary>
        /// Gets the value of a global variable from the VM.
        /// This is how the script can pass data back to the host application.
        /// Returns Null if no such variable is found.
        /// </summary>
        /// <param name="name">The name of the global variable.</param>
        /// <returns>The value of the variable, or null if not found.</returns>
        public object? GetGlobal(string name)
        {
            if (_vm != null && _vm.TryGetGlobalVariable(name, out RuntimeValue val))
            {
                return ConvertToObject(val, this);
            }
            return null;
        }

        /// <summary>
        /// Attempts to get the value of a global variable from the VM.
        /// Unlike <see cref="GetGlobal"/>, this method distinguishes between a variable
        /// that does not exist and a variable whose value is <c>nil</c>.
        /// </summary>
        /// <param name="name">The name of the global variable.</param>
        /// <param name="value">
        /// When this method returns <see langword="true"/>, contains the value of the variable,
        /// which may be <see langword="null"/> if the Fluence variable holds <c>nil</c>.
        /// When this method returns <see langword="false"/>, contains <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the variable exists in the global scope;
        /// <see langword="false"/> if no variable with that name was found.
        /// </returns>
        public bool TryGetGlobal(string name, out object? value)
        {
            if (_vm != null && _vm.TryGetGlobalVariable(name, out RuntimeValue val))
            {
                value = ConvertToObject(val, this);
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Sets a global variable in the VM's global scope.
        /// This is how the host application can pass data into the script.
        /// </summary>
        /// <param name="name">The name of the variable.</param>
        /// <param name="value">The value to set (can be a primitive like int, double, string, or bool).</param>
        public void SetGlobal(string name, object value) => _vm.SetGlobal(name, value);

        /// <summary>
        /// Handles the formatting and display of runtime exceptions.
        /// </summary>
        private static void ConstructAndThrowException(FluenceException ex) => throw ex;
    }
}