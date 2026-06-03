using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// An interface for built-in object types in Fluence which feature
    /// built-in intrinsic functions.
    /// </summary>
    internal interface IFluenceObject
    {
        /// <summary>
        /// Attempts to retrieve a native C# method implementation by its name.
        /// </summary>
        /// <param name="name">The name of the method being called in the script.</param>
        /// <param name="method">When this method returns, contains the C# delegate for the method if found; otherwise, null.</param>
        /// <returns><c>true</c> if an intrinsic method with the specified name was found; otherwise, <c>false</c>.</returns>
        internal bool TryGetIntrinsicMethod(string name, out IntrinsicRuntimeMethod method);
    }
}