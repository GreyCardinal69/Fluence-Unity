using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Collections.Generic;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    internal static class MethodMetadataWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static MethodMetadataWrapper()
        {
            _instanceMethods["name__0"] = GetName;
            _instanceMethods["mangled_name__0"] = GetMangledName;
            _instanceMethods["arity__0"] = GetArity;
            _instanceMethods["get_parameters__0"] = GetParameters;
            _instanceMethods["is_ctor__0"] = IsConstructor;
            _instanceMethods["signature__0"] = GetSignature;
        }

        internal static RuntimeValue Create(MethodMetadata metadata)
        {
            Wrapper wrapper = new Wrapper(metadata, _instanceMethods);
            return new RuntimeValue(wrapper);
        }

        private static RuntimeValue GetName(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(metadata.BaseName);
        }

        private static RuntimeValue GetMangledName(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(metadata.MangledName);
        }

        private static RuntimeValue GetArity(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.Arity);
        }

        private static RuntimeValue IsConstructor(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;
            return metadata.IsCtor ? RuntimeValue.True : RuntimeValue.False;
        }

        private static RuntimeValue GetSignature(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(metadata.GetSignature());
        }

        private static RuntimeValue GetParameters(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;

            ListObject list = new ListObject();

            for (int i = 0; i < metadata.Parameters.Count; i++)
            {
                string parameterName = metadata.Parameters[i];
                bool isRef = (metadata.RefMask & (1 << i)) != 0;

                list.Elements.Add(ParameterMetadataWrapper.Create(new ParameterMetadata()
                {
                    ByRef = isRef,
                    Name = parameterName
                }));
            }

            return new RuntimeValue(list);
        }
    }
}