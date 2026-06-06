using Fluence.Unity.VirtualMachine;
using System;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// Represents a heap-allocated string object in the Fluence VM.
    /// </summary>
    internal sealed class StringObject : IFluenceObject
    {
        internal string Value { get; private set; }

        internal StringObject(string value) => Value = value;

        public StringObject() { }

        internal void Initialize(string str) => Value = str;

        private static RuntimeValue Length(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            return new RuntimeValue(selfStringObj.Value?.Length ?? 0);
        }

        private static RuntimeValue ToUpper(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            string upper = selfStringObj.Value?.ToUpperInvariant() ?? string.Empty;
            return vm.ResolveStringObjectRuntimeValue(upper);
        }

        private static RuntimeValue ToLower(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            string lower = selfStringObj.Value?.ToLowerInvariant() ?? string.Empty;
            return vm.ResolveStringObjectRuntimeValue(lower);
        }

        private static RuntimeValue Trim(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            string trimmed = selfStringObj.Value?.Trim() ?? string.Empty;
            return vm.ResolveStringObjectRuntimeValue(trimmed);
        }

        private static RuntimeValue TrimStart(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            string trimmed = selfStringObj.Value?.TrimStart() ?? string.Empty;
            return vm.ResolveStringObjectRuntimeValue(trimmed);
        }

        private static RuntimeValue TrimEnd(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            string trimmed = selfStringObj.Value?.TrimEnd() ?? string.Empty;
            return vm.ResolveStringObjectRuntimeValue(trimmed);
        }

        private static RuntimeValue IndexOf(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'index_of' on a nil or non-string value.");
            }

            RuntimeValue charToFind = vm.PopStack();
            if (charToFind.As<CharObject>() is not { } charObj)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: string.index_of() expects a character argument.");
            }

            int index = selfStringObj.Value.IndexOf(charObj.Value);
            return new RuntimeValue(index);
        }

        private static RuntimeValue LastIndexOf(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'last_index_of' on a nil or non-string value.");
            }

            RuntimeValue charToFind = vm.PopStack();
            if (charToFind.As<CharObject>() is not { } charObj)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: string.last_index_of() expects a character argument.");
            }

            int index = selfStringObj.Value.LastIndexOf(charObj.Value);
            return new RuntimeValue(index);
        }

        private static RuntimeValue Contains(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'contains' on a nil or non-string value.");
            }

            RuntimeValue arg = vm.PopStack();
            StringObject argStringObj = arg.As<StringObject>();
            if (argStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: string.contains() expects a non-nil string argument, got {FluenceVirtualMachine.GetDetailedTypeName(arg)}.");
            }

            bool contains = selfStringObj.Value.Contains(argStringObj.Value, StringComparison.Ordinal);
            return new RuntimeValue(contains);
        }

        private static RuntimeValue EndsWith(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'ends_with' on a nil or non-string value.");
            }

            RuntimeValue arg = vm.PopStack();
            StringObject argStringObj = arg.As<StringObject>();
            if (argStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: string.ends_with() expects a non-nil string argument, got {FluenceVirtualMachine.GetDetailedTypeName(arg)}.");
            }

            bool endsWith = selfStringObj.Value.EndsWith(argStringObj.Value, StringComparison.Ordinal);
            return new RuntimeValue(endsWith);
        }

        private static RuntimeValue StartsWith(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'starts_with' on a nil or non-string value.");
            }

            RuntimeValue arg = vm.PopStack();
            StringObject argStringObj = arg.As<StringObject>();
            if (argStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: string.starts_with() expects a non-nil string argument, got {FluenceVirtualMachine.GetDetailedTypeName(arg)}.");
            }

            bool startsWith = selfStringObj.Value.StartsWith(argStringObj.Value, StringComparison.Ordinal);
            return new RuntimeValue(startsWith);
        }

        private static RuntimeValue Insert(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'insert' on a nil or non-string value.");
            }

            RuntimeValue arg = vm.PopStack();
            RuntimeValue index = vm.PopStack();

            StringObject argStringObj = arg.As<StringObject>();
            if (argStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: string.insert() expects a non-nil string argument, got {FluenceVirtualMachine.GetDetailedTypeName(arg)}.");
            }

            if (index.Type != RuntimeValueType.Number || index.IntValue < 0 || index.IntValue > selfStringObj.Value.Length)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Insert index is out of bounds or invalid.");
            }

            string newValue = selfStringObj.Value.Insert(index.IntValue, argStringObj.Value);
            return vm.ResolveStringObjectRuntimeValue(newValue);
        }

        private static RuntimeValue PadLeft(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'pad_left' on a nil or non-string value.");
            }

            RuntimeValue count = vm.PopStack();
            RuntimeValue arg = vm.PopStack();

            if (count.Type != RuntimeValueType.Number || count.IntValue < 0)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Pad count must be a non-negative integer.");
            }

            CharObject? argCharObj = arg.As<CharObject>() ?? vm.SignalError<CharObject>($"Runtime Error: string.pad_left() expects a character argument, got {FluenceVirtualMachine.GetDetailedTypeName(arg)}.");
            string newValue = selfStringObj.Value.PadLeft(count.IntValue, argCharObj.Value);
            return vm.ResolveStringObjectRuntimeValue(newValue);
        }

        private static RuntimeValue PadRight(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'pad_right' on a nil or non-string value.");
            }

            RuntimeValue count = vm.PopStack();
            RuntimeValue arg = vm.PopStack();

            if (count.Type != RuntimeValueType.Number || count.IntValue < 0)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Pad count must be a non-negative integer.");
            }

            CharObject? argCharObj = arg.As<CharObject>() ?? vm.SignalError<CharObject>($"Runtime Error: string.pad_right() expects a character argument, got {FluenceVirtualMachine.GetDetailedTypeName(arg)}.");
            string newValue = selfStringObj.Value.PadRight(count.IntValue, argCharObj.Value);
            return vm.ResolveStringObjectRuntimeValue(newValue);
        }

        private static RuntimeValue Replace(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'replace' on a nil or non-string value.");
            }

            RuntimeValue with = vm.PopStack();
            RuntimeValue replace = vm.PopStack();

            StringObject replaceStringObj = replace.As<StringObject>();
            if (replaceStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: string.replace() expects a non-nil string argument for string to replace, got {FluenceVirtualMachine.GetDetailedTypeName(replace)}.");
            }

            StringObject withStringObj = with.As<StringObject>();
            if (withStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil($"Runtime Error: string.replace() expects a non-nil string for string to replace with, got {FluenceVirtualMachine.GetDetailedTypeName(with)}.");
            }

            string newValue = selfStringObj.Value.Replace(replaceStringObj.Value, withStringObj.Value, StringComparison.Ordinal);
            return vm.ResolveStringObjectRuntimeValue(newValue);
        }

        private static RuntimeValue SubString(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'sub' on a nil or non-string value.");
            }

            RuntimeValue from = vm.PopStack();

            if (from.Type != RuntimeValueType.Number || from.IntValue < 0 || from.IntValue > selfStringObj.Value.Length)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Substring start index is out of bounds or invalid.");
            }

            string newValue = selfStringObj.Value[from.IntValue..];
            return vm.ResolveStringObjectRuntimeValue(newValue);
        }

        private static RuntimeValue SubStringWithLength(FluenceVirtualMachine vm, RuntimeValue self)
        {
            StringObject selfStringObj = self.As<StringObject>();
            if (selfStringObj.Value is null)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Cannot call method 'sub' on a nil or non-string value.");
            }

            RuntimeValue length = vm.PopStack();
            RuntimeValue from = vm.PopStack();

            if (from.Type != RuntimeValueType.Number || length.Type != RuntimeValueType.Number ||
                from.IntValue < 0 || length.IntValue < 0 || from.IntValue + length.IntValue > selfStringObj.Value.Length)
            {
                return vm.SignalRecoverableErrorAndReturnNil("Runtime Error: Substring start index or length is out of bounds or invalid.");
            }

            string newValue = selfStringObj.Value.Substring(from.IntValue, length.IntValue);
            return vm.ResolveStringObjectRuntimeValue(newValue);
        }

        /// <inheritdoc/>
        bool IFluenceObject.TryGetIntrinsicMethod(string name, out IntrinsicRuntimeMethod method)
        {
            method = name switch
            {
                "length__0" => Length,
                "upper__0" => ToUpper,
                "lower__0" => ToLower,
                "trim__0" => Trim,
                "trim_start__0" => TrimStart,
                "trim_end__0" => TrimEnd,
                "index_of__1" => IndexOf,
                "last_index_of__1" => LastIndexOf,
                "contains__1" => Contains,
                "ends_with__1" => EndsWith,
                "starts_with__1" => StartsWith,
                "insert__2" => Insert,
                "pad_left__2" => PadLeft,
                "pad_right__2" => PadRight,
                "replace__2" => Replace,
                "sub__1" => SubString,
                "sub__2" => SubStringWithLength,
                _ => null!
            };
            return method != null;
        }

        public override string ToString() => Value;
    }
}