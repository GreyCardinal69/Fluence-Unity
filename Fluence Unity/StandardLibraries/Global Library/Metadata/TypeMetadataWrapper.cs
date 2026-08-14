using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    internal static class TypeMetadataWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static TypeMetadataWrapper()
        {
            _instanceMethods["name__0"] = GetName;
            _instanceMethods["full_name__0"] = GetFullName;
            _instanceMethods["category__0"] = GetCategory;
            _instanceMethods["is_primitive__0"] = IsPrimitive;
            _instanceMethods["is_struct__0"] = IsStruct;
            _instanceMethods["is_enum__0"] = IsEnum;
            _instanceMethods["is_collection__0"] = IsCollection;
            _instanceMethods["instance_fields__0"] = GetInstanceFields;
            _instanceMethods["static_fields__0"] = GetStaticSolidFields;
            _instanceMethods["has_instance_field__1"] = HasInstanceField;
            _instanceMethods["has_static_field__1"] = HasStaticField;
            _instanceMethods["constructors__0"] = GetConstructors;
            _instanceMethods["instance_methods__0"] = GetInstanceMethods;
            _instanceMethods["get_all_methods__0"] = GetAllMethods;
            _instanceMethods["find_method__1"] = GetMethodsFromName;
            _instanceMethods["find_method__2"] = GetMethodsFromNameArity;
            _instanceMethods["arity__0"] = GetArity;
            _instanceMethods["parameters__0"] = GetParameters;
            _instanceMethods["parameters_ref__0"] = GetParametersRef;
            _instanceMethods["is_lambda__0"] = IsLambda;

            _instanceMethods["implements__1"] = ImplementsType;
            _instanceMethods["get_field_value__2"] = GetFieldValue;
            _instanceMethods["set_field_value__3"] = SetFieldValue;
            _instanceMethods["get_static_value__1"] = GetStaticValue;
            _instanceMethods["set_static_value__2"] = SetStaticValue;
        }

        internal static RuntimeValue Create(TypeMetadata metadata)
        {
            Wrapper foreignObject = new Wrapper(metadata, _instanceMethods);
            return new RuntimeValue(foreignObject);
        }

        private static RuntimeValue GetName(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(metadata.Name);
        }

        private static RuntimeValue GetFullName(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(metadata.FullName);
        }

        private static RuntimeValue GetArity(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.Arity);
        }

        private static RuntimeValue GetCategory(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue((int)metadata.Category);
        }

        private static RuntimeValue IsPrimitive(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.Category is TypeCategory.Primitive);
        }

        private static RuntimeValue IsLambda(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.IsLambda);
        }

        private static RuntimeValue IsStruct(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.Category is TypeCategory.Struct);
        }

        private static RuntimeValue IsEnum(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.Category is TypeCategory.Enum);
        }

        private static RuntimeValue IsCollection(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            return new RuntimeValue(metadata.Category is TypeCategory.BuiltIn);
        }

        private static RuntimeValue GetInstanceFields(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            ListObject list = new ListObject();

            if (metadata.InstanceFields.Count > 0)
            {
                list.Elements.AddRange(metadata.InstanceFields.Select(f => vm.ResolveStringObjectRuntimeValue(f.Name)));
            }
            return new RuntimeValue(list);
        }

        private static RuntimeValue GetStaticSolidFields(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            ListObject list = new ListObject();

            if (metadata.StaticFields.Count > 0)
            {
                list.Elements.AddRange(metadata.StaticFields.Select(f => vm.ResolveStringObjectRuntimeValue(f.Name)));
            }
            return new RuntimeValue(list);
        }

        private static RuntimeValue HasInstanceField(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            string fieldName = IntrinsicHelpers.GetStringArg(vm, "has_instance_field()");
            return new RuntimeValue(metadata.InstanceFieldNames.Contains(fieldName));
        }

        private static RuntimeValue HasStaticField(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            string fieldName = IntrinsicHelpers.GetStringArg(vm, "has_static_field()");
            return new RuntimeValue(metadata.StaticFieldNames.Contains(fieldName));
        }

        private static RuntimeValue GetConstructors(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            ListObject list = new ListObject();

            foreach (MethodMetadata ctor in metadata.Constructors)
            {
                list.Elements.Add(MethodMetadataWrapper.Create(ctor));
            }
            return new RuntimeValue(list);
        }

        private static RuntimeValue GetInstanceMethods(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            ListObject list = new ListObject();

            foreach (MethodMetadata method in metadata.InstanceMethods)
            {
                list.Elements.Add(MethodMetadataWrapper.Create(method));
            }
            return new RuntimeValue(list);
        }

        private static RuntimeValue GetAllMethods(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            ListObject list = new ListObject();

            foreach (MethodMetadata method in metadata.InstanceMethods)
                list.Elements.Add(MethodMetadataWrapper.Create(method));
            foreach (MethodMetadata ctor in metadata.Constructors)
                list.Elements.Add(MethodMetadataWrapper.Create(ctor));

            return new RuntimeValue(list);
        }

        private static RuntimeValue GetMethodsFromName(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;

            ListObject list = new ListObject();
            string name = IntrinsicHelpers.GetStringArg(vm, "find_method(name)");

            foreach (MethodMetadata method in metadata.InstanceMethods)
            {
                if (method.Name == name)
                {
                    list.Elements.Add(MethodMetadataWrapper.Create(method));
                }
            }

            return new RuntimeValue(list);
        }

        private static RuntimeValue GetMethodsFromNameArity(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;

            int arity = vm.PopStack().IntValue;
            string name = IntrinsicHelpers.GetStringArg(vm, "find_method(name, arity)");

            foreach (MethodMetadata method in metadata.InstanceMethods)
            {
                if (method.BaseName == name && method.Arity == arity)
                {
                    return MethodMetadataWrapper.Create(method);
                }
            }

            return RuntimeValue.Nil;
        }

        private static RuntimeValue GetParameters(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;

            ListObject list = new ListObject();

            foreach (string item in metadata.Parameters)
            {
                list.Elements.Add(vm.ResolveStringObjectRuntimeValue(item));
            }

            return new RuntimeValue(list);
        }

        private static RuntimeValue GetParametersRef(FluenceVirtualMachine vm, RuntimeValue self)
        {
            MethodMetadata metadata = (MethodMetadata)self.As<Wrapper>().Instance;

            ListObject list = new ListObject();

            for (int i = 0; i < metadata.Parameters.Count; i++)
            {
                if ((metadata.RefMask & (1 << i)) != 0)
                {
                    list.Elements.Add(vm.ResolveStringObjectRuntimeValue(metadata.Parameters[i]));
                }
            }

            return new RuntimeValue(list);
        }

        private static RuntimeValue ImplementsType(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            string traitName = IntrinsicHelpers.GetStringArg(vm, "implements(trait_name)");

            if (metadata.StructSymbol == null)
            {
                return new RuntimeValue(false);
            }

            return new RuntimeValue(metadata.StructSymbol.ImplementedTraits.Contains(traitName.GetHashCode()));
        }

        private static RuntimeValue GetFieldValue(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            string fieldName = IntrinsicHelpers.GetStringArg(vm, "get_field_value(instance, field_name)");
            RuntimeValue instanceVal = vm.PopStack();

            if (instanceVal.ObjectReference is InstanceObject instanceObj && instanceObj.Class == metadata.StructSymbol)
            {
                return instanceObj.GetField(fieldName, vm);
            }

            return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: Invalid instance or type mismatch in get_field_value.");
        }

        private static RuntimeValue SetFieldValue(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            RuntimeValue valueToSet = vm.PopStack();
            string fieldName = IntrinsicHelpers.GetStringArg(vm, "set_field_value(instance, field_name, value)");
            RuntimeValue instanceVal = vm.PopStack();

            if (instanceVal.ObjectReference is InstanceObject instanceObj && instanceObj.Class == metadata.StructSymbol)
            {
                if (metadata.StructSymbol != null && metadata.StructSymbol.StaticFields.ContainsKey(fieldName))
                {
                    return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: Cannot set solid field '{fieldName}' via reflection.");
                }

                instanceObj.SetField(fieldName, valueToSet);
                return RuntimeValue.Nil;
            }

            return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: Invalid instance or type mismatch in set_field_value.");
        }

        private static RuntimeValue GetStaticValue(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            string fieldName = IntrinsicHelpers.GetStringArg(vm, "get_static_value(field_name)");

            if (metadata.StructSymbol != null && metadata.StructSymbol.StaticFields.TryGetValue(fieldName, out RuntimeValue value))
            {
                return value;
            }

            return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: Static field '{fieldName}' not found.");
        }

        private static RuntimeValue SetStaticValue(FluenceVirtualMachine vm, RuntimeValue self)
        {
            TypeMetadata metadata = (TypeMetadata)self.As<Wrapper>().Instance;
            RuntimeValue valueToSet = vm.PopStack();
            string fieldName = IntrinsicHelpers.GetStringArg(vm, "set_static_value(field_name, value)");

            if (metadata.StructSymbol != null && metadata.StructSymbol.StaticFields.ContainsKey(fieldName))
            {
                metadata.StructSymbol.StaticFields[fieldName] = valueToSet;
                return RuntimeValue.Nil;
            }

            return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: Static field '{fieldName}' not found.");
        }
    }
}