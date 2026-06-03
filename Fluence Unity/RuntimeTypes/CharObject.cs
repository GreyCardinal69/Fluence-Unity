using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// Represents a heap-allocated char object in the Fluence VM.
    /// </summary>
    internal sealed class CharObject : IFluenceObject
    {
        internal char Value { get; private set; }

        internal CharObject(char value) => Value = value;

        public CharObject() { }

        internal void Initialize(char value) => Value = value;

        /// <inheritdoc/>
        bool IFluenceObject.TryGetIntrinsicMethod(string name, out IntrinsicRuntimeMethod method)
        {
            method = name switch
            {
                _ => null!
            };
            return method != null;
        }

        public override string ToString() => Value.ToString();
    }
}