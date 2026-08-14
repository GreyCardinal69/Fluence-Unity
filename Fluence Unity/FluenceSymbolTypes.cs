using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;

namespace Fluence.Unity
{
    /// <summary>
    /// Represents a named entity in the source code that is declared at a scope level.
    /// This is the abstract base class for functions, structs, and enums.
    /// It inherits from <see cref="Value"/> to be storable in the bytecode stream.
    /// </summary>
    internal abstract class Symbol : Value
    {
        /// <summary>
        /// The declared name of this symbol.
        /// Always non-null for any valid symbol.
        /// </summary>
        internal string Name { get; private set; }

        protected Symbol(string name) : base()
        {
            Name = name;
            Hash = Name.GetHashCode();
        }
    }

    /// <summary>
    /// Represents a declaration of an intrinsic struct symbol with a set of traits it implements.
    /// </summary>
    internal sealed class IntrinsicStructSymbol : Symbol
    {
        /// <summary>
        /// A set of pre-calculated hash codes representing the traits this struct implements.
        /// </summary>
        internal HashSet<int> ImplementedTraits { get; } = new HashSet<int>();

        internal IntrinsicStructSymbol(string name) : base(name) { }

        internal override string ToFluenceString() => "<internal: intrinsic__struct__symbol>";
        public override string ToString() => "IntrinsicStructSymbol";
    }

    /// <summary>
    /// Represents a variable of a scope.
    /// </summary>
    internal sealed class VariableSymbol : Symbol
    {
        /// <summary>The value of the variable.</summary>
        internal Value Value { get; private set; }

        /// <summary>
        /// Indicates that the variable is marked as 'solid' as in, a readonly variable.
        /// </summary>
        internal bool IsReadonly { get; private set; }

        internal VariableSymbol(string name, Value value, bool readOnly = false) : base(name)
        {
            Value = value;
            IsReadonly = readOnly;
        }

        internal override string ToFluenceString() => $"VariableSymbol: {Name}---{Value}";
        public override string ToString() => $"VariableSymbol: {Name}---{Value}";
    }

    /// <summary>
    /// Represents an enum declaration. It contains the enum's name and a collection of its members.
    /// </summary>
    internal sealed class EnumSymbol : Symbol
    {
        /// <summary>
        /// The dictionary mapping member names to their corresponding <see cref="EnumValue"/>s.
        /// </summary>
        internal Dictionary<string, EnumValue> Members { get; } = new Dictionary<string, EnumValue>();

        internal EnumSymbol(string name) : base(name) { }

        internal override string ToFluenceString() => "<internal: enum_symbol>";
        public override string ToString() => $"EnumSymbol: {Name}-{Members}";
    }

    /// <summary>
    /// Represents a trait encapsulating field and function signatures that classes inherit,
    /// along with default field values and static fields.
    /// </summary>
    internal sealed class TraitSymbol : Symbol
    {
        /// <summary>
        /// Defines the signature of a function within the trait, including its name, hash, and arity.
        /// </summary>
        internal struct FunctionSignature
        {
            /// <summary>The name of the function.</summary>
            internal string Name { get; set; }

            /// <summary>The hash code associated with the function signature.</summary>
            internal int Hash { get; set; }

            /// <summary>The arity of the function.</summary>
            internal int Arity { get; set; }

            /// <summary>Indicates whether the function signature is that of a constructor.</summary>
            internal bool IsAConstructor { get; set; }
        }

        /// <summary>Contains the trait's fields' names.</summary>
        internal Dictionary<int, string> FieldSignatures { get; private set; }

        /// <summary>Contains the trait's functions' signatures.</summary>
        internal Dictionary<int, FunctionSignature> FunctionSignatures { get; private set; }

        /// <summary>
        /// Gets a dictionary mapping field names to the sequence of tokens representing their default value expression.
        /// </summary>
        internal Dictionary<string, List<Token>> DefaultFieldValuesAsTokens { get; } = new Dictionary<string, List<Token>>();

        /// <summary>The static solid fields of the trait.</summary>
        internal Dictionary<string, RuntimeValue> StaticFields { get; } = new Dictionary<string, RuntimeValue>();

        internal TraitSymbol(string name) : base(name)
        {
            FieldSignatures = new Dictionary<int, string>();
            FunctionSignatures = new Dictionary<int, FunctionSignature>();
        }

        internal override string ToFluenceString() => "<internal: trait_symbol>";
        public override string ToString() => $"TraitSymbol<{Name}>";
    }

    /// <summary>
    /// Represents a struct declaration. It contains the struct's name, fields, methods, and constructor information.
    /// </summary>
    internal sealed class StructSymbol : Symbol
    {
        /// <summary>The scope the struct belongs to.</summary>
        internal FluenceScope Scope { get; private set; }

        /// <summary>The list of declared field names.</summary>
        internal List<string> Fields { get; } = new List<string>();

        /// <summary>The static solid fields of a struct.</summary>
        internal Dictionary<string, RuntimeValue> StaticFields { get; } = new Dictionary<string, RuntimeValue>();

