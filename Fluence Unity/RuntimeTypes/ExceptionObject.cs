using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;

namespace Fluence.Unity
{
    /// <summary>
    /// Represents a non-fatal exception that has occured and has been caught in the Fluence VM.
    /// </summary>
    internal sealed class ExceptionObject : IFluenceObject
    {
        internal string Value { get; private set; }

        internal ExceptionObject(string value) => Value = value;

        private RuntimeValue ToString(FluenceVirtualMachine vm, RuntimeValue self) => vm.ResolveStringObjectRuntimeValue(Value);

        /// <inheritdoc/>
        bool IFluenceObject.TryGetIntrinsicMethod(string name, out FluenceVirtualMachine.IntrinsicRuntimeMethod method)
        {
            method = name switch
            {
                "to_string__0" => ToString,
                _ => null!
            };
            return method != null;
        }

        public override string ToString() => $"Exception";
    }
}