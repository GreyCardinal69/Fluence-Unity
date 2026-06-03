using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Text;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    /// <summary>
    /// Represents a <see cref="HashSet{T}"/> where T is any runtime value.
    /// </summary>
    internal static class HashSetWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static HashSetWrapper()
        {
            _instanceMethods["add__1"] = Add;
            _instanceMethods["clear__0"] = Clear;
            _instanceMethods["contains__1"] = Contains;
            _instanceMethods["remove__1"] = Remove;
            _instanceMethods["to_string__0"] = ToString;
            _instanceMethods["count__0"] = Count;
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
                {
                new FunctionSymbol("HashSet__0", 0, (vm, argCount) =>
                {
                    HashSet<RuntimeValue> setInstance = new HashSet<RuntimeValue>();

                    Wrapper wrapper = new Wrapper(setInstance, _instanceMethods);

                    return new RuntimeValue(wrapper);

                }, scope, new List<string>()),

                new FunctionSymbol("HashSet__1", 1, (vm, argCount) =>
                {
                    RuntimeValue arg = vm.PopStack();
                    HashSet<RuntimeValue> setInstance = null!;

                    if (arg.ObjectReference is Wrapper obj)
                    {
                        if (obj.Instance is HashSet<RuntimeValue>)
                        {
                            setInstance = new HashSet<RuntimeValue>((HashSet<RuntimeValue>)arg.As<Wrapper>().Instance);
                        }
                    }
                    else
                    {
                        if (arg.Type != RuntimeValueType.Number || arg.NumberType != RuntimeNumberType.Int)
                        {
                            return vm.SignalRecoverableErrorAndReturnNil("HashSet constructor accepts only an integer value for its capacity in the constructor 'HashSet(capacity)'");
                        }
                        setInstance = new HashSet<RuntimeValue>(arg.IntValue);
                    }

                    Wrapper wrapper = new Wrapper(setInstance, _instanceMethods);

                    return new RuntimeValue(wrapper);

                }, scope, new List<string>() {"int_capacity/hash_Set" })
                };
        }

        private static RuntimeValue Add(FluenceVirtualMachine vm, RuntimeValue self)
        {
            HashSet<RuntimeValue> set = (HashSet<RuntimeValue>)self.As<Wrapper>().Instance;
            RuntimeValue arg = vm.PopStack();
            set.Add(arg);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue ToString(FluenceVirtualMachine vm, RuntimeValue self)
        {
            HashSet<RuntimeValue> set = (HashSet<RuntimeValue>)self.As<Wrapper>().Instance;

            StringBuilder sb = new StringBuilder("HashSet: [");

            int i = 0;
            foreach (RuntimeValue value in set)
            {
                sb.Append(value.ToString());
                sb.Append(i < set.Count - 1 ? ", " : "]");
                i++;
            }

            return vm.ResolveStringObjectRuntimeValue(sb.ToString());
        }

        private static RuntimeValue Remove(FluenceVirtualMachine vm, RuntimeValue self)
        {
            HashSet<RuntimeValue> set = (HashSet<RuntimeValue>)self.As<Wrapper>().Instance;
            RuntimeValue arg = vm.PopStack();
            set.Remove(arg);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Count(FluenceVirtualMachine vm, RuntimeValue self)
        {
            HashSet<RuntimeValue> set = (HashSet<RuntimeValue>)self.As<Wrapper>().Instance;
            return new RuntimeValue(set.Count);
        }

        private static RuntimeValue Clear(FluenceVirtualMachine vm, RuntimeValue self)
        {
            HashSet<RuntimeValue> set = (HashSet<RuntimeValue>)self.As<Wrapper>().Instance;
            set.Clear();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Contains(FluenceVirtualMachine vm, RuntimeValue self)
        {
            HashSet<RuntimeValue> set = (HashSet<RuntimeValue>)self.As<Wrapper>().Instance;
            RuntimeValue arg = vm.PopStack();
            return set.Contains(arg) ? RuntimeValue.True : RuntimeValue.False;
        }
    }
}