        /// <summary>
        /// Stores natively implemented static intrinsic methods.
        /// </summary>
        public Dictionary<string, FunctionSymbol> StaticIntrinsics { get; } = new Dictionary<string, FunctionSymbol>();

        /// <summary>
        /// Gets a dictionary of methods defined within the struct, mapping method names to their <see cref="FunctionValue"/>s.
        /// </summary>
        internal Dictionary<string, FunctionValue> Functions { get; } = new Dictionary<string, FunctionValue>();

        /// <summary>
        /// Gets a dictionary mapping field names to the sequence of tokens representing their default value expression.
        /// </summary>
        internal Dictionary<string, List<Token>> DefaultFieldValuesAsTokens { get; } = new Dictionary<string, List<Token>>();

        /// <summary>
        /// Gets or sets the constructor function (`init`) for this struct.
        /// </summary>
        internal Dictionary<string, FunctionValue> Constructors { get; } = new Dictionary<string, FunctionValue>();

        /// <summary>
        /// A set of pre-calculated hash codes representing the traits this struct implements.
        /// </summary>
        internal HashSet<int> ImplementedTraits { get; } = new HashSet<int>();

        internal StructSymbol(string name, FluenceScope scope) : base(name)
        {
            Scope = scope;
        }

        internal override string ToFluenceString() => "<internal: struct_symbol>";
        public override string ToString() => $"StructSymbol<{Name}>";
    }

    /// <summary>
    /// Defines the delegate signature for a native C# method that can be called from Fluence script.
    /// </summary>
    internal delegate RuntimeValue IntrinsicMethod(FluenceVirtualMachine vm, int argCount);

    /// <summary>
    /// Represents a function or method declaration. It can be a user-defined Fluence function
    /// with a bytecode address or a native C# intrinsic function with a delegate.
    /// </summary>
    internal sealed class FunctionSymbol : Symbol
    {
        /// <summary>
        /// The number of parameters the function is declared to accept (excluding the implicit 'self' for methods).
        /// </summary>
        internal int Arity { get; private set; }

        /// <summary>
        /// The starting address of the function's bytecode. For intrinsics, this is -1.
        /// </summary>
        internal int StartAddress { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this function is a native C# intrinsic.
        /// </summary>
        internal bool IsIntrinsic { get; private set; }

        /// <summary>
        /// The line in the source file where the function is defined, pointing to the line where the function name is declared.
        /// </summary>
        internal int StartAddressInSource { get; private set; }

        /// <summary>
        /// If this is an intrinsic function, gets the C# delegate that implements its logic.
        /// </summary>
        internal IntrinsicMethod IntrinsicBody { get; private set; }

        /// <summary>
        /// The arguments of the function by name.
        /// </summary>
        internal List<string> Arguments { get; private set; }

        /// <summary>
        /// A bitmask identifying which arguments are passed by reference.
        /// </summary>
        internal int RefMask { get; private set; }

        /// <summary>
        /// Keeps track of which namespace the function is defined in.
        /// </summary>
        internal FluenceScope DefiningScope { get; private set; }

        /// <summary>
        /// The total amount of register slots this function requires to execute its bytecode.
        /// </summary>
        internal int TotalRegisterSlots { get; set; }

        /// <summary>
        /// Indicates whether the function is an instance or a static method of some struct type.
        /// </summary>
        internal bool BelongsToAStruct { get; set; }

        /// <summary>
        /// Sets the bytecode start address for this function. Called by the parser during the second pass.
        /// </summary>
        internal void SetStartAddress(int addr) => StartAddress = addr;

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionSymbol"/> class for a native C# intrinsic.
        /// </summary>
        internal FunctionSymbol(string name, int arity, IntrinsicMethod body, FluenceScope definingScope, List<string> arguments) : base(name)
        {
            Arity = arity;
            StartAddress = -1; // Special address for intrinsics.
            IsIntrinsic = true;
            IntrinsicBody = body;
            Arguments = arguments;

            DefiningScope = definingScope;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionSymbol"/> class for a user-defined Fluence function.
        /// </summary>
        internal FunctionSymbol(string name, int arity, int startAddress, int lineInSource, FluenceScope definingScope, List<string> arguments, int refMask) : base(name)
        {
            StartAddressInSource = lineInSource;
            Arity = arity;
            StartAddress = startAddress;
            IsIntrinsic = false;
            Arguments = arguments;

            RefMask = refMask;
            DefiningScope = definingScope;
        }

        internal override string ToFluenceString() => "<internal: function_symbol>";

        public override string ToString()
        {
            string args = (Arguments == null || Arguments.Count == 0) ? "None" : string.Join(",", Arguments);
            return $"FunctionSymbol: {Name}, Intrinsic:{IsIntrinsic}, {FluenceDebug.FormatByteCodeAddress(StartAddress)}, #{Arity} args: {args}, LocationInSource: {StartAddressInSource}.";
        }
    }
}