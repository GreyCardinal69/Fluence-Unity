using Fluence.Unity.RuntimeTypes;
using System.Collections.Generic;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    /// <summary>
    /// A generic wrapper that allows a native C# object
    /// to be exposed and used within the Fluence runtime.
    /// </summary>
    internal sealed class Wrapper : IFluenceObject
    {
        /// <summary>The actual C# object being wrapped.</summary>
        internal object Instance { get; }

        /// <summary>
        /// A dictionary of "intrinsic methods" that maps a Fluence method name
        /// to a C# delegate that can be called by the VM.
        /// </summary>
        private readonly Dictionary<string, IntrinsicRuntimeMethod> _methods;

        /// <summary>
        /// A dictionary of the fields' names and their values of the current Instance this wrapper carries.
        /// </summary>
        internal Dictionary<string, RuntimeValue> InstanceFields { get; } = new Dictionary<string, RuntimeValue>();

        /// <summary>
        /// Holds a reference to the <see cref="IntrinsicStructSymbol"/> of the Instance this wrapper carries, if it has one.
        /// </summary>
        internal Symbol IntrinsicSymbolMarker { get; set; }

        internal Wrapper(object instance, Dictionary<string, IntrinsicRuntimeMethod> methods)
        {
            Instance = instance;
            _methods = methods;
        }

        /// <inheritdoc/>
        bool IFluenceObject.TryGetIntrinsicMethod(string name, out IntrinsicRuntimeMethod method)
        {
            return _methods.TryGetValue(name, out method!);
        }

        public override string ToString() => Instance.ToString();

        public override bool Equals(object? obj) => obj is Wrapper other && Instance.Equals(other.Instance);

        public override int GetHashCode() => Instance.GetHashCode();
    }
}