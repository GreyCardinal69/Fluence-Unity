using Fluence.Unity.VirtualMachine;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// Represents the runtime instance of a dictionary, which can contain any <see cref="RuntimeValue"/> as keys and values.
    /// Implements <see cref="DictionaryObject"/> to provide intrinsic functions.
    /// </summary>
    internal sealed class DictionaryObject : IFluenceObject
    {
        internal Dictionary<RuntimeValue, RuntimeValue> Dictionary { get; } = new();

        internal DictionaryObject(Dictionary<RuntimeValue, RuntimeValue> elements)
        {
            Dictionary = elements;
        }

        internal DictionaryObject() { }

        private static RuntimeValue Count(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return new RuntimeValue(self.As<DictionaryObject>()?.Dictionary.Count ?? 0);
        }

        public RuntimeValue this[RuntimeValue val]
        {
            get => Dictionary[val];
            set => Dictionary[val] = value;
        }

        private static RuntimeValue Add(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue value = vm.PopStack();
            RuntimeValue key = vm.PopStack();
            self.As<DictionaryObject>()[key] = value;
            return self;
        }

        private static RuntimeValue Get(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue key = vm.PopStack();
            if (self.As<DictionaryObject>().Dictionary.TryGetValue(key, out RuntimeValue value))
            {
                return value;
            }
            return RuntimeValue.Nil;
        }

        private static RuntimeValue GetWithDefault(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue defaultValue = vm.PopStack();
            RuntimeValue key = vm.PopStack();
            if (self.As<DictionaryObject>().Dictionary.TryGetValue(key, out RuntimeValue value))
            {
                return value;
            }
            return defaultValue;
        }

        private static RuntimeValue Remove(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue key = vm.PopStack();
            self.As<DictionaryObject>().Dictionary.Remove(key);
            return self;
        }

        private static RuntimeValue ContainsKey(FluenceVirtualMachine vm, RuntimeValue self)
        {
            RuntimeValue key = vm.PopStack();
            return self.As<DictionaryObject>().Dictionary.ContainsKey(key) ? RuntimeValue.True : RuntimeValue.False;
        }

        private static RuntimeValue Keys(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Dictionary<RuntimeValue, RuntimeValue>.KeyCollection keys = self.As<DictionaryObject>().Dictionary.Keys;
            ListObject list = new ListObject();

            foreach (RuntimeValue item in keys)
            {
                list.Elements.Add(item);
            }

            return new RuntimeValue(list);
        }

        private static RuntimeValue Values(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Dictionary<RuntimeValue, RuntimeValue>.ValueCollection values = self.As<DictionaryObject>().Dictionary.Values;
            ListObject list = new ListObject();

            foreach (RuntimeValue item in values)
            {
                list.Elements.Add(item);
            }

            return new RuntimeValue(list);
        }

        private static RuntimeValue IsEmpty(FluenceVirtualMachine vm, RuntimeValue self)
        {
            return self.As<DictionaryObject>().Dictionary.Count == 0 ? RuntimeValue.True : RuntimeValue.False;
        }

        private static RuntimeValue Clear(FluenceVirtualMachine vm, RuntimeValue self)
        {
            self.As<DictionaryObject>().Dictionary.Clear();
            return RuntimeValue.Nil;
        }

        private static RuntimeValue ToString(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Dictionary<RuntimeValue, RuntimeValue> dict = self.As<DictionaryObject>().Dictionary;
            StringBuilder sb = new StringBuilder("Map {");
            sb.Append(string.Join(", ", dict.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
            sb.Append('}');
            return vm.ResolveStringObjectRuntimeValue(sb.ToString());
        }

        /// <inheritdoc/>
        bool IFluenceObject.TryGetIntrinsicMethod(string name, out IntrinsicRuntimeMethod method)
        {
            method = name switch
            {
                "add__2" => Add,
                "get__1" => Get,
                "count__0" => Count,
                "clear__0" => Clear,
                "get__2" => GetWithDefault,
                "contains_key__1" => ContainsKey,
                "remove__1" => Remove,
                "keys__0" => Keys,
                "values__0" => Values,
                "is_empty__0" => IsEmpty,
                "to_string__0" => ToString,
                _ => null!
            };

            return method != null;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder("Map {");
            sb.Append(string.Join(", ", Dictionary.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
            sb.Append('}');
            return sb.ToString();
        }
    }
}