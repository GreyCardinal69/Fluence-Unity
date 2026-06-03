using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Globalization;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class IntrinsicHelpers
    {
        internal static string GetStringArg(FluenceVirtualMachine vm, string funcName)
        {
            RuntimeValue pathRv = vm.PopStack();

            if (pathRv.ObjectReference is not StringObject pathObj || string.IsNullOrEmpty(pathObj.Value))
            {
                return vm.SignalError<string>($"Invalid argument for function: \"{funcName}\". Argument must be a non-empty string.");
            }

            return pathObj.Value;
        }

        internal static (string, string) GetTwoStringArgs(FluenceVirtualMachine vm, string funcName)
        {
            RuntimeValue arg2Rv = vm.PopStack();
            RuntimeValue arg1Rv = vm.PopStack();

            if (arg1Rv.ObjectReference is not StringObject arg1Obj || string.IsNullOrEmpty(arg1Obj.Value) ||
                arg2Rv.ObjectReference is not StringObject arg2Obj || string.IsNullOrEmpty(arg2Obj.Value))
            {
                return vm.SignalError<(string, string)>($"Invalid argument(s) for function: \"{funcName}\". Both arguments must be non-empty strings.");
            }

            return (arg1Obj.Value, arg2Obj.Value);
        }

        internal static string GetRuntimeTypeName(RuntimeValue value)
        {
            switch (value.Type)
            {
                case RuntimeValueType.Nil: return "nil";
                case RuntimeValueType.Boolean: return "bool";
                case RuntimeValueType.Number:
                    return value.NumberType.ToString().ToLower(CultureInfo.InvariantCulture);
                case RuntimeValueType.Object:
                    if (value.ObjectReference == null) return "nil";
                    return value.ObjectReference switch
                    {
                        CharObject => "Char",
                        StringObject => "string",
                        ListObject => "list",
                        FunctionObject => "function",
                        InstanceObject inst => inst.Class.Name,
                        Wrapper fo => fo.Instance.GetType().Name,
                        _ => value.ToString(),
                    };
                default: return "unknown";
            }
        }

        internal static string ConvertRuntimeValueToString(FluenceVirtualMachine vm, RuntimeValue val)
        {
            if (val.ObjectReference is IFluenceObject fluenceObject)
            {
                if (fluenceObject.TryGetIntrinsicMethod("to_string__0", out IntrinsicRuntimeMethod? intrinsicMethod))
                {
                    return intrinsicMethod(vm, val).ToString();
                }
            }

            if (val.ObjectReference is InstanceObject instance && instance.Class.Functions.TryGetValue("to_string__0", out FunctionValue func))
            {
                RuntimeValue result = vm.ExecuteManualMethodCall(instance, func);

                // The struct's to_string() must return a string object. If not, it's a runtime error.
                if (result.ObjectReference is not StringObject stringResult)
                {
                    throw vm.ConstructRuntimeException($"Runtime Error: The 'to_string' method for '{instance.Class.Name}' must return a string, but it returned a '{GetDetailedTypeName(result)}'.");
                }

                return stringResult.Value;
            }

            return val.ToString();
        }
    }
}