namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// Represents a heap-allocated range object, typically used in 'for-in' loops.
    /// </summary>
    internal sealed class RangeObject
    {
        internal RuntimeValue Start { get; private set; }
        internal RuntimeValue End { get; private set; }

        public RangeObject() { }

        internal void Initialize(RuntimeValue start, RuntimeValue end)
        {
            Start = start;
            End = end;
        }

        public override string ToString() => $"{Start}..{End}";
    }
}