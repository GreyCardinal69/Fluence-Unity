using Fluence.Unity.RuntimeTypes;

namespace Fluence.Unity.VirtualMachine
{
    /// <summary>
    /// Represents the execution context of a single function call on the call stack.
    /// Manages local registers, temporary values, return information, and reference parameter tracking.
    /// </summary>
    internal sealed class CallFrame
    {
        internal readonly struct RefMappingInfo
        {
            internal readonly int OriginalVarIndex;

            internal readonly bool IsOriginalVarGlobal;


            internal RefMappingInfo(int originalVarIndex, bool isOriginalVarGlobal)
            {
                OriginalVarIndex = originalVarIndex;
                IsOriginalVarGlobal = isOriginalVarGlobal;
            }
        }

        /// <summary>
        /// Local registers containing function local variables and temporary values.
        /// </summary>
        internal RuntimeValue[] Registers { get; private set; }

        /// <summary>
        /// Tracks writability status of each register to enforce readonly rules.
        /// </summary>
        internal bool[] WritableCache { get; private set; }

        /// <summary>
        /// The number of total register slots the current frame is allocated to have.
        /// </summary>
        internal int RegisterCount { get; private set; }

        /// <summary>
        /// Destination register for the function's return value.
        /// </summary>
        internal TempValue DestinationRegister { get; private set; }

        /// <summary>
        /// The function object being executed in this call frame.
        /// </summary>
        internal FunctionObject Function { get; private set; }

        /// <summary>
        /// Instruction pointer for return address after function completion.
        /// </summary>
        internal int ReturnAddress { get; private set; }

        /// <summary>
        /// Maps reference parameters to their corresponding register indices in the parent call frame.
        /// </summary>
        internal Dictionary<int, int> RefParameterMap { get; } = new();

        public CallFrame()
        {
            Registers = new RuntimeValue[0];
            WritableCache = new bool[0];
            RegisterCount = 0;
        }

        /// <summary>
        /// Initializes this call frame for the execution of the specified function.
        /// Allocates and initializes registers based on the function's requirements.
        /// </summary>
        /// <param name="function">The function to execute in this call frame.</param>
        /// <param name="returnAddress">The instruction address to return to after function completion.</param>
        /// <param name="destination">The temporary register to store the return value, if any.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="function"/> is null.</exception>
        public void Initialize(FluenceVirtualMachine vm, FunctionObject function, int returnAddress, TempValue destination)
        {
            if (function is null)
            {
                throw vm.ConstructRuntimeException("Internal VM Error: Can not initialize a new CallFrame with a null function blueprint.");
            }

            int requiredSize = function.TotalRegisterSlots;

            if (Registers.Length < requiredSize || WritableCache.Length < requiredSize)
            {
                Registers = new RuntimeValue[requiredSize];
                WritableCache = new bool[requiredSize];
            }

            RegisterCount = requiredSize;
            Function = function;
            Function.TotalRegisterSlots = function.TotalRegisterSlots;
            ReturnAddress = returnAddress;
            DestinationRegister = destination;

            if (requiredSize > 0)
            {
                Array.Clear(WritableCache, 0, requiredSize);
            }
        }
    }
}