using Fluence.Unity.Exceptions;
using Fluence.Unity.Extensions;
using Fluence.Unity.Global;
using Fluence.Unity.RuntimeTypes;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceByteCode.InstructionLine;
using static Fluence.Unity.FluenceInterpreter;
using static Fluence.Unity.FluenceParser;
using static Fluence.Unity.VirtualMachine.InlineCacheManager;

namespace Fluence.Unity.VirtualMachine
{
    /// <summary>
    /// The core execution engine for Fluence bytecode. It manages the call stack, the instruction pointer,
    /// the memory (registers and globals), and the main execution loop.
    /// </summary>
    internal sealed class FluenceVirtualMachine : INativeVmContext
    {
        /// <summary>A delegate for a natively implemented C# function that can be called from Fluence.</summary>
        internal delegate RuntimeValue IntrinsicRuntimeMethod(FluenceVirtualMachine vm, RuntimeValue self);

        /// <summary>A delegate representing the handler for a specific VM instruction.</summary>
        private delegate void OpcodeHandler(InstructionLine instruction);

        /// <summary>A performance-critical dispatch table that maps an instruction's opcode to its handler method.</summary>
        private readonly OpcodeHandler[] _dispatchTable;

        /// <summary>The immutable list of bytecode instructions to be executed.</summary>
        private readonly List<InstructionLine> _byteCode;

        /// <summary>The call stack, containing a <see cref="CallFrame"/> for each active function call.</summary>
        private Stack<CallFrame> _callStack;

        /// <summary>A direct, cached reference to the registers of the currently executing function's call frame.</summary>
        private RuntimeValue[] _cachedRegisters;

        /// <summary>A direct, cached reference to the writeable cache of the currently executing function's call frame.</summary>
        private bool[] _cachedWritableCache;

        /// <summary>A dictionary holding all global variables.</summary>
        private readonly RuntimeValue[] _globals;

        private CoroutineObject _activeCoroutine;
        private readonly CoroutineObject _rootCoroutine;

        /// <summary>
        /// A dictionary of all global variable values in the bytecode by their names.
        /// </summary>
        private readonly Dictionary<string, VariableValue> _globalVariableRegister = new Dictionary<string, VariableValue>();

        /// <summary>The top-level global scope, used for resolving global functions and variables.</summary>
        private readonly FluenceScope _globalScope;

        /// <summary>The stack used for passing arguments to functions and for temporary operand storage.</summary>
        private Stack<RuntimeValue> _operandStack;

        /// <summary>
        /// The stack used for the tracking of try-catch blocks, if the stack is empty and an error occurs, the vm will crash regardless if the error can be caught.
        /// </summary>
        private Stack<TryCatchValue> _tryCatchBlocks;

        /// <summary>
        /// A collection of the standard library names that are permitted to be loaded by a script.
        /// If this set is empty, all standard libraries are allowed. If it is populated, only the
        /// libraries whose names are in this set can be imported via the 'use' statement.
        /// This acts as a security whitelist for sandboxing script execution.
        /// </summary>
        private HashSet<string> _allowedIntrinsicLibraries = new HashSet<string>();

        /// <summary>
        /// A collection of the standard library names that are not permitted to be loaded by the script.
        /// libraries whose names are in this set can not be imported via the 'use' statement.
        /// This acts as a security blacklist for sandboxing script execution.
        /// </summary>
        private HashSet<string> _disallowedIntrinsicLibraries = new HashSet<string>();

        /// <summary> A pool of CallFrame objects to reuse. </summary>
        private readonly ObjectPool<CallFrame> _callFramePool;

        /// <summary> A pool of IteratorObject-s to reuse. </summary>
        private readonly ObjectPool<IteratorObject> _iteratorObjectPool;

        /// <summary> A pool of CharObject-s objects to reuse. </summary>
        private readonly ObjectPool<CharObject> _charObjectPool;

        /// <summary> A pool of FunctionObject-s objects to reuse. </summary>
        private readonly ObjectPool<FunctionObject> _functionObjectPool;

        /// <summary> A pool of StringObject-s objects to reuse. </summary>
        private readonly ObjectPool<StringObject> _stringObjectPool;

        /// <summary>  A pool of RangeObject-s objects to reuse. </summary>
        private readonly ObjectPool<RangeObject> _rangeObjectPool;

        private readonly FluenceParser _parser;

        /// <summary>
        /// The interval representing the amount of instructions per which executed we check the
        /// elapsed time since the start of the Virtual Machine.
        /// </summary>
        private int _timeCheckInterval = 100000;

        /// <summary>
        /// A cache to store the readonly status of variables in the global scope.
        /// </summary>
        private readonly bool[] _globalWritableCache;

        /// <summary> The list of all namespaces in the source code. </summary>
        private readonly Dictionary<int, FluenceScope> Namespaces;

        /// <summary>The Instruction Pointer, which holds the address of the *next* instruction to be executed.</summary>
        private int _ip;

        /// <summary>
        /// A flag that, when set to true, will cause the execution loop to pause at the next instruction.
        /// </summary>
        private bool _stopRequested;

        private bool _globalsInitialized;

        /// <summary>The delegate method used for non-newline output.</summary>
        private readonly TextOutputMethod _output;

        /// <summary>The delegate method used for line-based output.</summary>
        private readonly TextOutputMethod _outputLine;

        /// <summary>The delegate method used to receive input.</summary>
        private readonly TextInputMethod _input;

        /// <summary>
        /// The current instance of the <see cref="VirtualMachineConfiguration"/> given by the interpreter.
        /// </summary>
        private readonly VirtualMachineConfiguration _configuration;

        /// <summary>Gets the currently active call frame from the top of the call stack.</summary>
        internal CallFrame CurrentFrame => _callStack.Peek();

        /// <summary>Gets the registers for the current call frame.</summary>
        internal RuntimeValue[] CurrentRegisters => _cachedRegisters;

        /// <summary>Gets the global registers of the current vm run.</summary>
        internal RuntimeValue[] GlobalRegisters => _globals;

        internal int CurrentInstructionPointer => _ip;

        /// <summary>
        /// The current state of the Virtual Machine.
        /// </summary>
        public FluenceVMState State { get; private set; } = FluenceVMState.NotStarted;

#if DEBUG
        //
        //      Debug fields, useful for measuring performance, but for release builds only a waste of memory and performance.
        //

        private readonly Dictionary<InstructionCode, long> _instructionTimings = new();
        private readonly Dictionary<InstructionCode, long> _instructionCounts = new();

        /// <summary>
        /// A debug stopwatch measuring the approximate time the Virtual Machine has been running for.
        /// </summary>
        private readonly Stopwatch _stopwatch = new();

        /// <summary>
        /// Dumps a detailed performance profile to the console, showing instruction counts,
        /// total time spent, and average time per instruction.
        /// </summary>
        internal void DumpPerformanceProfile()
        {
            _outputLine("\n--- FLUENCE VM EXECUTION PROFILE ---");

            if (_instructionCounts.Count == 0)
            {
                _outputLine("No instructions were executed or profiling was not enabled.");
                return;
            }

            long totalInstructions = 0;
            long totalTicks = 0;
            List<(InstructionCode OpCode, long Count, long Ticks)> profileData = new List<(InstructionCode OpCode, long Count, long Ticks)>();

            foreach (KeyValuePair<InstructionCode, long> kvp in _instructionCounts)
            {
                long ticks = _instructionTimings.GetValueOrDefault(kvp.Key, 0);
                profileData.Add((kvp.Key, kvp.Value, ticks));
                totalInstructions += kvp.Value;
                totalTicks += ticks;
            }

            _outputLine($"Total Instructions Executed: {totalInstructions:N0}");
            _outputLine($"Total Execution Time: {new TimeSpan(totalTicks).TotalMilliseconds:N3} ms\n");

            _outputLine($"{"OpCode",-25} | {"Count",-15} | {"% of Total",-12} | {"Total Time (ms)",-18} | {"% of Time",-12} | {"Avg. Ticks/Op",-15}");
            _outputLine(new string('-', 110));

            profileData.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));

            foreach ((InstructionCode OpCode, long Count, long Ticks) in profileData)
            {
                double percentOfTotalCount = (double)Count / totalInstructions * 100;
                double totalMs = new TimeSpan(Ticks).TotalMilliseconds;
                double percentOfTotalTime = (double)Ticks / totalTicks * 100;
                double avgTicksPerOp = (double)Ticks / Count;

                string opCodeStr = OpCode.ToString();
                string countStr = Count.ToString("N0");
                string percentCountStr = $"{percentOfTotalCount:F2}%";
                string totalMsStr = totalMs.ToString("N4");
                string percentTimeStr = $"{percentOfTotalTime:F2}%";
                string avgTicksStr = avgTicksPerOp.ToString("F2");

                _outputLine($"{opCodeStr,-25} | {countStr,-15} | {percentCountStr,-12} | {totalMsStr,-18} | {percentTimeStr,-12} | {avgTicksStr,-15}");
            }
            _outputLine(new string('-', 110));
            _outputLine(Environment.NewLine);
        }
