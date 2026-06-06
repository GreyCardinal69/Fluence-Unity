using Fluence.Unity.Exceptions;
using Fluence.Unity.RuntimeTypes;
using System.Linq;

namespace Fluence.Unity.Extensions
{
    /// <summary>
    /// Allows the safe creation of external intrinsic libraries for the use inside Fluence code.
    /// </summary>
    public sealed class LibraryBuilder
    {
        private readonly FluenceScope _scope;

        internal LibraryBuilder(FluenceScope scope)
        {
            _scope = scope;
        }

        /// <summary>
        /// Defines the delegate signature for user functions.
        /// </summary>
        public delegate RuntimeValue NativeFunction(INativeVmContext vm, int argCount);

        /// <summary>
        /// Registers a native function in the namespace.
        /// </summary>
        public void AddFunction(string name, int arity, NativeFunction body, params string[] argNames)
        {
            string mangledName = Mangler.Mangle(name, arity);

            if (_scope.ContainsLocal(mangledName.GetHashCode()))
            {
                throw new FluenceException($"Function '{name}' with arity {arity} is already defined in this scope.");
            }

            IntrinsicMethod internalBody = (vm, count) =>
            {
                return body(vm, count);
            };

            FunctionSymbol symbol = new FunctionSymbol(
                mangledName,
                arity,
                internalBody,
                _scope,
                argNames.ToList()
            );

            _scope.Declare(symbol.Name.GetHashCode(), symbol);
        }

        /// <summary>
        /// Registers a native function to a struct symbol in the namespace.
        /// </summary>
        public void AddFunctionToStruct(string structName, string name, int arity, NativeFunction body, params string[] argNames)
        {
            IntrinsicMethod internalBody = (vm, count) =>
            {
                return body(vm, count);
            };

            string mangledFuncName = Mangler.Mangle(name, arity);

            FunctionSymbol functionSymbol = new FunctionSymbol(
                mangledFuncName,
                arity,
                internalBody,
                _scope,
                argNames.ToList()
            );

            if (_scope.TryGetLocalSymbol(structName.GetHashCode(), out Symbol symb) && symb is StructSymbol structSymbol)
            {
                structSymbol.StaticIntrinsics.Add(mangledFuncName, functionSymbol);
                return;
            }

            throw new FluenceException($"Unable to declare a struct function symbol: \"{name}\" to a non-registered struct symbol with name: \"{structName}\".");
        }

        /// <summary>
        /// Registers a static constant value in the scope. All global scope variable constants are registered as read only.
        /// </summary>
        public void AddGlobalConstant(string name, object? value)
        {
            if (_scope.TryGetLocalSymbol(name.GetHashCode(), out Symbol symbol))
            {
                throw new FluenceException($"Can not declare a new global constant to the scope: \"{_scope.Name}\" with the name \"{name}\" as it is already declared.");
            }

            Value constantValue;

            if (value == null)
            {
                constantValue = NilValue.NilInstance;
            }
            else
            {
                constantValue = value switch
                {
                    int i => new NumberValue(i),
                    long l => new NumberValue(l),
                    float f => new NumberValue(f),
                    double d => new NumberValue(d),

                    string s => new StringValue(s),
                    char c => new CharValue(c),

                    bool b => new BooleanValue(b),

                    _ => throw new FluenceException($"Invalid constant type: '{value.GetType().Name}'. Global constants must be primitives (Number, String, Char, Bool, or Null).")
                };
            }

            _scope.Declare(name.GetHashCode(), new VariableSymbol(name, constantValue, true));
        }

        /// <summary>
        /// Registers a static constant value to a struct in the scope.
        /// </summary>
        public void AddConstantToStruct(string structName, string name, object value)
        {
            RuntimeValue rtValue = new RuntimeValue(value);

            if (_scope.TryGetLocalSymbol(structName.GetHashCode(), out Symbol symbol) && symbol is StructSymbol symb)
            {
                symb.StaticFields.Add(name, rtValue);
                return;
            }

            throw new FluenceException($"Unable to declare a constant value: \"{name}\" with value of \"{rtValue}\" to a non-registered struct symbol with name: \"{structName}\".");
        }

        /// <summary>
        /// Creates a nested struct in the scope.
        /// If the struct already exists, this does nothing.
        /// </summary>
        public void AddStruct(string name)
        {
            int hash = name.GetHashCode();

            if (_scope.TryGetLocalSymbol(hash, out Symbol existing))
            {
                if (existing is StructSymbol)
                {
                    return;
                }

                throw new FluenceException($"Cannot create struct '{name}' because a symbol with that name (Type: {existing.GetType().Name}) already exists in this scope.");
            }

            StructSymbol structSymbol = new StructSymbol(name, _scope);
            _scope.Declare(hash, structSymbol);
        }
    }
}