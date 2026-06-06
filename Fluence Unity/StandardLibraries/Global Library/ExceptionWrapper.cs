using Fluence.Unity.RuntimeTypes;
using System.Collections.Generic;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    /// <summary>Represents an exception thrown from a fluence script itself.</summary>
    internal sealed class ScriptException
    {
        internal string Message { get; set; }

        internal ScriptException(string message)
        {
            Message = message;
        }
    }

    /// <summary> Represents the core exception class of the language.</summary>
    internal static class ExceptionWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static ExceptionWrapper()
        {
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
                {
                new FunctionSymbol("Exception__1", 1, (vm, argCount) =>
                {
                    RuntimeValue arg = vm.PopStack();
                    ScriptException stackInstance;

                    if (arg.ObjectReference is not StringObject str)
                    {
                        return vm.SignalRecoverableErrorAndReturnNil("Exception constructor accepts only a string parameter.");
                    }

                    stackInstance = new ScriptException(str.Value);
                    Wrapper wrapper = new Wrapper(stackInstance, _instanceMethods);

                    wrapper.InstanceFields.Add("message", new RuntimeValue(str));
                    wrapper.IntrinsicSymbolMarker = scope.Symbols["Exception".GetHashCode()];

                    return new RuntimeValue(wrapper);
                }, scope, new List<string>() { "message" }),
            };
        }
    }
}