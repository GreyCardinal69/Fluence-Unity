using Fluence.Unity.VirtualMachine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// Represents the runtime instance of a list, which can contain any <see cref="RuntimeValue"/>.
    /// Implements <see cref="IFluenceObject"/> to provide intrinsic functions.
    /// </summary>
    internal sealed class ListObject : IFluenceObject, ICloneableFluenceObject
    {
        /// <summary>The elements of the list.</summary>
        internal List<RuntimeValue> Elements { get; } = new();

        internal ListObject(List<RuntimeValue> elements)
        {
            Elements = elements;
        }

        internal ListObject() { }

        public IFluenceObject CloneObject()
        {
            ListObject newList = new ListObject();
            newList.Elements.AddRange(this.Elements);
            return newList;
        }

        private static RuntimeValue Length(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return new RuntimeValue(self.As<ListObject>()?.Elements.Count ?? 0);
        }

        private static RuntimeValue Clear(FluenceVirtualMachine vm, RuntimeValue self)
        {
            self.As<ListObject>().Elements.Clear();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Reverse(FluenceVirtualMachine vm, RuntimeValue self)
        {
            self.As<ListObject>().Elements.Reverse();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Push(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            self.As<ListObject>().Elements.Add(element);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue IndexOf(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            return new RuntimeValue(self.As<ListObject>().Elements.IndexOf(element));
        }

        private static RuntimeValue LastIndexOf(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            return new RuntimeValue(self.As<ListObject>().Elements.LastIndexOf(element));
        }

        private static RuntimeValue Remove(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            self.As<ListObject>().Elements.Remove(element);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue ElementAt(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue index = vm.PopStack();
            ListObject selfList = self.As<ListObject>();

            if (index.Type != RuntimeValueType.Number || index.IntValue < 0 || index.IntValue > selfList.Elements.Count)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: remove index is out of bounds or invalid.");
            }

            return new RuntimeValue(selfList.Elements[index.IntValue]);
        }

        private static RuntimeValue RemoveAt(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue index = vm.PopStack();
            ListObject selfList = self.As<ListObject>();

            if (index.Type != RuntimeValueType.Number || index.IntValue < 0 || index.IntValue > selfList.Elements.Count)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: remove index is out of bounds or invalid.");
            }

            selfList.Elements.RemoveAt(index.IntValue);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Contains(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            return self.As<ListObject>().Elements.Contains(element) ? RuntimeValue.True : RuntimeValue.False;
        }

        private static RuntimeValue RemoveRange(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue count = vm.PopStack();
            RuntimeValue index = vm.PopStack();
            ListObject selfList = self.As<ListObject>();

            if (index.Type != RuntimeValueType.Number || index.IntValue < 0 || index.IntValue > selfList.Elements.Count)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: index is out of bounds or invalid.");
            }

            if (count.Type != RuntimeValueType.Number || count.IntValue < 0 || index.IntValue > selfList.Elements.Count)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: count to remove is out of bounds or invalid.");
            }

            selfList.Elements.RemoveRange(index.IntValue, count.IntValue);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue Insert(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            RuntimeValue index = vm.PopStack();
            ListObject selfList = self.As<ListObject>();

            if (index.Type != RuntimeValueType.Number || index.IntValue < 0 || index.IntValue > selfList.Elements.Count)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: index is out of bounds or invalid.");
            }

            selfList.Elements.Insert(index.IntValue, element);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue InsertRange(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue list = vm.PopStack();
            RuntimeValue index = vm.PopStack();
            ListObject selfList = self.As<ListObject>();

            if (index.Type != RuntimeValueType.Number || index.IntValue < 0 || index.IntValue > selfList.Elements.Count)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: index is out of bounds or invalid.");
            }

            selfList.Elements.InsertRange(index.IntValue, list.As<ListObject>().Elements);
            return RuntimeValue.Nil;
        }

        private static RuntimeValue PushRange(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue element = vm.PopStack();
            ListObject list = element.As<ListObject>();

            self.As<ListObject>().Elements.AddRange(list.Elements);
            return RuntimeValue.Nil;
        }

        /// <inheritdoc/>
        bool IFluenceObject.TryGetIntrinsicMethod(string name, out IntrinsicRuntimeMethod method)
        {
            method = name switch
            {
                "push__1" => Push,
                "push_range__1" => PushRange,
                "length__0" => Length,
                "clear__0" => Clear,
                "reverse__0" => Reverse,
                "contains__1" => Contains,
                "remove__1" => Remove,
                "remove_at__1" => RemoveAt,
                "element_at__1" => ElementAt,
                "remove_range__2" => RemoveRange,
                "insert__2" => Insert,
                "insert_range__2" => InsertRange,
                "index_of__1" => IndexOf,
                "last_index_of__1" => LastIndexOf,
                _ => null!
            };
            return method != null;
        }

        public override string ToString() => $"ListObject [{string.Join(", ", Elements)}]";
    }
}