using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Text;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    /// <summary>
    /// Represents a <see cref="Stack{T}"/> where T is any runtime value.
    /// </summary>
    internal static class StackWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static StackWrapper()
        {
            _instanceMethods["count__0"] = Count;
            _instanceMethods["clear__0"] = Clear;
            _instanceMethods["pop__0"] = Pop;
            _instanceMethods["peek__0"] = Peek;
            _instanceMethods["to_string__0"] = ToString;
            _instanceMethods["push__1"] = Push;
            _instanceMethods["contains__1"] = Contains;
            _instanceMethods["empty__0"] = IsEmpty;
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
{
                new FunctionSymbol("Stack__0", 0, (vm, argCount) =>
                {
                    Stack<RuntimeValue> stackInstance = new Stack<RuntimeValue>();

                    Wrapper wrapper = new Wrapper(stackInstance, _instanceMethods);

                    return new RuntimeValue(wrapper);

                }, scope, new System.Collections.Generic.List<string>()),

                new FunctionSymbol("Stack__1", 1, (vm, argCount) =>
                {
                    RuntimeValue arg = vm.PopStack();
                    Stack<RuntimeValue> stackInstance;

                    if (arg.Type != RuntimeValueType.Number || arg.NumberType != RuntimeNumberType.Int)
                    {
                        return vm.SignalRecoverableErrorAndReturnNil("Stack constructor accepts only an integer value for its capacity in the constructor 'Stack(capacity)'");
                    }

                    stackInstance = new Stack<RuntimeValue>(arg.IntValue);
                    Wrapper wrapper = new Wrapper(stackInstance, _instanceMethods);

                    return new RuntimeValue(wrapper);

                }, scope, new List<string>() { "int_capacity" }),
            };
        }

        private static RuntimeValue IsEmpty(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            return stack.Count == 0 ? RuntimeValue.True : RuntimeValue.False;
        }

        private static RuntimeValue Count(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            return new RuntimeValue(stack.Count);
        }

        private static RuntimeValue ToString(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> set = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;

            StringBuilder sb = new StringBuilder("Stack: [");

            int i = 0;
            foreach (RuntimeValue value in set)
            {
                sb.Append(value.ToString());
                sb.Append(i < set.Count - 1 ? ", " : "]");
                i++;
            }

            return vm.ResolveStringObjectRuntimeValue(sb.ToString());
        }

        private static RuntimeValue Clear(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            stack.Clear();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Pop(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            return new RuntimeValue(stack.Pop());
        }

        private static RuntimeValue Push(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            stack.Push(vm.PopStack());
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Peek(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            return new RuntimeValue(stack.Peek());
        }

        private static RuntimeValue Contains(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Stack<RuntimeValue> stack = (Stack<RuntimeValue>)self.As<Wrapper>().Instance;
            RuntimeValue arg = vm.PopStack();
            return stack.Contains(arg) ? RuntimeValue.True : RuntimeValue.False;
        }
    }
}