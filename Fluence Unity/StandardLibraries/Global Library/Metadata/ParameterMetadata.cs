using Fluence.Unity.RuntimeTypes;

namespace Fluence.Unity.Global
{
    internal sealed class ParameterMetadata
    {
        internal string Name
        {
            get; set;
        }

        internal bool ByRef
        {
            get; set;
        }

        // TO DO
        internal bool HasDefaultValue
        {
            get; set;
        }

        internal RuntimeValue DefualtValue
        {
            get; set;
        }
    }
}