using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Collections.Generic;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    internal static class ParameterMetadataWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static ParameterMetadataWrapper()
        {
            _instanceMethods["name__0"] = GetName;
            _instanceMethods["is_ref__0"] = IsByRef;
            _instanceMethods["has_default_value__0"] = HasDefaultValue;
            _instanceMethods["get_default_value__0"] = GetDefaultValue;
        }

        internal static RuntimeValue Create(ParameterMetadata metadata)
        {
            Wrapper wrapper = new Wrapper(metadata, _instanceMethods);
            return new RuntimeValue(wrapper);
        }

        private static RuntimeValue GetName(FluenceVirtualMachine vm, RuntimeValue self)
        {
            ParameterMetadata metadata = (ParameterMetadata)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(metadata.Name);
        }

        private static RuntimeValue IsByRef(FluenceVirtualMachine vm, RuntimeValue self)
        {
            ParameterMetadata metadata = (ParameterMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.ByRef);
        }

        private static RuntimeValue HasDefaultValue(FluenceVirtualMachine vm, RuntimeValue self)
        {
            ParameterMetadata metadata = (ParameterMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.HasDefaultValue);
        }
        private static RuntimeValue GetDefaultValue(FluenceVirtualMachine vm, RuntimeValue self)
        {
            ParameterMetadata metadata = (ParameterMetadata)self.As<Wrapper>().Instance;
            return metadata.DefualtValue;
        }
    }
}