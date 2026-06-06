using Fluence.Unity.VirtualMachine;
using System.Collections.Generic;

namespace Fluence.Unity.RuntimeTypes
{
    internal enum CoroutineState
    {
        Suspended,
        Running,
        Dead
    }

    /// <summary>
    /// Represents an isolated execution thread that can be paused and resumed.
    /// </summary>
    internal sealed class CoroutineObject : IFluenceObject
    {
        internal CoroutineState State { get; set; } = CoroutineState.Suspended;

        internal Stack<CallFrame> CallStack { get; private set; } = new Stack<CallFrame>();
        internal Stack<RuntimeValue> OperandStack { get; private set; } = new Stack<RuntimeValue>();
        internal Stack<TryCatchValue> TryCatchBlocks { get; private set; } = new Stack<TryCatchValue>();

        internal int InstructionPointer { get; set; }

        internal CoroutineObject? Caller { get; set; }

        internal Value? ResumeTarget { get; set; }
        internal Value? YieldTarget { get; set; }

        /// <summary>
        /// Intrinsic method to check if the coroutine is finished.
        /// </summary>
        private static RuntimeValue IsDead(FluenceVirtualMachine vm, RuntimeValue self)
        {
            CoroutineObject coro = self.As<CoroutineObject>();
            return new RuntimeValue(coro.State == CoroutineState.Dead);
        }

        /// <summary>
        /// Intrinsic method to get the current state as a string.
        /// </summary>
        private static RuntimeValue GetState(FluenceVirtualMachine vm, RuntimeValue self)
        {
            CoroutineObject coro = self.As<CoroutineObject>();
            return vm.ResolveStringObjectRuntimeValue(coro.State.ToString().ToLowerInvariant());
        }

        bool IFluenceObject.TryGetIntrinsicMethod(string name, out FluenceVirtualMachine.IntrinsicRuntimeMethod method)
        {
            method = name switch
            {
                "is_dead__0" => IsDead,
                "state__0" => GetState,
                _ => null!
            };
            return method != null;
        }

        public override string ToString() => $"Coroutine ({State})";
    }
}