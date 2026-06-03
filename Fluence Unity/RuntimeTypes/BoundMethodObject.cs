namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// Represents a "closure" that binds an instance of an object (the receiver).
    /// </summary>
    internal sealed class BoundMethodObject
    {
        internal InstanceObject Receiver;
        internal FunctionValue Method;

        internal BoundMethodObject(InstanceObject receiver, FunctionValue method)
        {
            Receiver = receiver;
            Method = method;
        }

        public override string ToString() => $"<bound method {Method.Name} of {Receiver}>";
    }
}