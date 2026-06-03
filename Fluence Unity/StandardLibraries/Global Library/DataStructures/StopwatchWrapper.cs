using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    /// <summary>
    /// Represents a <see cref="System.Diagnostics.Stopwatch"/>.
    /// Exposed as a 'Stopwatch' type in the language for high-precision timing.
    /// </summary>
    internal static class StopwatchWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static StopwatchWrapper()
        {
            _instanceMethods["start__0"] = Start;
            _instanceMethods["stop__0"] = Stop;
            _instanceMethods["reset__0"] = Reset;
            _instanceMethods["restart__0"] = Restart;
            _instanceMethods["is_running__0"] = IsRunning;
            _instanceMethods["elapsed_ms__0"] = ElapsedMilliseconds;
            _instanceMethods["elapsed_ticks__0"] = ElapsedTicks;
            _instanceMethods["elapsed__0"] = Elapsed;
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
            {
                new FunctionSymbol("Stopwatch__0", 0, (vm, argCount) =>
                {
                    Stopwatch sw = new Stopwatch();
                    Wrapper wrapper = new Wrapper(sw, _instanceMethods);
                    return new RuntimeValue(wrapper);

                }, scope, new List<string>()),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Stopwatch GetInstance(FluenceVirtualMachine vm, RuntimeValue self)
        {
            if (self.ObjectReference is Wrapper wrapper && wrapper.Instance is Stopwatch sw)
            {
                return sw;
            }

            return vm.SignalError<Stopwatch>("Internal Error: Stopwatch method called on a non-Stopwatch object.");
        }

        private static RuntimeValue Start(FluenceVirtualMachine vm, RuntimeValue self)
        {
            GetInstance(vm, self).Start();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Stop(FluenceVirtualMachine vm, RuntimeValue self)
        {
            GetInstance(vm, self).Stop();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Reset(FluenceVirtualMachine vm, RuntimeValue self)
        {
            GetInstance(vm, self).Reset();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Restart(FluenceVirtualMachine vm, RuntimeValue self)
        {
            GetInstance(vm, self).Restart();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue IsRunning(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return new RuntimeValue(GetInstance(vm, self).IsRunning);
        }

        private static RuntimeValue ElapsedMilliseconds(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return new RuntimeValue(GetInstance(vm, self).ElapsedMilliseconds);
        }

        private static RuntimeValue ElapsedTicks(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return new RuntimeValue(GetInstance(vm, self).ElapsedTicks);
        }

        private static RuntimeValue Elapsed(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return vm.ResolveStringObjectRuntimeValue(GetInstance(vm, self).Elapsed.ToString());
        }
    }
}