#endif

        /// <summary>
        /// Initializes a new instance of the Fluence Virtual Machine.
        /// </summary>
        /// <param name="bytecode">The compiled bytecode to execute.</param>
        /// <param name="parseState">The final state from the parser, containing scope information.</param>
        /// <param name="output">The delegate to handle non-newline output.</param>
        /// <param name="outputLine">The delegate to handle line-based output.</param>
        /// <param name="input">The delegate to handle user input.</param>
        internal FluenceVirtualMachine(List<InstructionLine> bytecode, VirtualMachineConfiguration config, ParseState parseState, TextOutputMethod? output, TextOutputMethod? outputLine, TextInputMethod? input)
        {
            _callFramePool = new ObjectPool<CallFrame>();
            _iteratorObjectPool = new ObjectPool<IteratorObject>();
            _charObjectPool = new ObjectPool<CharObject>();
            _functionObjectPool = new ObjectPool<FunctionObject>();
            _stringObjectPool = new ObjectPool<StringObject>();
            _rangeObjectPool = new ObjectPool<RangeObject>();

            _parser = parseState.ParserInstance;
            _byteCode = bytecode;
            _globalScope = parseState.GlobalScope;

            _rootCoroutine = new CoroutineObject { State = CoroutineState.Running };
            _activeCoroutine = _rootCoroutine;

            _callStack = _rootCoroutine.CallStack;
            _operandStack = _rootCoroutine.OperandStack;
            _tryCatchBlocks = _rootCoroutine.TryCatchBlocks;

            _output = output ?? Console.Write;
            _outputLine = outputLine ?? Console.WriteLine;

            _configuration = config;

            _input = input ?? Console.ReadLine!;

            // This represents the top-level global execution context.
            FunctionObject mainScriptFunc = new FunctionObject("<script>", 0, null!, 0, _globalScope);

            int maxOpCode = (int)InstructionCode.NumberOfOpcodes;

            int globalRegisterSlotCount = PrepareGlobalRegistry();

            mainScriptFunc.TotalRegisterSlots = globalRegisterSlotCount + 1;

            CallFrame initialFrame = new CallFrame();
            initialFrame.Initialize(this, mainScriptFunc, _byteCode.Count, null!);

            _callStack.Push(initialFrame);

            _cachedRegisters = initialFrame.Registers;

            _globals = new RuntimeValue[globalRegisterSlotCount];
            _globalWritableCache = new bool[globalRegisterSlotCount];

#if DEBUG
            FluenceDebug.DumpByteCodeInstructions(_parser.CompiledCode, _outputLine);
            FluenceDebug.GenerateCSharpCodeForInstructionList(_parser.CurrentParseState.CodeInstructions, _outputLine);
#endif

            _dispatchTable = new OpcodeHandler[maxOpCode + 1];

            _dispatchTable[(int)InstructionCode.Assign] = ExecuteAssign;
            _dispatchTable[(int)InstructionCode.AssignIfNil] = ExecuteAssignIfNil;
            _dispatchTable[(int)InstructionCode.Add] = ExecuteAdd;
            _dispatchTable[(int)InstructionCode.Subtract] = ExecuteSubtraction;
            _dispatchTable[(int)InstructionCode.Multiply] = ExecuteMultiplication;
            _dispatchTable[(int)InstructionCode.Divide] = ExecuteDivision;
            _dispatchTable[(int)InstructionCode.Modulo] = ExecuteModulo;
            _dispatchTable[(int)InstructionCode.Power] = ExecutePower;
            _dispatchTable[(int)InstructionCode.Negate] = ExecuteNegate;
            _dispatchTable[(int)InstructionCode.Not] = ExecuteNot;
            _dispatchTable[(int)InstructionCode.CallFunction] = ExecuteCallFunction;
            _dispatchTable[(int)InstructionCode.Return] = ExecuteReturn;
            _dispatchTable[(int)InstructionCode.NewInstance] = ExecuteNewInstance;
            _dispatchTable[(int)InstructionCode.SetField] = ExecuteSetField;
            _dispatchTable[(int)InstructionCode.GetField] = ExecuteGetField;
            _dispatchTable[(int)InstructionCode.CallMethod] = ExecuteCallMethod;
            _dispatchTable[(int)InstructionCode.NewList] = ExecuteNewList;
            _dispatchTable[(int)InstructionCode.NewDictionary] = ExecuteNewDictionary;
            _dispatchTable[(int)InstructionCode.PushElement] = ExecutePushElement;
            _dispatchTable[(int)InstructionCode.PushKeyValuePair] = ExecutePushKeyValuePair;
            _dispatchTable[(int)InstructionCode.GetElement] = ExecuteGetElement;
            _dispatchTable[(int)InstructionCode.SetElement] = ExecuteSetElement;
            _dispatchTable[(int)InstructionCode.NewRange] = ExecuteNewRange;
            _dispatchTable[(int)InstructionCode.GetLength] = ExecuteGetLength;
            _dispatchTable[(int)InstructionCode.ToString] = ExecuteToString;

            // Inlined directly in Run function.
            _dispatchTable[(int)InstructionCode.Goto] = ExecuteGoto;

            _dispatchTable[(int)InstructionCode.NewIterator] = ExecuteNewIterator;
            _dispatchTable[(int)InstructionCode.IterNext] = ExecuteIterNext;
            _dispatchTable[(int)InstructionCode.PushParam] = ExecutePushParam;

            _dispatchTable[(int)InstructionCode.CallStatic] = ExecuteCallStatic;
            _dispatchTable[(int)InstructionCode.GetStatic] = ExecuteGetStatic;
            _dispatchTable[(int)InstructionCode.SetStatic] = ExecuteSetStatic;

            _dispatchTable[(int)InstructionCode.BitwiseNot] = ExecuteBitwiseOperation;
            _dispatchTable[(int)InstructionCode.BitwiseAnd] = ExecuteBitwiseOperation;
            _dispatchTable[(int)InstructionCode.BitwiseOr] = ExecuteBitwiseOperation;
            _dispatchTable[(int)InstructionCode.BitwiseXor] = ExecuteBitwiseOperation;
            _dispatchTable[(int)InstructionCode.BitwiseLShift] = ExecuteBitwiseOperation;
            _dispatchTable[(int)InstructionCode.BitwiseRShift] = ExecuteBitwiseOperation;

            _dispatchTable[(int)InstructionCode.GreaterThan] = ExecuteGreaterThan;
            _dispatchTable[(int)InstructionCode.GreaterEqual] = ExecuteGreaterEqual;
            _dispatchTable[(int)InstructionCode.LessThan] = ExecuteLessThan;
            _dispatchTable[(int)InstructionCode.LessEqual] = ExecuteLessEqual;

            _dispatchTable[(int)InstructionCode.Equal] = (inst) => ExecuteEqualityComparison(inst, true);
            _dispatchTable[(int)InstructionCode.NotEqual] = (inst) => ExecuteEqualityComparison(inst, false);

            _dispatchTable[(int)InstructionCode.And] = (inst) => ExecuteLogicalOp(inst, true);
            _dispatchTable[(int)InstructionCode.Or] = (inst) => ExecuteLogicalOp(inst, false);

            _dispatchTable[(int)InstructionCode.GotoIfFalse] = (inst) => ExecuteGotoIf(inst, false);
            _dispatchTable[(int)InstructionCode.GotoIfTrue] = (inst) => ExecuteGotoIf(inst, true);

            _dispatchTable[(int)InstructionCode.NewCoroutine] = ExecuteNewCoroutine;
            _dispatchTable[(int)InstructionCode.Resume] = ExecuteResume;
            _dispatchTable[(int)InstructionCode.Yield] = ExecuteYield;

            _dispatchTable[(int)InstructionCode.NewLambda] = ExecuteNewLambda;
            _dispatchTable[(int)InstructionCode.IncrementIntUnrestricted] = ExecuteIncrementIntUnrestricted;
            _dispatchTable[(int)InstructionCode.LoadAddress] = ExecuteLoadAddress;

            _dispatchTable[(int)InstructionCode.GetType] = ExecuteGetType;
            _dispatchTable[(int)InstructionCode.IsType] = ExecuteIsType;
            _dispatchTable[(int)InstructionCode.Throw] = ExecuteThrow;

            _dispatchTable[(int)InstructionCode.TryBlock] = ExecuteTryBlock;
            _dispatchTable[(int)InstructionCode.CatchBlock] = ExecuteCatchBlock;

            //      ==!!==
            //      The following are unique opCodes generated only by the FluenceOptimizer.
            //      Some of these perfectly map to existing functions.
            //

            _dispatchTable[(int)InstructionCode.AddAssign] = ExecuteAdd;
            _dispatchTable[(int)InstructionCode.MulAssign] = ExecuteMultiplication;
            _dispatchTable[(int)InstructionCode.DivAssign] = ExecuteDivision;
            _dispatchTable[(int)InstructionCode.ModAssign] = ExecuteModulo;
            _dispatchTable[(int)InstructionCode.SubAssign] = ExecuteSubtraction;

            _dispatchTable[(int)InstructionCode.Increment] = ExecuteIncrement;
            _dispatchTable[(int)InstructionCode.Decrement] = ExecuteDecrement;

            _dispatchTable[(int)InstructionCode.AssignTwo] = ExecuteAssignTwo;

            _dispatchTable[(int)InstructionCode.PushTwoParams] = ExecutePushTwoParam;
            _dispatchTable[(int)InstructionCode.PushThreeParams] = ExecutePushThreeParam;
            _dispatchTable[(int)InstructionCode.PushFourParams] = ExecutePushFourParam;

            _dispatchTable[(int)InstructionCode.BranchIfEqual] = (inst) => ExecuteBranchIfEqual(inst, true);
            _dispatchTable[(int)InstructionCode.BranchIfNotEqual] = (inst) => ExecuteBranchIfEqual(inst, false);

            _dispatchTable[(int)InstructionCode.Skip] = ExecuteSkip;

            _dispatchTable[(int)InstructionCode.BranchIfGreaterThan] = ExecuteBranchIfGreaterThan;
            _dispatchTable[(int)InstructionCode.BranchIfGreaterOrEqual] = ExecuteBranchIfGreaterOrEqual;
            _dispatchTable[(int)InstructionCode.BranchIfLessThan] = ExecuteBranchIfLessThan;
            _dispatchTable[(int)InstructionCode.BranchIfLessOrEqual] = ExecuteBranchIfLessOrEqual;

            // Simple case for Terminate.
            _dispatchTable[(int)InstructionCode.Terminate] = (inst) => _ip = _byteCode.Count;

            Namespaces = parseState.NameSpaces;
        }

        public void SetElapsedTimeCheckInterval(int interval) => _timeCheckInterval = interval;

        private static bool IsAGlobalVariable(Value val, out VariableValue variable)
        {
            if (val is VariableValue var && var.IsGlobal)
            {
                variable = var;
                return true;
            }

            variable = null!;
            return false;
        }

        private void RegisterVariableToGlobalRegister(VariableValue variable, HashSet<string> globalVars, ref int registerIndex)
        {
            globalVars.Add(variable.Name);
            variable.IsGlobal = true;
            variable.RegisterIndex = registerIndex;
            registerIndex++;

            // We store the global variables to allow the access and setting of their values by the interpreter.
            _globalVariableRegister.Add(variable.Name, variable);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SaveStateToActiveCoroutine()
        {
            _activeCoroutine.InstructionPointer = _ip;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RestoreStateFromCoroutine(CoroutineObject coro)
        {
            _activeCoroutine = coro;

            _callStack = coro.CallStack;
            _operandStack = coro.OperandStack;
            _tryCatchBlocks = coro.TryCatchBlocks;
            _ip = coro.InstructionPointer;

            if (_callStack.Count > 0)
            {
                CallFrame topFrame = _callStack.Peek();
                _cachedRegisters = topFrame.Registers;
                _cachedWritableCache = topFrame.WritableCache;
            }
        }

        /// <summary>
        /// Calculates the total amount of register slots the global frame requires to store its
        /// global variables and patches the indecies of its global variables.
        /// </summary>
        /// <returns>The total number of unique global variable slots required.</returns>
        private int PrepareGlobalRegistry()
        {
            HashSet<string> globalVars = new HashSet<string>();
            int index = 0;
            int startIndex = 0;

            for (int i = 0; i < _byteCode.Count; i++)
            {
                InstructionLine insn = _byteCode[i];

                // Any variables defined inside functions with the keyword "root" become global, but since they are not defined outside functions
                // They are not present in the global section after the "SectionGlobal" indicator, we have to go through all the instructions and pre-register them.

                if (insn.Lhs is not null && IsAGlobalVariable(insn.Lhs, out VariableValue variable) && !globalVars.Contains(variable.Name))
                {
                    RegisterVariableToGlobalRegister(variable, globalVars, ref index);
                }
                if (insn.Rhs is not null && IsAGlobalVariable(insn.Rhs, out VariableValue variable2) && !globalVars.Contains(variable2.Name))
                {
                    RegisterVariableToGlobalRegister(variable2, globalVars, ref index);
                }
                if (insn.Rhs2 is not null && IsAGlobalVariable(insn.Rhs2, out VariableValue variable3) && !globalVars.Contains(variable3.Name))
                {
                    RegisterVariableToGlobalRegister(variable3, globalVars, ref index);
                }
                if (insn.Rhs3 is not null && IsAGlobalVariable(insn.Rhs3, out VariableValue variable4) && !globalVars.Contains(variable4.Name))
                {
                    RegisterVariableToGlobalRegister(variable4, globalVars, ref index);
                }

                if (_byteCode[i].Instruction is InstructionCode.SectionGlobal or InstructionCode.Skip)
                {
                    startIndex = i;
                    break;
                }
            }

            // We no longer need the indicator.
            _byteCode[startIndex].Instruction = InstructionCode.Skip;

            for (int i = startIndex; i < _byteCode.Count; i++)
            {
                InstructionLine insn = _byteCode[i];
                if ((insn.Instruction == InstructionCode.Assign || insn.Instruction == InstructionCode.AssignIfNil) && insn.Lhs is VariableValue variable && !globalVars.Contains(variable.Name))
                {
                    globalVars.Add(variable.Name);
                    variable.IsGlobal = true;
                    variable.RegisterIndex = index;
                    index++;

                    // We store the global variables to allow the access and setting of their values by the interpreter.
                    _globalVariableRegister.Add(variable.Name, variable);
                }
                // In a few cases there are temporary registers in the global section, such as the mandatory call to the main function.
                // Or if any global variables call a function to get their value.
                else if (insn.Lhs is TempValue temp)
                {
                    temp.RegisterIndex = index;
                    index++;
                }
                else if (insn.Rhs is TempValue temp2)
                {
                    temp2.RegisterIndex = index;
                    index++;
                }
                else if (insn.Rhs2 is TempValue temp3)
                {
                    temp3.RegisterIndex = index;
                    index++;
                }
                else if (insn.Rhs3 is TempValue temp4)
                {
                    temp4.RegisterIndex = index;
                    index++;
                }
            }

            void PatchOperand(Value operand)
            {
                if (operand is VariableValue var && _globalVariableRegister.TryGetValue(var.Name, out VariableValue outVar))
                {
                    var.IsGlobal = true;
                    var.RegisterIndex = outVar.RegisterIndex;
                }
            }

            for (int i = 0; i < _byteCode.Count; i++)
            {
                InstructionLine insn = _byteCode[i];

                PatchOperand(insn.Lhs);
                PatchOperand(insn.Rhs);
                PatchOperand(insn.Rhs2);
                PatchOperand(insn.Rhs3);
            }

            return index;
        }

        /// <summary>
        /// Runs the initialization instructions (globals) up to the point where the Main function is called.
        /// </summary>
        internal void InitializeGlobals()
        {
            if (_globalsInitialized) return;

            while (_ip < _byteCode.Count)
            {
                InstructionLine instruction = _byteCode[_ip];

                if (instruction.Instruction == InstructionCode.CallFunction &&
                    instruction.Rhs is VariableValue var && var.Name.StartsWith("Main__", StringComparison.InvariantCulture))
                {
                    break;
                }

                if (instruction.Instruction == InstructionCode.Terminate)
                {
                    break;
                }

                _ip++;
                if (instruction.SpecializedHandler != null)
                {
                    instruction.SpecializedHandler(instruction, this);
                }
                else
                {
                    if (instruction.Instruction is InstructionCode.Goto)
                    {
                        _ip = ((GoToValue)instruction.Lhs).Address;
                        continue;
                    }
                    _dispatchTable[(int)instruction.Instruction](instruction);
                }
            }

            _globalsInitialized = true;
        }

        /// <summary>
        /// Tries to retrieve a global variable by name.
        /// </summary>
        /// <param name="name">The name of the global variable.</param>
        /// <param name="val">When this method returns, contains the value of the global variable, if found; otherwise, the default value (Nil).</param>
        /// <returns>True if the global variable was found; otherwise, false.</returns>
        internal bool TryGetGlobalVariable(string name, out RuntimeValue val)
        {
            if (_globalVariableRegister.TryGetValue(name, out VariableValue var))
            {
                val = _globals[var.RegisterIndex];
                return true;
            }

            val = RuntimeValue.Nil;
            return false;
        }

        /// <summary>
        /// Returns a reusable CallFrame insitance from the CallFrame pool or creates one if non are available.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CallFrame GetCallframe() => _callFramePool.Get();

        /// <summary>
        /// Sets a global variable in the VM's global scope, converting from a standard C# type.
        /// </summary>
        /// <param name="name">The name the variable will have in the script.</param>
        /// <param name="value">The C# object to convert and assign. Supported types are:
        /// null, bool, int, long, float, double, string, list, dictionary and char.
        /// </param>
        public void SetGlobal(string name, object? value)
        {
            if (!_globalVariableRegister.TryGetValue(name, out VariableValue var))
            {
                throw new FluenceException($"Invalid global variable name: '{name}'. Unable to set new value. Is the name correct and defined as a global in the script?");
            }

            RuntimeValue runtimeValue = ConvertToRuntimeValue(value, this);

            AssignVariable(var, runtimeValue, null!);
        }

        /// <summary>
        /// Sets the whitelist and the blacklist of standard libraries.
        /// </summary>
        internal void SetIntrinsicLibraryWhiteAndBlackLists(HashSet<string> whiteList, HashSet<string> blackList)
        {
            _allowedIntrinsicLibraries = whiteList;
            _disallowedIntrinsicLibraries = blackList;
        }

        /// <summary>
        /// Runs the loaded bytecode for a specified duration.
        /// The main execution loop of the virtual machine.
        /// </summary>
        /// <param name="duration">The maximum time to run before pausing.</param>
        internal void RunFor(TimeSpan duration)
        {
            if (State is FluenceVMState.Finished or FluenceVMState.Error) return;

            _stopRequested = false;
            State = FluenceVMState.Running;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int instructionsUntilNextCheck = _timeCheckInterval;
            bool willRunUntilDone = duration == TimeSpan.MaxValue;

            int instructionCount = _byteCode.Count;
            bool timeOutEnabled = true;

            if (willRunUntilDone && _configuration.ExecutionTimeout == TimeSpan.MaxValue)
            {
                stopwatch.Stop();
                timeOutEnabled = false;
            }

            while (_ip < instructionCount)
            {
                if (_stopRequested)
                {
                    State = FluenceVMState.Paused;
                    return;
                }

                // If the VM is set to run until completion, we can save a lot of time on just not doing time elapsed checks.
                if (!willRunUntilDone || timeOutEnabled)
                {
                    instructionsUntilNextCheck--;

                    if (instructionsUntilNextCheck == 0)
                    {
                        instructionsUntilNextCheck = _timeCheckInterval;

                        if (stopwatch.Elapsed >= duration)
                        {
                            State = FluenceVMState.Paused;
                            return;
                        }
                    }
                }

                InstructionLine instruction = _byteCode[_ip];
                _ip++;

#if DEBUG
                _stopwatch.Restart();
#endif

                if (instruction.SpecializedHandler != null)
                {
                    instruction.SpecializedHandler(instruction, this);
                }
                else
                {
                    _dispatchTable[(int)instruction.Instruction](instruction);
                }

#if DEBUG
                _stopwatch.Stop();
                _instructionCounts.TryAdd(instruction.Instruction, 0);
                _instructionCounts[instruction.Instruction]++;
                _instructionTimings.TryAdd(instruction.Instruction, 0);
                _instructionTimings[instruction.Instruction] += _stopwatch.ElapsedTicks;
#endif
            }

            // If the loop finishes naturally, the script is done.
            State = FluenceVMState.Finished;
        }

        /// <summary>
        /// Signals the VM to stop execution at the next available opportunity.
        /// </summary>
        internal void Stop() => _stopRequested = true;

        /// <summary>
        /// Directly sets the instruction pointer. Used for debugging or advanced control.
        /// </summary>
        /// <param name="id">The address of the next instruction to execute.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetInstructionPointer(int id) => _ip = id;

        internal static void ExecuteSkip(InstructionLine _) { }

        /// <summary>Handles the ADD instruction, which performs numeric addition, string concatenation, or list concatenation.</summary>
        private void ExecuteAdd(InstructionLine instruction)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            if (left.Type == RuntimeValueType.Number && right.Type == RuntimeValueType.Number)
            {
                SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedAddHandler(instruction, this, left, right);
                if (handler != null)
                {
                    instruction.SpecializedHandler = handler;
                    handler(instruction, this);
                    return;
                }

                // Fallback.
                SetVariableOrRegister(instruction.Lhs, new RuntimeValue(left.IntValue + right.IntValue), instruction);
                return;
            }

            // String concatenation.
            if ((left.Type == RuntimeValueType.Object && left.Is<StringObject>()) ||
                (right.Type == RuntimeValueType.Object && right.Is<StringObject>()))
            {
                SetVariableOrRegister(instruction.Lhs, ResolveStringObjectRuntimeValue(string.Concat(left.ToString(), right.ToString())), instruction);
                return;
            }

            if (left.Type == RuntimeValueType.Object && left.ObjectReference is ListObject leftList &&
                right.Type == RuntimeValueType.Object && right.ObjectReference is ListObject rightList)
            {
                ListObject concatenatedList = new ListObject();

                concatenatedList.Elements.AddRange(leftList.Elements);
                concatenatedList.Elements.AddRange(rightList.Elements);

                SetVariableOrRegister(instruction.Lhs, new RuntimeValue(concatenatedList), instruction);
                return;
            }

            SignalError($"Runtime Error: Cannot apply operator '+' to types {GetDetailedTypeName(left)} and {GetDetailedTypeName(right)}.");
        }

        private void ExecuteIncrementIntUnrestricted(InstructionLine instruction)
        {
            SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedIncrementIntUnrestrictedHandler(instruction, this);
            if (handler != null)
            {
                instruction.SpecializedHandler = handler;
                handler(instruction, this);
                return;
            }

            if (instruction.Lhs is TempValue temp)
            {
                SetRegister(temp, new RuntimeValue(GetRuntimeValue(temp, instruction).IntValue + 1));
                return;
            }

            VariableValue var = (VariableValue)instruction.Lhs;
            RuntimeValue result = new RuntimeValue(GetRuntimeValue(var, instruction).IntValue + 1);
            SetVariable(var, result);
        }


        private void ExecuteNewCoroutine(InstructionLine instruction)
        {
            RuntimeValue functionVal = GetRuntimeValue(instruction.Rhs, instruction);

            if (functionVal.ObjectReference is not FunctionObject function)
            {
                SignalError($"Runtime Error: Attempted to create a coroutine from a non-function type.");
                return;
            }

            int argCount = GetRuntimeValue(instruction.Rhs2, instruction).IntValue;

            if (function.Arity != argCount && function.Arity != -100)
            {
                SignalError($"Runtime Error: Wrong number of arguments for coroutine '{function.Name}'. Expected {function.Arity}, got {argCount}.");
                return;
            }

            CoroutineObject newCoro = new CoroutineObject();
            CallFrame newFrame = _callFramePool.Get();

            // Return Address -1 acts as a sentinel to signify this is the root frame of a coroutine.
            newFrame.Initialize(this, function, -1, null!);

            int initialArgIndex = 0;
            for (int i = argCount - 1; i >= 0; i--)
            {
                int paramIndex = initialArgIndex + i;
                RuntimeValue argValue = _operandStack.Pop();
                newFrame.Registers[paramIndex] = argValue;
            }

            newCoro.CallStack.Push(newFrame);
            newCoro.InstructionPointer = function.StartAddress;

            SetVariableOrRegister(instruction.Lhs, new RuntimeValue(newCoro), instruction);
        }

        private void ExecuteResume(InstructionLine instruction)
        {
            RuntimeValue coroVal = GetRuntimeValue(instruction.Rhs, instruction);
            if (coroVal.ObjectReference is not CoroutineObject coroToResume)
            {
                SignalError("Runtime Error: Cannot resume a non-coroutine value.");
                return;
            }

            if (coroToResume.State == CoroutineState.Dead)
            {
                SetVariableOrRegister(instruction.Lhs, RuntimeValue.Nil, instruction);
                return;
            }

            RuntimeValue argToPass = GetRuntimeValue(instruction.Rhs2, instruction);

            coroToResume.Caller = _activeCoroutine;

            // Where the yielded result will go when it yields.
            coroToResume.ResumeTarget = instruction.Lhs;
            coroToResume.State = CoroutineState.Running;

            SaveStateToActiveCoroutine();
            RestoreStateFromCoroutine(coroToResume);

            if (coroToResume.YieldTarget is not null)
            {
                SetVariableOrRegister(coroToResume.YieldTarget, argToPass, instruction);
                coroToResume.YieldTarget = null;
            }
        }

        private void ExecuteYield(InstructionLine instruction)
        {
            if (_activeCoroutine.Caller == null)
            {
                SignalError("Runtime Error: Cannot yield from the root execution thread.");
                return;
            }

            RuntimeValue valueToYield = GetRuntimeValue(instruction.Rhs, instruction);

            CoroutineObject yieldingCoro = _activeCoroutine;
            CoroutineObject caller = yieldingCoro.Caller;

            yieldingCoro.YieldTarget = instruction.Lhs;
            yieldingCoro.State = CoroutineState.Suspended;
            yieldingCoro.Caller = null;

            SaveStateToActiveCoroutine();
            RestoreStateFromCoroutine(caller);

            if (yieldingCoro.ResumeTarget is not null)
            {
                SetVariableOrRegister(yieldingCoro.ResumeTarget, valueToYield, instruction);
                yieldingCoro.ResumeTarget = null;
            }
        }

        private void ExecuteLoadAddress(InstructionLine instruction)
        {
            ReferenceValue reference = (ReferenceValue)instruction.Lhs;
            _operandStack.Push(new RuntimeValue(reference));
        }

        /// <summary>Handles the SUBTRACT instruction for numeric subtraction or list difference.</summary>
        private void ExecuteSubtraction(InstructionLine instruction)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            if (left.Type == RuntimeValueType.Number && right.Type == RuntimeValueType.Number)
            {
                SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedSubtractionHandler(instruction, this, left, right);
                if (handler != null)
                {
                    instruction.SpecializedHandler = handler;
                    handler(instruction, this);
                    return;
                }

                // Fallback.
                SetVariableOrRegister(instruction.Lhs, new RuntimeValue(left.IntValue - right.IntValue), instruction);
                return;
            }

            if (left.Type == RuntimeValueType.Object && left.ObjectReference is ListObject leftList &&
                right.Type == RuntimeValueType.Object && right.ObjectReference is ListObject rightList)
            {
                ListObject concatenatedList = new ListObject();

                // This performs a set difference, which is the intuitive meaning of list subtraction.
                concatenatedList.Elements.AddRange(leftList.Elements.Except(rightList.Elements));
                SetRegister((TempValue)instruction.Lhs, new RuntimeValue(concatenatedList));
                return;
            }

            SignalError($"Runtime Error: Cannot apply operator '-' to types {GetDetailedTypeName(left)} and {GetDetailedTypeName(right)}.");
        }

        /// <summary>Handles the MULTIPLY instruction.</summary>
        private void ExecuteMultiplication(InstructionLine instruction)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            if (left.Type == RuntimeValueType.Number && right.Type == RuntimeValueType.Number)
            {
                SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedMulHandler(instruction, this, left, right);
                if (handler != null)
                {
                    instruction.SpecializedHandler = handler;
                    handler(instruction, this);
                    return;
                }

                // Fallback.
                SetVariableOrRegister(instruction.Lhs, new RuntimeValue(left.IntValue * right.IntValue), instruction);
                return;
            }

            if (left.ObjectReference is StringObject strLeft && right.Type == RuntimeValueType.Number)
            {
                SetVariableOrRegister(instruction.Lhs, HandleStringRepetition(strLeft, right), instruction);
                return;
            }
            if (left.Type == RuntimeValueType.Number && right.ObjectReference is StringObject strRight)
            {
                SetVariableOrRegister(instruction.Lhs, HandleStringRepetition(strRight, left), instruction);
                return;
            }

            if (left.ObjectReference is CharObject charLeft && right.Type == RuntimeValueType.Number)
            {
                SetVariableOrRegister(instruction.Lhs, HandleStringRepetition(new StringObject(charLeft.Value.ToString()), right), instruction);
                return;
            }
            if (left.Type == RuntimeValueType.Number && right.ObjectReference is CharObject charRight)
            {
                SetVariableOrRegister(instruction.Lhs, HandleStringRepetition(new StringObject(charRight.Value.ToString()), left), instruction);
                return;
            }

            if (left.ObjectReference is ListObject listLeft && right.Type == RuntimeValueType.Number)
            {
                SetVariableOrRegister(instruction.Lhs, HandleListRepetition(listLeft, right), instruction);
                return;
            }
            if (left.Type == RuntimeValueType.Number && right.ObjectReference is ListObject listRight)
            {
                SetVariableOrRegister(instruction.Lhs, HandleListRepetition(listRight, left), instruction);
                return;
            }

            SignalError($"Runtime Error: Cannot apply operator '*' to types {GetDetailedTypeName(left)} and {GetDetailedTypeName(right)}.");
        }

        /// <summary>Handles the DIVIDE instruction.</summary>
        internal void ExecuteDivision(InstructionLine instruction)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedDivHandler(instruction, this, left, right);
            if (handler != null)
            {
                instruction.SpecializedHandler = handler;
                handler(instruction, this);
                return;
            }

            SignalError($"Runtime Error: Cannot apply operator '/' to types {GetDetailedTypeName(left)} and {GetDetailedTypeName(right)}.");
        }

        /// <summary>Handles the MULTIPLY.</summary>
        private void ExecuteModulo(InstructionLine instruction)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedModuloHandler(instruction, this, left, right);
            if (handler != null)
            {
                instruction.SpecializedHandler = handler;
                handler(instruction, this);
                return;
            }

            SignalError($"Runtime Error: Cannot apply operator '%' to types {GetDetailedTypeName(left)} and {GetDetailedTypeName(right)}.");
        }

        /// <summary>Handles the POWER instruction.</summary>
        private void ExecutePower(InstructionLine instruction)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedPowerHandler(instruction, this, left, right);
            if (handler != null)
            {
                instruction.SpecializedHandler = handler;
                handler(instruction, this);
                return;
            }

            SignalError($"Runtime Error: Cannot apply operator '**' to types {GetDetailedTypeName(left)} and {GetDetailedTypeName(right)}.");
        }

        /// <summary>Handles the ASSIGN_TWO instruction, which is used for variable assignment of two variables at once.</summary>
        private void ExecuteAssignTwo(InstructionLine instruction)
        {
            if (!(instruction.Lhs is VariableValue && instruction.Rhs is RangeValue) && !(instruction.Rhs2 is VariableValue && instruction.Rhs3 is RangeValue))
            {
                if (instruction.SpecializedHandler == null)
                {
                    instruction.SpecializedHandler = CreateSpecializedAssignTwoHandler(instruction, this);

                    if (instruction.SpecializedHandler != null)
                    {
                        instruction.SpecializedHandler(instruction, this);
                        return;
                    }
                }
            }

            AssignTo(instruction.Lhs, instruction.Rhs, instruction);
            AssignTo(instruction.Rhs2, instruction.Rhs3, instruction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ExecuteGoto(InstructionLine insn)
        {
            _ip = ((GoToValue)insn.Lhs).Address;
        }

        /// <summary>Handles the ASSIGN_IF_NIL instruction, which is used for global variable initialization.</summary>
        private void ExecuteAssignIfNil(InstructionLine instruction)
        {
            if (_globals[((VariableValue)instruction.Lhs).RegisterIndex] == RuntimeValue.Nil)
            {
                AssignTo(instruction.Lhs, instruction.Rhs, instruction);
            }
        }

        /// <summary>Handles the ASSIGN instruction, which is used for variable assignment and range-to-list expansion.</summary>
        private void ExecuteAssign(InstructionLine instruction)
        {
            if (!(instruction.Lhs is VariableValue && instruction.Rhs is RangeValue))
            {
                instruction.SpecializedHandler = CreateSpecializedAssignHandler(instruction, this);

                if (instruction.SpecializedHandler != null)
                {
                    instruction.SpecializedHandler(instruction, this);
                    return;
                }
            }

            AssignTo(instruction.Lhs, instruction.Rhs, instruction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AssignTo(Value left, Value right, InstructionLine insn)
        {
            RuntimeValue sourceValue = GetRuntimeValue(right, insn);

            if (left is VariableValue destVar && sourceValue.ObjectReference is RangeObject range)
            {
                ListObject list = new ListObject();
                RuntimeValue startValue = range.Start;
                RuntimeValue endValue = range.End;

                if (startValue.Type != RuntimeValueType.Number || endValue.Type != RuntimeValueType.Number)
                {
                    SignalError($"Runtime Error: Range bounds must be numbers, not {GetDetailedTypeName(range.Start)} and {GetDetailedTypeName(range.End)}.");
                    return;
                }

                int start = startValue.IntValue;
                int end = endValue.IntValue;

                if (start <= end)
                {
                    for (int i = start; i <= end; i++)
                    {
                        list.Elements.Add(new RuntimeValue(i));
                    }
                }
                else // Decreasing range.
                {
                    for (int i = start; i >= end; i--)
                    {
                        list.Elements.Add(new RuntimeValue(i));
                    }
                }
                AssignVariable(destVar, new RuntimeValue(list), insn, destVar.IsReadOnly);
            }
            // Standard assignment.
            else if (left is VariableValue destVar2)
            {
                AssignVariable(destVar2, sourceValue, insn, destVar2.IsReadOnly);
            }
            else if (left is TempValue destTemp) SetRegister(destTemp, sourceValue);
            else throw ConstructRuntimeException("Internal VM Error: Destination of 'Assign' must be a variable or a temporary register.");
        }

        /// <summary>
        /// A unified handler for all bitwise operations (~, &amp;, |, ^, &lt;&lt;, &gt;&gt;).
        /// </summary>
        private void ExecuteBitwiseOperation(InstructionLine instruction)
        {
            RuntimeValue leftValue = GetRuntimeValue(instruction.Rhs, instruction);
            long leftLong = ToLong(leftValue);

            if (instruction.Instruction == InstructionCode.BitwiseNot)
            {
                SetRegister((TempValue)instruction.Lhs, new RuntimeValue(~leftLong));
                return;
            }

            RuntimeValue rightValue = GetRuntimeValue(instruction.Rhs2, instruction);

            switch (instruction.Instruction)
            {
                case InstructionCode.BitwiseRShift:
                    instruction.SpecializedHandler = CreateBitwiseRightShiftHandler(instruction, this, leftValue, rightValue);
                    break;
                case InstructionCode.BitwiseLShift:
                    instruction.SpecializedHandler = CreateBitwiseLeftShiftHandler(instruction, this, leftValue, rightValue);
                    break;
                case InstructionCode.BitwiseOr:
                    instruction.SpecializedHandler = CreateBitwiseOrHandler(instruction, this, leftValue, rightValue);
                    break;
                case InstructionCode.BitwiseXor:
                    instruction.SpecializedHandler = CreateBitwiseXorHandler(instruction, this, leftValue, rightValue);
                    break;
                case InstructionCode.BitwiseAnd:
                    instruction.SpecializedHandler = CreateBitwiseAndHandler(instruction, this, leftValue, rightValue);
                    break;
            }

            instruction.SpecializedHandler(instruction, this);
        }

        /// <summary>
        /// Handles the NEGATE instruction (unary minus).
        /// </summary>
        private void ExecuteNegate(InstructionLine instruction)
        {
            TempValue destination = (TempValue)instruction.Lhs;
            RuntimeValue value = GetRuntimeValue(instruction.Rhs, instruction);

            if (value.Type != RuntimeValueType.Number)
            {
                SignalError($"Runtime Error: The unary minus operator '-' cannot be applied to a value of type '{GetDetailedTypeName(value)}'.");
                return;
            }

            RuntimeValue result = value.NumberType switch
            {
                RuntimeNumberType.Int => new RuntimeValue(-value.IntValue),
                RuntimeNumberType.Double => new RuntimeValue(-value.DoubleValue),
                RuntimeNumberType.Float => new RuntimeValue(-value.FloatValue),
                RuntimeNumberType.Long => new RuntimeValue(-value.LongValue),
                _ => SignalRecoverableErrorAndReturnNil("Internal VM Error: Invalid number type for negate operation.")
            };

            SetRegister(destination, result);
        }

        /// <summary>
        /// Handles the INCREMENT instruction that increments a variable that is numeric.
        /// </summary>
        private void ExecuteIncrement(InstructionLine instruction)
        {
            instruction.SpecializedHandler = CreateSpecializedIncrementDecrementHandler(instruction, this, true);

            if (instruction.SpecializedHandler != null)
            {
                instruction.SpecializedHandler(instruction, this);
                return;
            }

            // Fallback?
            VariableValue var = (VariableValue)instruction.Lhs;
            AssignVariable(var, new RuntimeValue(_cachedRegisters[var.RegisterIndex].IntValue + 1), instruction, var.IsReadOnly);
        }

        /// <summary>
        /// Handles the DECREMENT instruction that increments a variable that is numeric.
        /// </summary>
        private void ExecuteDecrement(InstructionLine instruction)
        {
            instruction.SpecializedHandler = CreateSpecializedIncrementDecrementHandler(instruction, this, false);

            if (instruction.SpecializedHandler != null)
            {
                instruction.SpecializedHandler(instruction, this);
                return;
            }

            // Fallback?
            VariableValue var = (VariableValue)instruction.Lhs;
            AssignVariable(var, new RuntimeValue(_cachedRegisters[var.RegisterIndex].IntValue - 1), instruction, var.IsReadOnly);
        }

        /// <summary>
        /// Handles the NOT instruction (logical not).
        /// </summary>
        private void ExecuteNot(InstructionLine instruction)
        {
            RuntimeValue value = GetRuntimeValue(instruction.Rhs, instruction);
            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(!value.IsTruthy));
        }

        /// <summary>
        /// Handles the GOTO_IF_TRUE and GOTO_IF_FALSE instructions for conditional jumps.
        /// </summary>
        /// <param name="requiredCondition">The truthiness value that triggers the jump (true for GotoIfTrue, false for GotoIfFalse).</param>
        private void ExecuteGotoIf(InstructionLine instruction, bool requiredCondition)
        {
            RuntimeValue condition = GetRuntimeValue(instruction.Rhs, instruction);

            if (instruction.Lhs is not GoToValue target)
            {
                throw ConstructRuntimeException("Internal VM Error: The target of a jump instruction must be a GoToValue.");
            }

            if (condition.IsTruthy == requiredCondition)
            {
                _ip = target.Address;
            }
        }

        internal void ExecuteBranchIfEqual(InstructionLine instruction, bool target)
        {
            if (instruction.Lhs is not GoToValue jmp)
            {
                throw ConstructRuntimeException("Internal VM Error: The target of a jump instruction must be a GoToValue.");
            }

            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            instruction.SpecializedHandler = InlineCacheManager.CreateSpecializedBranchHandler(instruction, this, right, target);
            bool result = left.Equals(right);

            if (result == target)
            {
                _ip = jmp.Address;
            }
        }

        private void ExecuteBranchIfGreaterThan(InstructionLine instruction)
        {
            if (instruction.Lhs is not GoToValue jmp)
            {
                throw ConstructRuntimeException("Internal VM Error: The target of a jump instruction must be a GoToValue.");
            }

            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            instruction.SpecializedHandler = CreateSpecializedComparisonBranchHandler(instruction, this, ComparisonOperation.GreaterThan);

            if (left.DoubleValue > right.DoubleValue)
            {
                _ip = jmp.Address;
            }
        }

        private void ExecuteBranchIfGreaterOrEqual(InstructionLine instruction)
        {
            if (instruction.Lhs is not GoToValue jmp)
            {
                throw ConstructRuntimeException("Internal VM Error: The target of a jump instruction must be a GoToValue.");
            }

            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);
            instruction.SpecializedHandler = CreateSpecializedComparisonBranchHandler(instruction, this, ComparisonOperation.GreaterOrEqual);

            if (left.DoubleValue >= right.DoubleValue)
            {
                _ip = jmp.Address;
            }
        }

        private void ExecuteBranchIfLessThan(InstructionLine instruction)
        {
            if (instruction.Lhs is not GoToValue jmp)
            {
                throw ConstructRuntimeException("Internal VM Error: The target of a jump instruction must be a GoToValue.");
            }

            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);
            instruction.SpecializedHandler = CreateSpecializedComparisonBranchHandler(instruction, this, ComparisonOperation.LessThan);

            if (left.DoubleValue < right.DoubleValue)
            {
                _ip = jmp.Address;
            }
        }

        private void ExecuteBranchIfLessOrEqual(InstructionLine instruction)
        {
            if (instruction.Lhs is not GoToValue jmp)
            {
                throw ConstructRuntimeException("Internal VM Error: The target of a jump instruction must be a GoToValue.");
            }

            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);
            instruction.SpecializedHandler = CreateSpecializedComparisonBranchHandler(instruction, this, ComparisonOperation.LessOrEqual);

            if (left.DoubleValue <= right.DoubleValue)
            {
                _ip = jmp.Address;
            }
        }

        /// <summary>
        /// Handles the AND and OR logical instructions with short-circuiting behavior.
        /// </summary>
        /// <param name="isAnd">True if the operation is a logical AND, false for logical OR.</param>
        private void ExecuteLogicalOp(InstructionLine instruction, bool isAnd)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);

            // Short-circuiting for efficiency.
            if (isAnd)
            {
                if (!left.IsTruthy)
                {
                    SetRegister((TempValue)instruction.Lhs, new RuntimeValue(false));
                    return;
                }
            }
            else // is OR.
            {
                if (left.IsTruthy)
                {
                    SetRegister((TempValue)instruction.Lhs, new RuntimeValue(true));
                    return;
                }
            }

            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);
            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(right.IsTruthy));
        }

        /// <summary>
        /// Handles the TO_STRING instruction, which explicitly converts any runtime value to a string object.
        /// </summary>
        private void ExecuteToString(InstructionLine instruction)
        {
            RuntimeValue valueToConvert = GetRuntimeValue(instruction.Rhs, instruction);

            SetRegister((TempValue)instruction.Lhs, ResolveStringObjectRuntimeValue(IntrinsicHelpers.ConvertRuntimeValueToString(this, valueToConvert)));
        }

        /// <summary>
        /// Handles the EQUAL and NOT_EQUAL instructions.
        /// </summary>
        /// <param name="isEqual">True if the operation is for equality (==), false for inequality (!=).</param>
        private void ExecuteEqualityComparison(InstructionLine instruction, bool isEqual)
        {
            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);
            bool result = left.Equals(right);
            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(isEqual ? result : !result));
        }

        /// <summary>Handles the GREATER_THAN instruction.</summary>
        private void ExecuteGreaterThan(InstructionLine instruction)
        {
            ExecuteNumericComparison(instruction,
                (a, b) => a > b,
                (a, b) => a > b,
                (a, b) => a > b,
                (a, b) => a > b
            );
        }

        /// <summary>Handles the GREATER_EQUAL instruction.</summary>
        private void ExecuteGreaterEqual(InstructionLine instruction)
        {
            ExecuteNumericComparison(instruction,
                (a, b) => a >= b,
                (a, b) => a >= b,
                (a, b) => a >= b,
                (a, b) => a >= b
            );
        }

        /// <summary>Handles the LESS_THAN instruction.</summary>
        private void ExecuteLessThan(InstructionLine instruction)
        {
            ExecuteNumericComparison(instruction,
                (a, b) => a < b,
                (a, b) => a < b,
                (a, b) => a < b,
                (a, b) => a < b
            );
        }

        /// <summary>Handles the LESS_EQUAL instruction.</summary>
        private void ExecuteLessEqual(InstructionLine instruction)
        {
            ExecuteNumericComparison(instruction,
                (a, b) => a <= b,
                (a, b) => a <= b,
                (a, b) => a <= b,
                (a, b) => a <= b
            );
        }

        /// <summary>
        /// Handles the NEW_LIST instruction, creating a new, empty list object.
        /// </summary>
        private void ExecuteNewList(InstructionLine instruction)
        {
            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(new ListObject()));
        }

        /// <summary>
        /// Handles the NEW_DICTIONARY instruction, creating a new, empty dictionary object.
        /// </summary>
        private void ExecuteNewDictionary(InstructionLine instruction)
        {
            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(new DictionaryObject()));
        }

        /// <summary>
        /// A generic handler for all relational comparison operations (&gt;, &gt;=, &lt;, &lt;=).
        /// It correctly handles comparisons for both numbers and strings.
        /// </summary>
        /// <summary>
        /// Performs relational comparisons between numeric types (int, long, float, double),
        /// and string comparisons when both sides are strings.
        /// Ensures no fall-through occurs after string comparison,
        /// and guarantees correct coercion for mixed numeric types.
        /// </summary>
        private void ExecuteNumericComparison(
            InstructionLine instruction,
            Func<int, int, bool> intOp,
            Func<long, long, bool> longOp,
            Func<float, float, bool> floatOp,
            Func<double, double, bool> doubleOp)
        {
            TempValue destination = (TempValue)instruction.Lhs;

            RuntimeValue left = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue right = GetRuntimeValue(instruction.Rhs2, instruction);

            if (left.ObjectReference is StringObject leftStr &&
                right.ObjectReference is StringObject rightStr)
            {
                bool stringResult = instruction.Instruction switch
                {
                    InstructionCode.LessThan => string.CompareOrdinal(leftStr.Value, rightStr.Value) < 0,
                    InstructionCode.GreaterThan => string.CompareOrdinal(leftStr.Value, rightStr.Value) > 0,
                    InstructionCode.LessEqual => string.CompareOrdinal(leftStr.Value, rightStr.Value) <= 0,
                    InstructionCode.GreaterEqual => string.CompareOrdinal(leftStr.Value, rightStr.Value) >= 0,
                    _ => SignalError<bool>("Invalid comparison instruction for strings.")
                };

                SetRegister(destination, new RuntimeValue(stringResult));
                return;
            }

            if (left.Type != RuntimeValueType.Number || right.Type != RuntimeValueType.Number)
            {
                SignalError($"Cannot perform numeric comparison on non-number types: ({left.Type}, {right.Type})");
                return;
            }

            bool result = (left.NumberType, right.NumberType) switch
            {
                (RuntimeNumberType.Int, RuntimeNumberType.Int) => intOp(left.IntValue, right.IntValue),
                (RuntimeNumberType.Int, RuntimeNumberType.Long) => longOp(left.IntValue, right.LongValue),
                (RuntimeNumberType.Int, RuntimeNumberType.Float) => floatOp(left.IntValue, right.FloatValue),
                (RuntimeNumberType.Int, RuntimeNumberType.Double) => doubleOp(left.IntValue, right.DoubleValue),

                (RuntimeNumberType.Long, RuntimeNumberType.Int) => longOp(left.LongValue, right.IntValue),
                (RuntimeNumberType.Long, RuntimeNumberType.Long) => longOp(left.LongValue, right.LongValue),
                (RuntimeNumberType.Long, RuntimeNumberType.Float) => floatOp(left.LongValue, right.FloatValue),
                (RuntimeNumberType.Long, RuntimeNumberType.Double) => doubleOp(left.LongValue, right.DoubleValue),

                (RuntimeNumberType.Float, RuntimeNumberType.Int) => floatOp(left.FloatValue, right.IntValue),
                (RuntimeNumberType.Float, RuntimeNumberType.Long) => floatOp(left.FloatValue, right.LongValue),
                (RuntimeNumberType.Float, RuntimeNumberType.Float) => floatOp(left.FloatValue, right.FloatValue),
                (RuntimeNumberType.Float, RuntimeNumberType.Double) => doubleOp(left.FloatValue, right.DoubleValue),

                (RuntimeNumberType.Double, RuntimeNumberType.Int) => doubleOp(left.DoubleValue, right.IntValue),
                (RuntimeNumberType.Double, RuntimeNumberType.Long) => doubleOp(left.DoubleValue, right.LongValue),
                (RuntimeNumberType.Double, RuntimeNumberType.Float) => doubleOp(left.DoubleValue, right.FloatValue),
                (RuntimeNumberType.Double, RuntimeNumberType.Double) => doubleOp(left.DoubleValue, right.DoubleValue),

                _ => SignalError<bool>("Unsupported numeric type for comparison.")
            };

            SetRegister(destination, new RuntimeValue(result));
        }

        /// <summary>
        /// Handles the NEW_RANGE instruction, creating a runtime range object.
        /// </summary>
        private void ExecuteNewRange(InstructionLine instruction)
        {
            if (instruction.Rhs is null)
            {
                throw ConstructRuntimeException("Internal VM Error: NewRange opcode requires a non-null RangeValue operand.");
            }

            TryReturnRegisterReferenceToPool((TempValue)instruction.Lhs);

            RuntimeValue rangeRuntimeValue = GetRuntimeValue(instruction.Rhs, instruction);
            SetRegister((TempValue)instruction.Lhs, rangeRuntimeValue);
        }

        /// <summary>
        /// Handles the GET_LENGTH instruction for any collection that has a length (string, list, range).
        /// </summary>
        private void ExecuteGetLength(InstructionLine instruction)
        {
            RuntimeValue collection = GetRuntimeValue(instruction.Rhs, instruction);
            int length;

            if (collection.Type == RuntimeValueType.Object)
            {
                switch (collection.ObjectReference)
                {
                    case StringObject str:
                        length = string.IsNullOrEmpty(str.Value) ? 0 : str.Value.Length;
                        break;
                    case ListObject list:
                        length = list.Elements.Count;
                        break;
                    case RangeObject range:
                        if (range.Start.Type != RuntimeValueType.Number || range.End.Type != RuntimeValueType.Number)
                        {
                            SignalError($"Runtime Error: Cannot get length of a range with non-numeric bounds ({GetDetailedTypeName(range.Start)}, {GetDetailedTypeName(range.End)}).");
                            return;
                        }
                        int start = range.Start.IntValue;
                        int end = range.End.IntValue;
                        length = end < start ? 0 : end - start + 1;
                        break;
                    default:
                        SignalError($"Runtime Error: Cannot get the length of a value of type '{GetDetailedTypeName(collection)}'.");
                        return;
                }
            }
            else
            {
                SignalError($"Cannot get length of a non-object type '{collection.Type}'.");
                return;
            }

            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(length));
        }

        /// <summary>
        /// Handles the PUSH_KEY_VALUE_PAIR instruction, which adds a key-value pair to a dictionary object.
        /// </summary>
        private void ExecutePushKeyValuePair(InstructionLine instruction)
        {
            // Rhs is key, Rhs2 is value.

            RuntimeValue dictVal = GetRuntimeValue(instruction.Lhs, instruction);
            if (dictVal.ObjectReference is not DictionaryObject dictionary)
            {
                SignalError($"Runtime Error: Cannot push a key-value pair element to a non-dictionary value (got type '{GetDetailedTypeName(dictVal)}').");
                return;
            }

            RuntimeValue key = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue value = GetRuntimeValue(instruction.Rhs2, instruction);

            dictionary.Dictionary.Add(key, value);
        }

        /// <summary>
        /// Handles the PUSH_ELEMENT instruction, which adds an element or an expanded range to a list.
        /// </summary>
        private void ExecutePushElement(InstructionLine instruction)
        {
            RuntimeValue listVal = GetRuntimeValue(instruction.Lhs, instruction);
            if (listVal.ObjectReference is not ListObject list)
            {
                SignalError($"Runtime Error: Cannot push an element to a non-list value (got type '{GetDetailedTypeName(listVal)}').");
                return;
            }

            RuntimeValue elementToAdd = GetRuntimeValue(instruction.Rhs, instruction);

            if (elementToAdd.ObjectReference is RangeObject range)
            {
                RuntimeValue startValue = range.Start;
                RuntimeValue endValue = range.End;

                if (startValue.Type != RuntimeValueType.Number || endValue.Type != RuntimeValueType.Number)
                {
                    SignalError($"Runtime Error: Range bounds must be numbers, not '{GetDetailedTypeName(range.Start)}' and '{GetDetailedTypeName(range.End)}'.");
                    return;
                }

                int start = startValue.IntValue;
                int end = endValue.IntValue;

                if (start <= end)
                {
                    for (int i = start; i <= end; i++)
                    {
                        list.Elements.Add(new RuntimeValue(i));
                    }
                }
                else // Decreasing range.
                {
                    for (int i = start; i >= end; i--)
                    {
                        list.Elements.Add(new RuntimeValue(i));
                    }
                }
            }
            else
            {
                list.Elements.Add(elementToAdd);
            }
        }

        /// <summary>
        /// Handles the NEW_INSTANCE instruction, allocating a new, empty instance of a user-defined struct.
        /// </summary>
        private void ExecuteNewInstance(InstructionLine instruction)
        {
            if (instruction.Rhs is not StructSymbol classSymbol)
            {
                throw ConstructRuntimeException("Internal VM Error: NewInstance requires a StructSymbol as its operand.");
            }

            InstanceObject instance = new InstanceObject(classSymbol);
            SetRegister((TempValue)instruction.Lhs, new RuntimeValue(instance));
        }

        /// <summary>
        /// Handles the GET_FIELD instruction, which retrieves the value of a field or method from a struct instance.
        /// </summary>
        private void ExecuteGetField(InstructionLine instruction)
        {
            if (instruction.Rhs2 is not StringValue fieldName)
            {
                throw ConstructRuntimeException("Internal VM Error: GetField requires a string literal for the field name.");
            }

            RuntimeValue instanceValue = GetRuntimeValue(instruction.Rhs, instruction);

            if (instanceValue.ObjectReference is Wrapper wrapper)
            {
                if (wrapper.InstanceFields.TryGetValue(fieldName.Value, out RuntimeValue value))
                {
                    SetRegister((TempValue)instruction.Lhs, value);
                    return;
                }
                else
                {
                    throw ConstructRuntimeException($"Runtime Error: Cannot access property '{fieldName.Value}' on an intrinsic wrapper instance (got type 'Wrapper__{GetDetailedTypeName(instanceValue)}').");
                }
            }

            if (instanceValue.ObjectReference is not InstanceObject instance)
            {
                throw ConstructRuntimeException($"Runtime Error: Cannot access property '{fieldName.Value}' on a non-instance value (got type '{GetDetailedTypeName(instanceValue)}').");
            }

            SetRegister((TempValue)instruction.Lhs, instance.GetField(fieldName.Value, this));
        }

        /// <summary>
        /// Handles the GET_STATIC instruction, retrieving a static field's value from a struct symbol.
        /// </summary>
        private void ExecuteGetStatic(InstructionLine instruction)
        {
            if (instruction.Rhs is not StructSymbol structSymbol ||
                instruction.Rhs2 is not StringValue fieldName)
            {
                throw ConstructRuntimeException("Internal VM Error: Invalid operands for GET_STATIC. Expected StructSymbol and StringValue.");
            }

            if (structSymbol.StaticFields.TryGetValue(fieldName.Value, out RuntimeValue value))
            {
                SetRegister((TempValue)instruction.Lhs, value);
                return;
            }

            SignalError($"Internal VM Erorr: Attempt to retrieve a non-existant static struct field: {structSymbol}__Field:{fieldName.Value}.");
        }

        /// <summary>
        /// Handles the SET_STATIC instruction, assigning a value to a struct's static field.
        /// </summary>
        private void ExecuteSetStatic(InstructionLine instruction)
        {
            if (instruction.Lhs is not StructSymbol structSymbol || instruction.Rhs is not StringValue fieldName)
            {
                throw ConstructRuntimeException("Internal VM Error: Invalid operands for SET_STATIC. Expected StructSymbol and StringValue.");
            }

            RuntimeValue valueToSet = GetRuntimeValue(instruction.Rhs2, instruction);
            structSymbol.StaticFields[fieldName.Value] = valueToSet;
        }

        /// <summary>
        /// Handles the SET_FIELD instruction, which assigns a new value to a field of a struct instance.
        /// </summary>
        private void ExecuteSetField(InstructionLine instruction)
        {
            // Lhs: The instance to modify.
            // Rhs: The field name.
            // Rhs2: The new value.

            if (instruction.Rhs is not StringValue fieldName)
            {
                throw ConstructRuntimeException("Internal VM Error: SetField requires a string literal for the field name.");
            }

            RuntimeValue instanceValue = GetRuntimeValue(instruction.Lhs, instruction);

            if (instanceValue.ObjectReference is Wrapper wrapper)
            {
                if (wrapper.InstanceFields.ContainsKey(fieldName.Value))
                {
                    RuntimeValue newValue = GetRuntimeValue(instruction.Rhs2, instruction);
                    wrapper.InstanceFields[fieldName.Value] = newValue;
                    return;
                }
                else
                {
                    throw ConstructRuntimeException($"Runtime Error: Cannot access property '{fieldName.Value}' on an intrinsic wrapper instance (got type 'Wrapper__{GetDetailedTypeName(instanceValue)}').");
                }
            }

            if (instanceValue.ObjectReference is not InstanceObject instance)
            {
                throw ConstructRuntimeException($"Runtime Error: Cannot set property '{fieldName.Value}' on a non-instance value (got type '{GetDetailedTypeName(instanceValue)}').");
            }

            if (instance.Class.StaticFields.ContainsKey(fieldName.Value))
            {
                throw ConstructRuntimeException($"Runtime Error: Cannot set solid ( static ) property '{fieldName.Value}' of a struct.");
            }

            RuntimeValue valueToSet = GetRuntimeValue(instruction.Rhs2, instruction);

            instance.SetField(fieldName.Value, valueToSet);
        }

        /// <summary>
        /// Handles the GET_ELEMENT instruction for retrieving an element from a list by its index.
        /// </summary>
        private void ExecuteGetElement(InstructionLine instruction)
        {
            RuntimeValue collection = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue indexVal = GetRuntimeValue(instruction.Rhs2, instruction);

            switch (collection.ObjectReference)
            {
                case ListObject list:
                    {
                        if (indexVal.Type != RuntimeValueType.Number)
                        {
                            SignalError($"Runtime Error: List index must be a number, not '{GetDetailedTypeName(indexVal)}'.");
                            return;
                        }

                        int index = indexVal.IntValue;

                        if (index < 0 || index >= list.Elements.Count)
                        {
                            SignalError($"Runtime Error: Index out of range. Index was {index}, but list size is {list.Elements.Count}.");
                            return;
                        }

                        SetRegister((TempValue)instruction.Lhs, list.Elements[index]);
                        break;
                    }
                case StringObject str:
                    {
                        if (indexVal.Type != RuntimeValueType.Number)
                        {
                            SignalError($"Runtime Error: String index must be a number, not '{GetDetailedTypeName(indexVal)}'.");
                            return;
                        }

                        int index = indexVal.IntValue;

                        if (index < 0 || string.IsNullOrEmpty(str.Value) || index >= str.Value.Length)
                        {
                            SignalError($"Runtime Error: Index out of range. Index was {index}, but string length is '{(str.Value is null ? "The string was empty" : str.Value.Length)}'.");
                            return;
                        }

                        char charAsString = str.Value![index];
                        CharObject resultChar = _charObjectPool.Get();
                        resultChar.Initialize(charAsString);
                        SetRegister((TempValue)instruction.Lhs, new RuntimeValue(resultChar));
                        break;
                    }
                case DictionaryObject dictionary:
                    {
                        SetRegister((TempValue)instruction.Lhs, dictionary.Dictionary[indexVal]);
                        break;
                    }
                // Not an indexable type.
                default:
                    SignalError($"Runtime Error: Cannot apply index operator [...] to a non-indexable type '{GetDetailedTypeName(collection)}'.");
                    return;
            }

            instruction.SpecializedHandler = InlineCacheManager.CreateSpecializedGetElementHandler(instruction, this, collection, indexVal);
        }

        /// <summary>
        /// Handles the SET_ELEMENT instruction for updating an element in a list at a given index.
        /// </summary>
        private void ExecuteSetElement(InstructionLine instruction)
        {
            RuntimeValue collection = GetRuntimeValue(instruction.Lhs, instruction);
            RuntimeValue indexVal = GetRuntimeValue(instruction.Rhs, instruction);
            RuntimeValue valueToSet = GetRuntimeValue(instruction.Rhs2, instruction);

            if (collection.As<DictionaryObject>() is DictionaryObject dictionary)
            {
                dictionary.Dictionary[indexVal] = valueToSet;
                return;
            }

            if (collection.As<ListObject>() is not ListObject list)
            {
                SignalError($"Internal VM Error: Cannot apply index operator [...] to a non-list value (got type '{GetDetailedTypeName(collection)}').");
                return;
            }

            if (indexVal.Type != RuntimeValueType.Number)
            {
                SignalError($"Runtime Error: List index must be a number, not '{GetDetailedTypeName(indexVal)}'.");
                return;
            }

            int index = indexVal.IntValue;

            if (index < 0 || index >= list.Elements.Count)
            {
                SignalError($"Runtime Error: Index out of range. Index was {index}, but list size is {list.Elements.Count}.");
                return;
            }

            list.Elements[index] = valueToSet;
        }

        /// <summary>
        /// Handles the TRY_BLOCK instruction, pushing a new try-catch context block onto the error stack.
        /// </summary>
        private void ExecuteTryBlock(InstructionLine instruction)
        {
            TryCatchValue context = (TryCatchValue)instruction.Lhs;

            _tryCatchBlocks.Push(context);
        }

        /// <summary>
        /// Handles the CATCH_BLOCK instruction, popping a try-catch context block from the error stack, signaling that the 
        /// try block executed without any runtime errors.
        /// </summary>
        private void ExecuteCatchBlock(InstructionLine instruction)
        {
            TryCatchValue context = _tryCatchBlocks.Pop();

            if (!context.CaughtException)
            {
                _ip = context.CatchGoToIndex;
            }

            context.CaughtException = false;
        }

        /// <summary>
        /// Handles the NEW_ITERATOR instruction, creating an iterator object for a for-in loop.
        /// </summary>
        private void ExecuteNewIterator(InstructionLine instruction)
        {
            RuntimeValue iterable = GetRuntimeValue(instruction.Rhs, instruction);

            if (iterable.ObjectReference is ListObject or RangeObject)
            {
                TryReturnRegisterReferenceToPool((TempValue)instruction.Lhs);

                IteratorObject iterator = _iteratorObjectPool.Get();
                iterator.Initialize(iterable.ObjectReference);
                SetRegister((TempValue)instruction.Lhs, new RuntimeValue(iterator));
                return;
            }

            throw ConstructRuntimeException($"Runtime Error: Cannot create an iterator from a non-iterable type '{GetDetailedTypeName(iterable)}'.");
        }

        /// <summary>
        /// Handles the ITER_NEXT instruction, which advances an iterator and retrieves the next value.
        /// </summary>
        private void ExecuteIterNext(InstructionLine instruction)
        {
            // Lhs:  The source iterator register.
            // Rhs:  The destination register for the value.
            // Rhs2: The destination register for the continue flag.

            if (instruction.Lhs is not TempValue iteratorReg ||
                instruction.Rhs is not TempValue valueReg ||
                instruction.Rhs2 is not TempValue continueFlagReg)
            {
                throw ConstructRuntimeException("Internal VM Error: Invalid operands for IterNext. Expected (Source Iterator, Dest Value, Dest Flag).");
            }

            RuntimeValue iteratorVal = _cachedRegisters[iteratorReg.RegisterIndex];

            if (iteratorVal.As<IteratorObject>() is not IteratorObject iterator)
            {
                throw ConstructRuntimeException("Internal VM Error: Attempted to iterate over a non-iterator value.");
            }

            SpecializedOpcodeHandler? handler = InlineCacheManager.CreateSpecializedIterNextHandler(instruction, iterator);
            if (handler != null)
            {
                instruction.SpecializedHandler = handler;
                handler(instruction, this);
                return;
            }

            // Fallback.
            bool continueLoop = false;
            RuntimeValue nextValue = RuntimeValue.Nil;

            switch (iterator.Iterable)
            {
                case RangeObject range:
                    int start = range.Start.IntValue;
                    int end = range.End.IntValue;
                    int currentValue = start + iterator.CurrentIndex;

                    if (start <= end ? currentValue <= end : currentValue >= end)
                    {
                        nextValue = new RuntimeValue(currentValue);
                        continueLoop = true;
                        iterator.CurrentIndex += start <= end ? 1 : -1;
                    }
                    break;
                case ListObject list:
                    if (iterator.CurrentIndex < list.Elements.Count)
                    {
                        nextValue = list.Elements[iterator.CurrentIndex];
                        continueLoop = true;
                        iterator.CurrentIndex++;
                    }
                    break;
            }

            SetRegister(valueReg, nextValue);
            SetRegister(continueFlagReg, new RuntimeValue(continueLoop));
        }

        /// <summary>
        /// Handles the PUSH_PARAM instruction, which pushes a value onto the operand stack in preparation for a function call.
        /// </summary>
        private void ExecutePushParam(InstructionLine instruction)
        {
            _operandStack.Push(GetRuntimeValue(instruction.Lhs, instruction));
        }

        /// <summary>
        /// Handles the PUSH_TWO_PARAMS instruction, which pushes two values onto the operand stack in preparation for a function call.
        /// </summary>
        private void ExecutePushTwoParam(InstructionLine instruction)
        {
            _operandStack.Push(GetRuntimeValue(instruction.Lhs, instruction));
            _operandStack.Push(GetRuntimeValue(instruction.Rhs, instruction));
        }

        /// <summary>
        /// Handles the PUSH_THREE_PARAMS instruction, which pushes three values onto the operand stack in preparation for a function call.
        /// </summary>
        private void ExecutePushThreeParam(InstructionLine instruction)
        {
            _operandStack.Push(GetRuntimeValue(instruction.Lhs, instruction));
            _operandStack.Push(GetRuntimeValue(instruction.Rhs, instruction));
            _operandStack.Push(GetRuntimeValue(instruction.Rhs2, instruction));
        }

        /// <summary>
        /// Handles the PUSH_FOUR_PARAMS instruction, which pushes four values onto the operand stack in preparation for a function call.
        /// This is the most we can push in one instruction since an instruction allows up to four values: Lhs, Rhs, Rhs2, Rhs3.
        /// </summary>
        private void ExecutePushFourParam(InstructionLine instruction)
        {
            _operandStack.Push(GetRuntimeValue(instruction.Lhs, instruction));
            _operandStack.Push(GetRuntimeValue(instruction.Rhs, instruction));
            _operandStack.Push(GetRuntimeValue(instruction.Rhs2, instruction));
            _operandStack.Push(GetRuntimeValue(instruction.Rhs3, instruction));
        }

        private void ExecuteThrow(InstructionLine instruction)
        {
            RuntimeValue value;

            if (instruction.Lhs is TempValue temp)
            {
                value = GetRuntimeValue(temp, instruction);
            }
            else
            {
                value = GetRuntimeValue((VariableValue)instruction.Lhs, instruction);
            }

            if (value.ObjectReference is Wrapper wrapper && wrapper.Instance is ScriptException scriptException)
            {
                SignalError(scriptException.Message, RuntimeExceptionType.ScriptException);
                return;
            }

            if (value.ObjectReference is InstanceObject instance && instance.Class.ImplementedTraits.Contains("exception".GetHashCode()))
            {
                SignalError(instance.GetField("message", this).As<StringObject>().Value, RuntimeExceptionType.ScriptException);
                return;
            }

            throw ConstructRuntimeException($"Runtime Error: 'throw' keyword allows only either the intrinsic 'Exception' class to be used, or a struct that inherits from the intrinsic 'exception' trait to be used.");
        }

        /// <summary>
        /// Handles the IS_TYPE instruction, which checks whether a variable is of the given trait or struct type.
        /// </summary>
        private void ExecuteIsType(InstructionLine instruction)
        {
            TempValue destRegister = (TempValue)instruction.Lhs;
            VariableValue variable = (VariableValue)instruction.Rhs;
            StringValue targetType = (StringValue)instruction.Rhs2;

            RuntimeValue value = GetRuntimeValue(variable, instruction);
            int hash = targetType.Value.GetHashCode();

            if (value.ObjectReference is Wrapper wrapper)
            {
                if (wrapper.IntrinsicSymbolMarker is not null && wrapper.IntrinsicSymbolMarker is IntrinsicStructSymbol symbol && (wrapper.IntrinsicSymbolMarker.Hash == hash || symbol.ImplementedTraits.Contains(hash)))
                {
                    SetRegister(destRegister, RuntimeValue.True);
                    return;
                }

                SetRegister(destRegister, RuntimeValue.False);
                return;
            }

            if (value.ObjectReference is not InstanceObject instance)
            {
                SetRegister(destRegister, RuntimeValue.False);
                return;
            }

            // If the class implements a trait, and we compare against a trait, that is still true.
            if (instance.Class.ImplementedTraits.Contains(hash) || instance.Class.Hash == hash)
            {
                SetRegister(destRegister, RuntimeValue.True);
                return;
            }

            SetRegister(destRegister, RuntimeValue.False);
        }

        /// <summary>
        /// Handles the GET_TYPE instruction, which creates a wrapper around a <see cref="TypeMetadata"/> object.
        /// </summary>
        private void ExecuteGetType(InstructionLine instruction)
        {
            TempValue destRegister = (TempValue)instruction.Lhs;
            Value operand = instruction.Rhs;

            TypeMetadata metadata;

            // Raw type name.
            if (operand is StringValue typeNameValue)
            {
                string typeName = typeNameValue.Value;

                if (TryFindSymbol(typeName.GetHashCode(), out Symbol symbol, out FluenceScope symbolScope))
                {
                    metadata = symbol switch
                    {
                        StructSymbol s => CreateMetadataFromStructSymbol(s, symbolScope),
                        EnumSymbol e => new TypeMetadata(e.Name, $"{symbolScope.Name}.{e.Name}", TypeCategory.Enum, 0, false, enumMembers: e.Members.Keys.ToList()),
                        _ => new TypeMetadata(typeName, typeName, TypeCategory.Unknown, 0, false)
                    };
                }
                else
                {
                    throw ConstructRuntimeException($"Runtime Error: Unknown type or symbol '{typeName}'.");
                }
            }
            // Variable or expression.
            else
            {
                RuntimeValue value = GetRuntimeValue(operand, instruction);

                string name = IntrinsicHelpers.GetRuntimeTypeName(value);

                switch (value.ObjectReference)
                {
                    case InstanceObject instance:
                        metadata = CreateMetadataFromStructSymbol(instance.Class, instance.Class.Scope);
                        break;
                    // TO DO
                    //case EnumMemberObject enumMember:
                    //    var e = enumMember.EnumType;
                    //    metadata = new TypeMetadata(e.Name, $"{e.DefiningScope.Name}.{e.Name}", TypeCategory.Enum, enumMembers: e.Members.Keys.ToList());
                    //    break;
                    case ListObject:
                        metadata = new TypeMetadata("List", "List", TypeCategory.BuiltIn, 0, false);
                        break;
                    case FunctionObject func:
                        bool isLambda = operand is LambdaValue;

                        MethodMetadata methodMeta = new MethodMetadata(func.Name, func.Arity, false, func.Arguments, func.RefMask);
                        metadata = new TypeMetadata("function", "function", TypeCategory.Function, func.Arity, isLambda, null, null, null, new List<MethodMetadata>() { methodMeta }, null, func.Arguments, func.RefMask);
                        break;
                    default:
                        metadata = new TypeMetadata(name, name, TypeCategory.Primitive, 0, false);
                        break;
                }
            }

            RuntimeValue typeObject = TypeMetadataWrapper.Create(metadata);
            SetRegister(destRegister, typeObject);
        }

        /// <summary>
        /// A helper to create a TypeMetadata object from a StructSymbol.
        /// </summary>
        private static TypeMetadata CreateMetadataFromStructSymbol(StructSymbol s, FluenceScope scope) =>
            new TypeMetadata(
                name: s.Name,
                fullName: $"{scope.Name}.{s.Name}",
                category: TypeCategory.Struct,
                0,
                false,
                instanceFields: s.Fields.Select(f => new FieldMetadata(f, false, false)).ToList(),
                staticFields: s.StaticFields.Keys.Select(f => new FieldMetadata(f, true, true)).ToList(),
                constructors: s.Constructors.Values.Select(c => new MethodMetadata(c.Name, c.Arity, true, c.Arguments!, c.RefMask)).ToList(),
                instanceMethods: s.Functions.Values.Select(m => new MethodMetadata(m.Name, m.Arity, false, m.Arguments!, m.RefMask)).ToList(),
                null!,
                null!,
                0,
                s
            );

        /// <summary>
        /// A helper to search all relevant scopes for a named symbol.
        /// </summary>
        private bool TryFindSymbol(int hash, out Symbol symbol, out FluenceScope foundScope)
        {
            if (CurrentFrame.Function.DefiningScope?.TryResolve(hash, out symbol) ?? false)
            {
                foundScope = CurrentFrame.Function.DefiningScope;
                return true;
            }

            foreach (FluenceScope ns in Namespaces.Values)
            {
                if (ns.TryResolve(hash, out symbol))
                {
                    foundScope = ns;
                    return true;
                }
            }

            if (_globalScope.TryResolve(hash, out symbol))
            {
                foundScope = _globalScope;
                return true;
            }

            foundScope = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PrepareFunctionCall(CallFrame frame, FunctionObject function)
        {
            _callStack.Push(frame);
            _cachedRegisters = frame.Registers;
            _cachedWritableCache = frame.WritableCache;
            _ip = function.StartAddress;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RuntimeValue PopStack()
        {
            if (_operandStack.Count > 0)
            {
                return _operandStack.Pop();
            }

            throw ConstructRuntimeException("Internal VM Error: Attempt to pop value from the operand stack, the operand stack was empty.");
        }

        /// <summary>
        /// Handles the CALL_FUNCTION instruction, which invokes a standalone function.
        /// </summary>
        private void ExecuteCallFunction(InstructionLine instruction)
        {
            RuntimeValue functionVal = GetRuntimeValue(instruction.Rhs, instruction);

            if (functionVal.ObjectReference is not FunctionObject function)
            {
                throw ConstructRuntimeException($"Internal VM Error: Attempted to call a value that is not a function (got type '{GetDetailedTypeName(functionVal)}').");
            }

            if (!function.DefiningScope.IsTheGlobalScope)
            {
                string scopeName = function.DefiningScope.Name;
                if (!IsLibraryAllowed(scopeName))
                {
                    throw ConstructRuntimeException($"Security Error: Use of the library '{scopeName}' is disallowed by the host application due to library whiteList and or blackList rules.");
                }
            }

            int argCount = GetRuntimeValue(instruction.Rhs2, instruction).IntValue;

            if (function.Arity != argCount && function.Arity != -100)
            {
                throw ConstructRuntimeException($"Internal VM Error: Mismatched arguments for function '{function.Name}'. Expected {function.Arity}, but got {argCount}.");
            }

            SpecializedOpcodeHandler? handler = CreateSpecializedCallFunctionHandler(this, instruction, function);
            if (handler != null)
            {
                instruction.SpecializedHandler = handler;
                handler(instruction, this);
                return;
            }

            // Intrinsic and normal function calls are handled by the SpecializedHandler, because their function blueprint is not null.
            // If we get here then we are dealing with a lambda function.

            CallFrame newFrame = _callFramePool.Get();
            newFrame.Initialize(this, function, _ip, (TempValue)instruction.Lhs);
            int initialArgIndex = 0;

            for (int i = argCount - 1; i >= 0; i--)
            {
                int paramIndex = initialArgIndex + i;
                RuntimeValue argValue = _operandStack.Pop();

                bool isRef = (function.RefMask & (1 << i)) != 0;

                if (isRef)
                {
                    if (argValue.ObjectReference is not ReferenceValue reference)
                    {
                        string argName = function.Arguments[i];
                        SignalError($"Internal VM Error: Argument '{argName}' in function: \"{function.ToCodeLikeString()}\" must be passed by reference ('ref').");
                        return;
                    }
                    else
                    {
                        newFrame.RefParameterMap[paramIndex] = reference.Reference.RegisterIndex;
                        argValue = GetRuntimeValue(reference.Reference, instruction);
                        newFrame.Registers[paramIndex] = argValue;
                    }
                }
                else
                {
                    newFrame.Registers[paramIndex] = argValue;
                }
            }

            _callStack.Push(newFrame);
            _cachedRegisters = newFrame.Registers;
            _ip = function.StartAddress;
        }

        private void ExecuteNewLambda(InstructionLine instruction)
        {
            VariableValue destination = (VariableValue)instruction.Lhs;
            LambdaValue lambdaValue = (LambdaValue)instruction.Rhs;

            string baseName = destination.Name;
            int arity = lambdaValue.Function.Arity;
            string mangledName = Mangler.Mangle(baseName, arity);

            lambdaValue.Function.SetName(mangledName);
            FunctionObject lambdaObject = CreateFunctionObject(lambdaValue.Function);
            lambdaObject.IsLambda = true;

            AssignVariable(destination, new RuntimeValue(lambdaObject), instruction, destination.IsReadOnly);
            AssignVariable(destination, new RuntimeValue(lambdaObject), instruction, destination.IsReadOnly);
        }

        /// <summary>
        /// Handles the CALL_METHOD instruction code, which invokes a method on an object instance.
        /// </summary>
        private void ExecuteCallMethod(InstructionLine instruction)
        {
            if (instruction.Rhs2 is not StringValue methodNameVal)
            {
                throw ConstructRuntimeException("Internal VM Error: Invalid operands for CallMethod. Expected a method name as a string.");
            }

            string methodName = methodNameVal.Value;
            RuntimeValue instanceVal = GetRuntimeValue(instruction.Rhs, instruction);

            if (instanceVal.ObjectReference is IFluenceObject fluenceObject)
            {
                if (fluenceObject.TryGetIntrinsicMethod(methodName, out IntrinsicRuntimeMethod? intrinsicMethod))
                {
                    SetRegister((TempValue)instruction.Lhs, intrinsicMethod(this, instanceVal));
                    return;
                }
            }

            if (instanceVal.ObjectReference is not InstanceObject instance)
            {
                throw ConstructRuntimeException($"Internal VM Error: Cannot call method '{methodName}' on a non-instance object of type '{GetDetailedTypeName(instanceVal)}'.");
            }

            FunctionObject functionToExecute = null;
            FunctionValue methodBlueprint;

            if (methodName.StartsWith("init__", StringComparison.InvariantCulture))
            {
                methodBlueprint = instance.Class.Constructors[methodName];
            }
            else
            {
                methodBlueprint = instance.Class.Functions[methodName];
            }

            // <script> frame.
            if (CurrentFrame.ReturnAddress == _byteCode.Count)
            {
                if (methodBlueprint == null)
                {
                    SetRegister((TempValue)instruction.Lhs, instanceVal);
                    return;
                }
            }
            else
            {
                string demangledMethodName = Mangler.Demangle(methodName);
                // A class field that is a function, that is currently a lambda.
                if (instance.Class.Fields.Contains(demangledMethodName))
                {
                    functionToExecute = (FunctionObject)instance.GetField(demangledMethodName, this).ObjectReference;
                    functionToExecute!.IsLambda = true;
                }
                else if (!instance.Class.Functions.TryGetValue(methodName, out methodBlueprint) && !instance.Class.Constructors.TryGetValue(methodName, out methodBlueprint))
                {
                    throw ConstructRuntimeException($"Internal VM Error: Undefined method or lambda '{methodName}' on struct '{instance.Class.Name}'.");
                }
            }

            functionToExecute ??= CreateFunctionObject(methodBlueprint);

            if (!functionToExecute.DefiningScope.IsTheGlobalScope)
            {
                string scopeName = functionToExecute.DefiningScope.Name;
                if (!IsLibraryAllowed(scopeName))
                {
                    throw ConstructRuntimeException($"Security Error: Use of the library '{scopeName}' is disallowed by the host application due to library whiteList and or blackList rules.");
                }
            }

            int argCountOnStack = _operandStack.Count;
            if (functionToExecute.Arity != argCountOnStack)
            {
                throw ConstructRuntimeException($"Internal VM Error: Mismatched arity for method '{functionToExecute.Name}'. Expected {functionToExecute.Arity}, but got {argCountOnStack}.");
            }

            CallFrame newFrame = _callFramePool.Get();
            newFrame.Initialize(this, functionToExecute, _ip, (TempValue)instruction.Lhs);

            // Implicitly pass 'self'.
            newFrame.Registers[0] = instanceVal;
            int initialArgIndex = 1;

            for (int i = functionToExecute.Arity - 1; i >= 0; i--)
            {
                int paramIndex = initialArgIndex + i;
                RuntimeValue argValue = _operandStack.Pop();

                bool isRef = (functionToExecute.RefMask & (1 << i)) != 0;

                if (isRef)
                {
                    if (argValue.ObjectReference is not ReferenceValue reference)
                    {
                        string argName = functionToExecute.Arguments[i];
                        SignalError($"Internal VM Error: Argument '{argName}' in function: \"{functionToExecute.ToCodeLikeString()}\" must be passed by reference ('ref').");
                        return;
                    }
                    else
                    {
                        newFrame.RefParameterMap[paramIndex] = reference.Reference.RegisterIndex;
                        argValue = GetRuntimeValue(reference.Reference, instruction);
                        newFrame.Registers[paramIndex] = argValue;
                    }
                }
                else
                {
                    newFrame.Registers[paramIndex] = argValue;
                }
            }

            _callStack.Push(newFrame);
            _cachedRegisters = newFrame.Registers;
            _ip = functionToExecute.StartAddress;
        }

        /// <summary>
        /// Executes a manual method call from outside the virtual machine.
        /// </summary>
        /// <param name="instance">The instance of a struct to call the method on.</param>
        /// <param name="func">The function of the instance to call.</param>
        /// <returns>The result of the function's return.</returns>
        internal RuntimeValue ExecuteManualMethodCall(InstanceObject instance, FunctionValue func)
        {
            int savedIp = _ip;
            RuntimeValue[] savedRegisters = _cachedRegisters;

            FunctionObject functionToExecute = CreateFunctionObject(func);
            CallFrame newFrame = _callFramePool.Get();

            newFrame.Initialize(this, functionToExecute, -1, null!);

            // Sometimes a struct function may have no arguments, or no "self" used, no temps.
            if (newFrame.Registers.Length > 0)
            {
                newFrame.Registers[0] = new RuntimeValue(instance);
            }

            _callStack.Push(newFrame);
            _cachedRegisters = newFrame.Registers;
            _ip = functionToExecute.StartAddress;

            RuntimeValue returnValue = RuntimeValue.Nil;

            while (true)
            {
                if (_callStack.Peek() != newFrame)
                {
                    break;
                }

                InstructionLine instruction = _byteCode[_ip];
                _ip++;

                if (instruction.Instruction == InstructionCode.Return)
                {
                    returnValue = GetRuntimeValue(instruction.Lhs, instruction);

                    _callStack.Pop();
                    _callFramePool.Return(newFrame);
                    break;
                }

                if (instruction.SpecializedHandler != null)
                {
                    instruction.SpecializedHandler(instruction, this);
                }
                else
                {
                    if (instruction.Instruction is InstructionCode.Goto)
                    {
                        _ip = ((GoToValue)instruction.Lhs).Address;
                        continue;
                    }
                    _dispatchTable[(int)instruction.Instruction](instruction);
                }
            }

            _ip = savedIp;
            _cachedRegisters = savedRegisters;
            return returnValue;
        }

        /// <summary>
        /// Executes a specific function directly from the host application.
        /// </summary>
        /// <param name="funcBlueprint">The function blueprint to execute.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        /// <returns>The result of the function execution.</returns>
        internal RuntimeValue ExecuteFunctionDirect(FunctionSymbol funcBlueprint, RuntimeValue[] args)
        {
            int savedIp = _ip;
            RuntimeValue[] savedRegisters = _cachedRegisters;

            FunctionObject functionToExecute = CreateFunctionObject(funcBlueprint);
            CallFrame newFrame = _callFramePool.Get();

            newFrame.Initialize(this, functionToExecute, -1, null!);

            _callStack.Push(newFrame);
            _cachedRegisters = newFrame.Registers;
            _ip = functionToExecute.StartAddress;

            foreach (RuntimeValue arg in args) _operandStack.Push(arg);
            int initialArgIndex = 0;

            for (int i = functionToExecute.Arity - 1; i >= 0; i--)
            {
                int paramIndex = initialArgIndex + i;
                RuntimeValue argValue = _operandStack.Pop();

                bool isRef = (functionToExecute.RefMask & (1 << i)) != 0;

                if (isRef)
                {
                    SignalError("Internal VM Error: Calling functions with reference arguments manually is not supported.");
                    return RuntimeValue.Nil;
                }

                newFrame.Registers[paramIndex] = argValue;
            }

            _cachedRegisters = newFrame.Registers;
            _cachedWritableCache = newFrame.WritableCache;
            RuntimeValue returnValue = RuntimeValue.Nil;

            while (true)
            {
                if (_callStack.Peek() != newFrame)
                {
                    break;
                }

                InstructionLine instruction = _byteCode[_ip];
                _ip++;

                if (instruction.Instruction == InstructionCode.Return)
                {
                    returnValue = GetRuntimeValue(instruction.Lhs, instruction);

                    _callStack.Pop();
                    _callFramePool.Return(newFrame);
                    break;
                }

                if (instruction.SpecializedHandler != null)
                {
                    instruction.SpecializedHandler(instruction, this);
                }
                else
                {
                    if (instruction.Instruction is InstructionCode.Goto)
                    {
                        _ip = ((GoToValue)instruction.Lhs).Address;
                        continue;
                    }
                    _dispatchTable[(int)instruction.Instruction](instruction);
                }
            }

            _ip = savedIp;
            _cachedRegisters = savedRegisters;
            return returnValue;
        }

        /// <summary>
        /// Handles the CAll_STATIC instruction, which executes a static method of a struct type.
        /// </summary>
        private void ExecuteCallStatic(InstructionLine instruction)
        {
            if (instruction.Rhs is not StructSymbol structSymbol ||
                instruction.Rhs2 is not StringValue methodName)
            {
                throw CreateRuntimeException("Internal VM Error: Invalid operands for CALL_STATIC.");
            }

            if (structSymbol.StaticIntrinsics.TryGetValue(methodName.Value, out FunctionSymbol intrinsicSymbol))
            {
                int argCount = _operandStack.Count;
                if (intrinsicSymbol.Arity != argCount)
                {
                    CreateAndThrowRuntimeException($"Runtime Error: Mismatched arity for static intrinsic struct function '{intrinsicSymbol.Name}'. Expected {intrinsicSymbol.Arity}, but got {argCount}.");
                }

                RuntimeValue resultValue = intrinsicSymbol.IntrinsicBody!(this, argCount);
                SetRegister((TempValue)instruction.Lhs, resultValue);
                return;
            }

            if (!structSymbol.Functions.TryGetValue(methodName.Value, out FunctionValue? methodBlueprint))
            {
                throw ConstructRuntimeException($"Runtime Error: Static function '{methodName.Value}' not found on struct '{structSymbol.Name}'.");
            }

            FunctionObject functionToExecute = CreateFunctionObject(methodBlueprint);

            int argCountOnStack = _operandStack.Count;
            if (functionToExecute.Arity != argCountOnStack)
            {
                CreateAndThrowRuntimeException($"Runtime Error: Mismatched arity for static function '{functionToExecute.Name}'. Expected {functionToExecute.Arity}, but got {argCountOnStack}.");
            }

            if (!functionToExecute.DefiningScope.IsTheGlobalScope)
            {
                string scopeName = functionToExecute.DefiningScope.Name;
                if (!IsLibraryAllowed(scopeName))
                {
                    CreateAndThrowRuntimeException($"Security Error: Use of the library '{scopeName}' is disallowed by the host application due to library whiteList and or blackList rules.");
                }
            }

            CallFrame newFrame = _callFramePool.Get();
            newFrame.Initialize(this, functionToExecute, _ip, (TempValue)instruction.Lhs);
            int initialArgIndex = 0;

            for (int i = functionToExecute.Arity - 1; i >= 0; i--)
            {
                int paramIndex = initialArgIndex + i;
                RuntimeValue argValue = _operandStack.Pop();

                bool isRef = (functionToExecute.RefMask & (1 << i)) != 0;

                if (isRef)
                {
                    if (argValue.ObjectReference is not ReferenceValue reference)
                    {
                        string argName = functionToExecute.Arguments[i];
                        SignalError($"Internal VM Error: Argument '{argName}' in function: \"{functionToExecute.ToCodeLikeString()}\" must be passed by reference ('ref').");
                        return;
                    }
                    else
                    {
                        newFrame.RefParameterMap[paramIndex] = reference.Reference.RegisterIndex;
                        argValue = GetRuntimeValue(reference.Reference, instruction);
                        newFrame.Registers[paramIndex] = argValue;
                    }
                }
                else
                {
                    newFrame.Registers[paramIndex] = argValue;
                }
            }

            _callStack.Push(newFrame);
            _cachedRegisters = newFrame.Registers;
            _ip = functionToExecute.StartAddress;
        }


        /// <summary>
        /// Handles the RETURN instruction, which ends the current function's execution.
        /// </summary>
        private void ExecuteReturn(InstructionLine instruction)
        {
            RuntimeValue returnValue = GetRuntimeValue(instruction.Lhs, instruction);
            CallFrame finishedFrame = _callStack.Pop();

            if (_callStack.Count == 0)
            {
                // Is this a Coroutine finishing?
                if (_activeCoroutine.Caller != null)
                {
                    CoroutineObject deadCoro = _activeCoroutine;
                    CoroutineObject caller = deadCoro.Caller;

                    deadCoro.State = CoroutineState.Dead;
                    deadCoro.Caller = null;

                    SaveStateToActiveCoroutine();
                    RestoreStateFromCoroutine(caller);

                    if (deadCoro.ResumeTarget is not null)
                    {
                        SetVariableOrRegister(deadCoro.ResumeTarget, returnValue, instruction);
                        deadCoro.ResumeTarget = null;
                    }

                    if (finishedFrame.Function.IsLambda)
                        _functionObjectPool.Return(finishedFrame.Function);

                    _callFramePool.Return(finishedFrame);
                    return;
                }
                else
                {
                    // This means we are terminating the top-level root script.
                    _ip = _byteCode.Count;
                    _callFramePool.Return(finishedFrame);
                    return;
                }
            }

            _cachedRegisters = _callStack.Peek().Registers;
            _cachedWritableCache = _callStack.Peek().WritableCache;

            _cachedRegisters[finishedFrame.DestinationRegister.RegisterIndex] = returnValue;

            if (finishedFrame.RefParameterMap.Count > 0)
            {
                foreach (KeyValuePair<int, int> mapping in finishedFrame.RefParameterMap)
                {
                    int paramIndexInFinishedFrame = mapping.Key;
                    int originalVarIndexInCaller = mapping.Value;

                    RuntimeValue finalValue = finishedFrame.Registers[paramIndexInFinishedFrame];

                    _cachedRegisters[originalVarIndexInCaller] = finalValue;
                }
            }

            _ip = finishedFrame.ReturnAddress;

            // Lambdas don't return thier function object until their' parent call frame returns.
            if (finishedFrame.Function.IsLambda)
            {
                _functionObjectPool.Return(finishedFrame.Function);
            }
            _callFramePool.Return(finishedFrame);
        }

        internal void SetVariableOrRegister(Value target, RuntimeValue value, InstructionLine insn)
        {
            if (target is VariableValue var)
            {
                AssignVariable(var, value, insn, var.IsReadOnly);
                return;
            }

            SetRegister((TempValue)target, value);
        }

        internal RuntimeValue ResolveCharObjectRuntimeValue(char ch)
        {
            CharObject chr = _charObjectPool.Get();
            chr.Initialize(ch);
            return new RuntimeValue(chr);
        }

        internal RuntimeValue ResolveStringObjectRuntimeValue(string strv)
        {
            StringObject str = _stringObjectPool.Get();
            str.Initialize(strv);
            return new RuntimeValue(str);
        }

        private RuntimeValue ResolveRangeObjectRuntimeValue(RuntimeValue start, RuntimeValue end)
        {
            RangeObject range = _rangeObjectPool.Get();
            range.Initialize(start, end);
            return new RuntimeValue(range);
        }

        /// <summary>
        /// Converts a compile-time <see cref="Value"/> from bytecode into a runtime <see cref="RuntimeValue"/>.
        /// </summary>
        internal RuntimeValue GetRuntimeValue(Value val, InstructionLine instruction)
        {
            if (val is TempValue temp)
            {
                return _cachedRegisters[temp.RegisterIndex];
            }

            if (val is VariableValue variable)
            {
                return ResolveVariable(variable, instruction);
            }

            if (val is FunctionValue func)
            {
                // A FunctionValue from the bytecode is just a blueprint.
                // We must convert it into a live, runtime FunctionObject.
                return new RuntimeValue(CreateFunctionObject(func));
            }

            return val switch
            {
                CharValue ch => ResolveCharObjectRuntimeValue(ch.Value),
                EnumValue enumVal => new RuntimeValue(enumVal.Value),
                NumberValue num => num.Type switch
                {
                    NumberValue.NumberType.Integer => new RuntimeValue((int)num.Value),
                    NumberValue.NumberType.Float => new RuntimeValue((float)num.Value),
                    NumberValue.NumberType.Double => new RuntimeValue((double)num.Value),
                    NumberValue.NumberType.Long => new RuntimeValue((long)num.Value),
                    _ => SignalRecoverableErrorAndReturnNil($"Internal VM Error: Unrecognized NumberType '{num.Type}' in bytecode.")
                },
                BooleanValue boolean => new RuntimeValue(boolean.Value),
                NilValue => RuntimeValue.Nil,
                StringValue str => ResolveStringObjectRuntimeValue(str.Value),
                RangeValue range => ResolveRangeObjectRuntimeValue(GetRuntimeValue(range.Start, instruction), GetRuntimeValue(range.End, instruction)),
                LambdaValue lambda => new RuntimeValue(CreateFunctionObject(lambda.Function)),
                _ => SignalRecoverableErrorAndReturnNil($"Internal VM Error: Unrecognized Value type '{val.GetType().Name}' during conversion.")
            };
        }

        /// <summary>
        /// Resolves a variable name to its runtime value by searching the current scope hierarchy.
        /// </summary>
        /// <param name="name">The name of the variable to resolve.</param>
        /// <returns>The <see cref="RuntimeValue"/> associated with the variable name.</returns>
        /// <exception cref="FluenceRuntimeException">Thrown if the variable is not defined in any accessible scope.</exception>
        private RuntimeValue ResolveVariable(VariableValue var, InstructionLine instruction)
        {
            if (instruction.Instruction is InstructionCode.CallFunction
                                        or InstructionCode.CallMethod
                                        or InstructionCode.CallStatic)
            {
                FluenceScope lexicalScope = CurrentFrame.Function?.DefiningScope;

                if (lexicalScope != null)
                {
                    RuntimeValue returnValue;

                    if (lexicalScope.TryResolve(var.Hash, out Symbol symbol))
                    {
                        returnValue = ResolveVariableFromScopeSymbol(symbol, instruction);
                        if (returnValue != RuntimeValue.Nil)
                        {
                            return returnValue;
                        }
                    }

                    if (CurrentFrame.ReturnAddress == _byteCode.Count)
                    {
                        foreach (FluenceScope item in Namespaces.Values)
                        {
                            FluenceScope lexicalScope2 = item;

                            if (lexicalScope2.TryResolve(var.Hash, out Symbol symb))
                            {
                                returnValue = ResolveVariableFromScopeSymbol(symb, instruction);
                                if (returnValue != RuntimeValue.Nil)
                                {
                                    return returnValue;
                                }
                            }
                        }
                    }

                    if (_globalScope.TryResolve(var.Hash, out Symbol globalSym) && globalSym is VariableSymbol varSym)
                    {
                        returnValue = GetRuntimeValue(varSym.Value, instruction);
                        if (returnValue.Type != RuntimeValueType.Nil) return returnValue;
                    }
                }
            }

            if (var.IsGlobal)
            {
                return _globals[var.RegisterIndex];
            }

            return _cachedRegisters[var.RegisterIndex];
        }

        /// <summary>
        /// Resolves complex symbols into RuntimeValues.
        /// </summary>
        /// <param name="symbol">The symbol to resolve from.</param>
        /// <param name="scope">The scope of the symbol</param>
        /// <returns>The <see cref="RuntimeValue"/> resolved from the symbol.</returns>
        private RuntimeValue ResolveVariableFromScopeSymbol(Symbol symbol, InstructionLine instruction)
        {
            if (symbol is FunctionSymbol funcSymbol)
            {
                return new RuntimeValue(CreateFunctionObject(funcSymbol));
            }
            else if (symbol is VariableSymbol variable)
            {
                if (variable.Value is TempValue temp && temp.RegisterIndex != -1)
                {
                    return _cachedRegisters[temp.RegisterIndex];
                }
                else if (variable.Value is VariableValue var2 && var2.RegisterIndex != -1)
                {
                    return var2.IsGlobal ? _globals[var2.RegisterIndex] : _cachedRegisters[var2.RegisterIndex];
                }

                foreach (FluenceScope item in Namespaces.Values)
                {
                    if (item.TryResolve(variable.Hash, out Symbol symb))
                    {
                        return GetRuntimeValue(((VariableSymbol)symb).Value, instruction);
                    }
                }

                if (_globalVariableRegister.TryGetValue(symbol.Name, out VariableValue var))
                {
                    return _globals[var.RegisterIndex];
                }
            }

            return RuntimeValue.Nil;
        }

        /// <summary>
        /// Retrieves or creates a FunctionObject and initializes it from a given <see cref="FunctionSymbol"/> object.
        /// </summary>
        /// <param name="funcSymbol">The blueprint for the <see cref="FunctionSymbol"/> to create.</param>
        /// <returns>The initialized <see cref="FunctionObject"/>.</returns>
        internal FunctionObject CreateFunctionObject(FunctionSymbol funcSymbol)
        {
            FunctionObject func = _functionObjectPool.Get();

            if (funcSymbol.IsIntrinsic)
            {
                func.Initialize(funcSymbol.Name, funcSymbol.Arity, funcSymbol.IntrinsicBody!, funcSymbol.DefiningScope, funcSymbol);
                return func;
            }

            func.Initialize(funcSymbol);
            return func;
        }

        /// <summary>
        /// Retrieves or creates a FunctionObject and initializes it from a given <see cref="FunctionValue"/> object.
        /// </summary>
        /// <param name="funcSymbol">The blueprint for the <see cref="FunctionObject"/> to create.</param>
        /// <returns>The initialized <see cref="FunctionObject"/>.</returns>
        private FunctionObject CreateFunctionObject(FunctionValue funcValue)
        {
            FunctionObject func = _functionObjectPool.Get();
            func.Initialize(funcValue);
            return func;
        }

        /// <summary>
        /// Attemps to return the current object reference of the temporary register to its appropriate pool if available
        /// for further reuse.
        /// </summary>
        /// <param name="register"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void TryReturnRegisterReferenceToPool(TempValue register)
        {
            RuntimeValue registerValue = _cachedRegisters[register.RegisterIndex];

            switch (registerValue.ObjectReference)
            {
                case RangeObject range:
                    _rangeObjectPool.Return(range);
                    break;
                case IteratorObject iter:
                    _iteratorObjectPool.Return(iter);
                    break;
                case CharObject chr:
                    _charObjectPool.Return(chr);
                    break;
            }
        }

        /// <summary>
        /// Attemps to return the current object reference of the temporary register to its appropriate pool if available
        /// for further reuse.
        /// </summary>
        /// <param name="registerIndex">The index of the register the value of which needs to be returned to its pool.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void TryReturnRegisterReferenceToPool(int registerIndex)
        {
            RuntimeValue registerValue = _cachedRegisters[registerIndex];

            switch (registerValue.ObjectReference)
            {
                case RangeObject range:
                    _rangeObjectPool.Return(range);
                    break;
                case IteratorObject iter:
                    _iteratorObjectPool.Return(iter);
                    break;
                case CharObject chr:
                    _charObjectPool.Return(chr);
                    break;
            }
        }

        /// <summary>
        /// Writes a value to a specified temporary register in the current call frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetRegister(TempValue destination, RuntimeValue value)
        {
            _cachedRegisters[destination.RegisterIndex] = value;
        }

        /// <summary>
        /// Checks whether the given local or global variable is a readonly solid variable.
        /// </summary>
        /// <param name="variable">The variable to check.</param>
        /// <returns>True if the variable is readonly.</returns>
        internal bool VariableIsReadonly(VariableValue variable)
        {
            if (variable.IsGlobal)
            {
                return _globalWritableCache[variable.RegisterIndex];
            }

            return _cachedWritableCache[variable.RegisterIndex];
        }

        /// <summary>
        /// Assigns a value to a variable, going through all the necessary readonly rule checks.
        /// </summary>
        private void AssignVariable(VariableValue var, RuntimeValue value, InstructionLine insn, bool readOnly = false)
        {
            if (insn != null && insn.AssignsVariableSafely)
            {
                SetVariable(var, value);
                return;
            }

            // Write caches initialized as [ false, false, false, ... ] by default.
            // True in the cache means not readonly.
            // First assignment is always not readonly.

            if (var.IsGlobal)
            {
                bool isReadonlyGlobalVar = _globalWritableCache[var.RegisterIndex];

                if (isReadonlyGlobalVar)
                {
                    throw CreateRuntimeException($"Runtime Error: Cannot assign to the readonly or solid variable '{var.Name}'.");
                }
                else if (readOnly)
                {
                    _globalWritableCache[var.RegisterIndex] = true;
                }
            }
            else
            {
                bool isReadonlyLocalVar = CurrentFrame.WritableCache[var.RegisterIndex];

                if (isReadonlyLocalVar)
                {
                    throw CreateRuntimeException($"Runtime Error: Cannot assign to the readonly or solid variable '{var.Name}'.");
                }
                else if (readOnly)
                {
                    CurrentFrame.WritableCache[var.RegisterIndex] = true;
                }
            }

            if (insn != null)
            {
                insn.AssignsVariableSafely = true;
            }

            SetVariable(var, value);
        }

        /// <summary>
        /// Manually sets the readonly flag for a global or local variable.
        /// </summary>
        /// <param name="var">The variable to set the flag for.</param>
        internal void MarkWritableCacheAsReadonly(VariableValue var)
        {
            if (var.IsGlobal)
            {
                _globalWritableCache[var.RegisterIndex] = true;
                return;
            }

            _cachedWritableCache[var.RegisterIndex] = true;
        }

        /// <summary>
        /// Sets the value of a variable directly, avoiding extra checks.
        /// </summary>
        /// <param name="var">The Variable.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetVariable(VariableValue var, RuntimeValue value)
        {
            if (var.IsGlobal)
            {
                _globals[var.RegisterIndex] = value;
                return;
            }

            _cachedRegisters[var.RegisterIndex] = value;
        }

        /// <summary>
        /// Helper to safely convert any numeric RuntimeValue to a long for bitwise operations.
        /// </summary>
        private long ToLong(RuntimeValue value)
        {
            if (value.Type != RuntimeValueType.Number)
            {
                return SignalError<long>($"Internal VM Error: Bitwise operations require integer numbers, but got a {value.Type}.");
            }

            return value.NumberType switch
            {
                RuntimeNumberType.Int => value.IntValue,
                RuntimeNumberType.Long => value.LongValue,
                // Floats and doubles are truncated (decimal part is cut off).
                RuntimeNumberType.Float => (long)value.FloatValue,
                RuntimeNumberType.Double => (long)value.DoubleValue,
                _ => SignalError<long>("Internal VM Error: Unhandled number type in bitwise op."),
            };
        }

        /// <summary>
        /// Handles the logic for repeating a list's elements N times.
        /// </summary>
        private RuntimeValue HandleListRepetition(ListObject list, RuntimeValue num)
        {
            if (num.NumberType is not RuntimeNumberType.Int and not RuntimeNumberType.Long)
            {
                return SignalRecoverableErrorAndReturnNil($"Internal VM Error: Cannot multiply a list by a non-integer number ({num.NumberType}).");
            }

            int count = num.IntValue;
            ListObject repeatedList = new ListObject();

            if (count > 0)
            {
                repeatedList.Elements.Capacity = list.Elements.Count * count;
                for (int i = 0; i < count; i++)
                {
                    foreach (RuntimeValue element in list.Elements)
                    {
                        if (element.ObjectReference is ICloneableFluenceObject cloneable)
                        {
                            repeatedList.Elements.Add(new RuntimeValue(cloneable.CloneObject()));
                        }
                        else
                        {
                            repeatedList.Elements.Add(element);
                        }
                    }
                }
            }

            // Multiplying by 0 or a negative number results in an empty list.
            return new RuntimeValue(repeatedList);
        }

        /// <summary>
        /// Determines if a library is allowed to be loaded based on the current
        /// whitelist and blacklist rules.
        /// </summary>
        /// <param name="libraryName">The name of the library being checked.</param>
        /// <returns>True if the library is permitted to be used.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLibraryAllowed(string libraryName)
        {
            if (_disallowedIntrinsicLibraries.Contains(libraryName))
            {
                return false;
            }

            if (_allowedIntrinsicLibraries.Count > 0 && !_allowedIntrinsicLibraries.Contains(libraryName))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Helper for string/list repetition. Throws a user-friendly runtime exception for non-integer multipliers.
        /// </summary>
        private RuntimeValue HandleStringRepetition(StringObject str, RuntimeValue num)
        {
            if (num.NumberType is not RuntimeNumberType.Int and not RuntimeNumberType.Long)
            {
                return SignalRecoverableErrorAndReturnNil($"Internal VM Error: Cannot multiply a string by a non-integer number (got {num.NumberType}).");
            }

            int count = num.IntValue;

            // Multiplying by 0 or a negative number results in an empty string.
            if (count <= 0)
            {
                return ResolveStringObjectRuntimeValue("");
            }

            StringBuilder sb = new StringBuilder((str.Value?.Length ?? 0) * count);
            for (int i = 0; i < count; i++)
            {
                sb.Append(str.Value);
            }

            return ResolveStringObjectRuntimeValue(sb.ToString());
        }

        /// <summary>
        /// Gets a detailed, user-friendly type name for a runtime value.
        /// </summary>
        internal static string GetDetailedTypeName(RuntimeValue value)
        {
            if (value.Type == RuntimeValueType.Object && value.ObjectReference != null)
            {
                if (value.ObjectReference is Wrapper wrapper)
                {
                    return wrapper.Instance.GetType().Name;
                }
                return value.ObjectReference.GetType().Name;
            }

            // For primitives.
            return value.Type.ToString();
        }

        /// <summary>
        /// Handles a runtime error that is allowed to be catched. If a try-catch block is active, it redirects the instruction pointer
        /// to the catch block. Otherwise, it throws an unhandled exception, terminating the VM.
        /// </summary>
        /// <param name="exception">The error message.</param>
        public void SignalError(string exception, RuntimeExceptionType exceptionType = RuntimeExceptionType.NonSpecific)
        {
            if (_tryCatchBlocks.Count > 0)
            {
                TryCatchValue tryCatchContext = _tryCatchBlocks.Pop();

                // Error in try block.
                if (_ip < tryCatchContext.TryGoToIndex)
                {
                    if (tryCatchContext.HasExceptionVar && !string.IsNullOrEmpty(tryCatchContext.ExceptionVarName))
                    {
                        _cachedRegisters[tryCatchContext.ExceptionAsVarRegisterIndex] = new RuntimeValue(new ExceptionObject(exception));
                    }

                    // Jumps to catch block.
                    _ip = tryCatchContext.TryGoToIndex;
                    tryCatchContext.CaughtException = true;

                    // We empty any and all pushed arguments before jumping to catch ( if present ) and or beyond.
                    // This way arguments pushed for whatever reason in the try block won't be left behind.
                    _operandStack.Clear();

                    _tryCatchBlocks.Push(tryCatchContext);
                }
                // Error in catch block.
                else if (_ip > tryCatchContext.TryGoToIndex && _ip < tryCatchContext.CatchGoToIndex)
                {
                    throw CreateRuntimeException(exception, exceptionType);
                }
            }
            else
            {
                throw CreateRuntimeException(exception, exceptionType);
            }
        }

        /// <summary>
        /// Handles a runtime error. If a try-catch block is active, it redirects the instruction pointer
        /// to the catch block. Otherwise, it throws an unhandled exception, terminating the VM.
        /// Accepts a <see cref="T"/> type to satisfy the compiler.
        /// </summary>
        /// <param name="exception">The error message.</param>
        internal T SignalError<T>(string exception, RuntimeExceptionType exceptionType = RuntimeExceptionType.NonSpecific)
        {
            SignalError(exception, exceptionType);
            return default;
        }

        /// <summary>
        /// Handles a runtime error. If a try-catch block is active, it redirects the instruction pointer
        /// to the catch block. Otherwise, it throws an unhandled exception, terminating the VM.
        /// Returns Nil if the exception has been caught.
        /// </summary>
        internal RuntimeValue SignalRecoverableErrorAndReturnNil(string message, RuntimeExceptionType exceptionType = RuntimeExceptionType.NonSpecific)
        {
            SignalError(message, exceptionType);
            return RuntimeValue.Nil;
        }

        /// <summary>
        /// Creates and throws a runtime error that can not be caught.
        /// </summary>
        /// <param name="exception">The exception message.</param>
        /// <param name="exceptionType">The type of the exception.</param>
        internal void CreateAndThrowRuntimeException(string exception, RuntimeExceptionType exceptionType = RuntimeExceptionType.NonSpecific) => throw CreateRuntimeException(exception, exceptionType);

        /// <summary>
        /// Creates a runtime error that can not be caught.
        /// </summary>
        /// <param name="exception">The exception message.</param>
        /// <param name="exceptionType">The type of the exception.</param>
        internal FluenceRuntimeException ConstructRuntimeException(string exception, RuntimeExceptionType exceptionType = RuntimeExceptionType.NonSpecific) => CreateRuntimeException(exception, exceptionType);

        //  These exceptions should not be catchable
        //
        //  1. Readonly assignment.
        //  2. wrong argument count in function call.
        //  3. undefined variable.
        //  4. undefined function.
        //  5. calling non function.
        //  6. wrong struct field/function.
        //  8. invalid return.

        /// <summary>
        /// Creates and logs to the console a highly detailed exception with the current state of the VM.
        /// </summary>
        /// <param name="exception">The exception message.</param>
        private FluenceRuntimeException CreateRuntimeException(string exception, RuntimeExceptionType excType = RuntimeExceptionType.NonSpecific)
        {
            VMDebugContext debugCtx = new VMDebugContext(this, CurrentFrame, _byteCode, _operandStack, _callStack.Count);
            List<StackFrameInfo> stackFrames = new List<StackFrameInfo>();

            while (_callStack.Count != 0)
            {
                CallFrame frame = _callStack.Pop();
                InstructionLine insn = _byteCode[_ip];
                string fileName = _parser.IsMultiFileProject ? _parser.CurrentParseState.ProjectFilePaths[insn.ProjectFileIndex] : "Script";

                stackFrames.Add(new StackFrameInfo(frame.Function.Name, fileName, frame.Function.StartAddressInSource));
            }

            RuntimeExceptionContext context = new RuntimeExceptionContext()
            {
                DebugContext = debugCtx,
                ExceptionMessage = exception,
                InstructionLine = debugCtx.CurrentInstruction,
                StackTraces = stackFrames,
                Parser = _parser,
                ExceptionType = excType,
            };

            return new FluenceRuntimeException(exception, context);
        }

        public RuntimeValue CreateValue(int value) => new RuntimeValue(value);

        public RuntimeValue CreateValue(float value) => new RuntimeValue(value);

        public RuntimeValue CreateValue(double value) => new RuntimeValue(value);

        public RuntimeValue CreateValue(bool value) => new RuntimeValue(value);

        public RuntimeValue CreateValue(string value) => ResolveStringObjectRuntimeValue(value);
    }
}