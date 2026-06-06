using Fluence.Unity.Exceptions;
using Fluence.Unity.RuntimeTypes;
using System;
using System.Collections.Generic;

namespace Fluence.Unity
{
    /// <summary>
    /// A public accessor for interacting with Fluence struct instances.
    /// </summary>
    public sealed class FluenceStructAccessor : IFluenceStructInstance
    {
        private readonly InstanceObject _instance;
        private readonly FluenceInterpreter _interpreter;

        public string StructName => _instance.Class.Name;

        internal FluenceStructAccessor(InstanceObject instance, FluenceInterpreter interpreter)
        {
            _instance = instance;
            _interpreter = interpreter;
        }

        public object? GetField(string fieldName)
        {
            try
            {
                RuntimeValue rv = _instance.GetField(fieldName, _interpreter.VM);
                return FluenceInterpreter.ConvertToObject(rv, _interpreter);
            }
            catch (FluenceException)
            {
                return null;
            }
        }

        public void SetField(string fieldName, object? value)
        {
            if (_instance.Class.StaticFields.ContainsKey(fieldName))
            {
                throw new InvalidOperationException($"Cannot modify solid (static) field '{fieldName}'.");
            }

            RuntimeValue rv = FluenceInterpreter.ConvertToRuntimeValue(value, _interpreter.VM);
            _instance.SetField(fieldName, rv);
        }

        public IEnumerable<string> GetFieldNames() => _instance.GetActiveFieldNames();
    }
}