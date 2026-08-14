using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Text;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.Global
{
    /// <summary>
    /// Represents a <see cref="StringBuilder"/>.
    /// </summary>
    internal static class StringBuilderWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new();

        static StringBuilderWrapper()
        {
            _instanceMethods["append__1"] = Append;
            _instanceMethods["append__2"] = AppendCharInt;
            _instanceMethods["append_line__0"] = AppendLine;
            _instanceMethods["append_line__1"] = AppendLineContent;
            _instanceMethods["append_join__2"] = AppendJoin;
            _instanceMethods["insert__2"] = Insert;
            _instanceMethods["to_string__0"] = ToString;
            _instanceMethods["clear__0"] = Clear;
            _instanceMethods["length__0"] = Length;
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
            {
                new FunctionSymbol("StringBuilder__0", 0, (vm, argCount) =>
                {
                    StringBuilder stringBuilderInstance = new StringBuilder();

                    Wrapper wrapper = new Wrapper(stringBuilderInstance, _instanceMethods);

                    return new RuntimeValue(wrapper);

                }, scope, new List<string>()),

                new FunctionSymbol("StringBuilder__1", 1, (vm, argCount) =>
                {
                    RuntimeValue arg = vm.PopStack();
                    StringBuilder stringBuilderInstance;

                    if (arg.Type != RuntimeValueType.Number || arg.NumberType != RuntimeNumberType.Int)
                    {
                        return vm.SignalRecoverableErrorAndReturnNil("StringBuilder constructor accepts only an integer value for its capacity in the constructor 'StringBuilder(capacity)'");
                    }

                    if (arg.ObjectReference is StringObject str)
                    {
                        string valueToAppend = arg.ToString();
                        stringBuilderInstance = new StringBuilder(valueToAppend);
                    }
                    else
                    {
                        stringBuilderInstance = new StringBuilder(arg.IntValue);
                    }

                    Wrapper wrapper = new Wrapper(stringBuilderInstance, _instanceMethods);

                    return new RuntimeValue(wrapper);

                }, scope, new List<string>() {"int_capacity" }),
            };
        }

        private static RuntimeValue AppendCharInt(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue count = vm.PopStack();
            RuntimeValue chr = vm.PopStack();

            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;

            sb.Append(chr.As<CharObject>().Value, count.IntValue);
            return self;
        }

        private static RuntimeValue Append(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue arg = vm.PopStack();
            string valueToAppend = arg.ToString();

            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;
            sb.Append(valueToAppend);
            return self;
        }

        private static RuntimeValue AppendJoin(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue list = vm.PopStack();
            RuntimeValue arg = vm.PopStack();
            string separator = arg.ToString();

            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;

            sb.AppendJoin(separator, list.As<ListObject>().Elements);
            return self;
        }

        private static RuntimeValue Insert(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue arg = vm.PopStack();
            string valueToAppend = arg.ToString();
            RuntimeValue count = vm.PopStack();

            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;

            sb.Insert(count.IntValue, valueToAppend);
            return self;
        }

        private static RuntimeValue AppendLine(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;

            sb.Append(Environment.NewLine);
            return self;
        }

        private static RuntimeValue AppendLineContent(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue arg = vm.PopStack();
            string valueToAppend = arg.ToString();

            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;

            sb.Append(valueToAppend).Append(Environment.NewLine);
            return self;
        }

        private static RuntimeValue Length(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;
            return new RuntimeValue(sb.Length);
        }

        private static RuntimeValue ToString(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;
            return vm.ResolveStringObjectRuntimeValue(sb.ToString());
        }

        private static RuntimeValue Clear(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringBuilder sb = (StringBuilder)self.As<Wrapper>().Instance;
            sb.Clear();
            return self;
        }
    }
}