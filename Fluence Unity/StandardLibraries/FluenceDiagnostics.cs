using Fluence.Unity.RuntimeTypes;
using static Fluence.Unity.FluenceInterpreter;

namespace Fluence.Unity
{
    /// <summary>
    /// A standard library that provides functionality for diagnostics.
    /// </summary>
    internal static class FluenceDiagnostics
    {
        internal const string NamespaceName = "FluenceDiagnostics";

        internal static void Register(FluenceScope diagnosticsNamespace, TextOutputMethod outputLine, TextInputMethod input, TextOutputMethod errorOutput)
        {
            StructSymbol debugStruct = new StructSymbol("Debug", diagnosticsNamespace);
            diagnosticsNamespace.Declare("Debug".GetHashCode(), debugStruct);

            debugStruct.StaticIntrinsics.Add("assert__2", new FunctionSymbol("assert__2", 2, (vm, args) =>
            {
                RuntimeValue message = vm.PopStack();
                RuntimeValue condition = vm.PopStack();

                if (!condition.IsTruthy)
                {
                    string msg = message.Type == RuntimeValueType.Object && message.ObjectReference is StringObject str
                        ? str.Value
                        : "Assertion failed!";

                    vm.SignalError($"[Assertion Failed] {msg}");
                }
                return RuntimeValue.Nil;
            }, diagnosticsNamespace, new List<string>() { "condition", "message" }));

            debugStruct.StaticIntrinsics.Add("log_info__1", new FunctionSymbol("log_info__1", 1, (vm, args) =>
            {
                RuntimeValue val = vm.PopStack();
                outputLine($"[INFO] {val}");
                return RuntimeValue.Nil;
            }, diagnosticsNamespace, new List<string>() { "message" }));

            debugStruct.StaticIntrinsics.Add("log_warning__1", new FunctionSymbol("log_warning__1", 1, (vm, args) =>
            {
                RuntimeValue val = vm.PopStack();
                outputLine($"[WARNING] {val}");
                return RuntimeValue.Nil;
            }, diagnosticsNamespace, new List<string>() { "message" }));

            debugStruct.StaticIntrinsics.Add("log_error__1", new FunctionSymbol("log_error__1", 1, (vm, args) =>
            {
                RuntimeValue val = vm.PopStack();
                errorOutput($"[ERROR] {val}");
                return RuntimeValue.Nil;
            }, diagnosticsNamespace, new List<string>() { "message" }));

            StructSymbol profilerStruct = new StructSymbol("Profiler", diagnosticsNamespace);
            diagnosticsNamespace.Declare("Profiler".GetHashCode(), profilerStruct);

            profilerStruct.StaticIntrinsics.Add("get_system_memory__0", new FunctionSymbol("get_system_memory__0", 0, (vm, args) =>
            {
                long allocatedBytes = GC.GetTotalMemory(forceFullCollection: false);
                double allocatedMb = (double)allocatedBytes / (1024 * 1024);
                return new RuntimeValue(allocatedMb);
            }, diagnosticsNamespace, new List<string>() { }));
        }
    }
}