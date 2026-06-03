using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Fluence.Unity
{
    /// <summary>
    /// The abstract base type for all values that can be represented in bytecode.
    /// These objects are used by the parser to build an abstract representation of the code.
    /// </summary>
    public abstract class Value
    {
        internal virtual int Hash { get; set; }

        internal Value()
        {
            Hash = GetHashCode();
        }

        /// <summary>
        /// Provides a user-facing string representation of the value, as it would appear
        /// when printed within the Fluence language itself.
        /// </summary>
        internal abstract string ToFluenceString();

        /// <summary>
        /// Returns a compact representation optimized for bytecode debugging display.
        /// Designed to prevent column overflow in instruction listings.
        /// </summary>
        internal virtual string ToByteCodeString() => ToString();
    }

    /// <summary>Represents a single character literal.</summary>
    public sealed class CharValue : Value
    {
        public char Value { get; private set; }

        public CharValue(char value) : base()
        {
            Value = value;
        }

        internal override string ToFluenceString() => Value.ToString();
        public override string ToString() => $"CharValue: {Value}";
    }

    /// <summary>Represents a string literal.</summary>
    public sealed class StringValue : Value
    {
        private const int MaxDisplayLength = 15;
        public string Value { get; private set; }

        public StringValue(string value) : base()
        {
            Value = value;
        }

        internal override string ToFluenceString() => $"\"{Value}\"";

        public override string ToString() => string.IsNullOrEmpty(Value)
            ? "StringValue: \"\""
            : $"StringValue: \"{(Value.Length > MaxDisplayLength ? Value[..MaxDisplayLength] + "..." : Value)}\"";
    }

    /// <summary>Represents a boolean literal.</summary>
    public sealed class BooleanValue : Value
    {
        internal bool Value { get; private set; }

        internal static readonly BooleanValue True = new BooleanValue(true);
        internal static readonly BooleanValue False = new BooleanValue(false);

        internal BooleanValue(bool value) : base()
        {
            Value = value;
        }

        internal override string ToFluenceString() => Value ? "true" : "false";
        public override string ToString() => $"BooleanValue: {Value}";
    }

    /// <summary>Represents the nil (null) value.</summary>
    public sealed class NilValue : Value
    {
        internal static readonly NilValue NilInstance = new NilValue();

        internal override string ToFluenceString() => "nil";
        public override string ToString() => "NilValue";
    }

    /// <summary>
    /// Represents a range expression with start and end bounds.
    /// </summary>
    internal sealed class RangeValue : Value
    {
        internal Value Start { get; private set; }
        internal Value End { get; private set; }

        internal RangeValue(Value start, Value end) : base()
        {
            Start = start;
            End = end;
        }

        internal override string ToFluenceString() =>
                    $"<internal: range_{Start.ToFluenceString()}..{End.ToFluenceString()}>";

        internal override string ToByteCodeString() =>
                    $"Range[{Start.ToByteCodeString()}..{End.ToByteCodeString()}]";

        public override string ToString() =>
                    $"RangeValue: {Start.ToFluenceString()}..{End.ToFluenceString()}";
    }

    /// <summary>
    /// Holds a non boxed integer value for the use in the GoTo family of instructions.
    /// </summary>
    internal sealed class GoToValue : Value
    {
        internal int Address { get; set; }

        internal GoToValue(int address) : base()
        {
            Address = address;
        }

        internal override string ToFluenceString() =>
                    $"<internal: goto_{Address}";

        internal override string ToByteCodeString() =>
                    $"GoTo {Address}";

        public override string ToString() =>
                    $"GoToValue: {Address}";
    }

    /// <summary>Represents a numerical literal, which can be an Integer, Float, Long, or Double.</summary>
    public sealed class NumberValue : Value
    {
        internal enum NumberType
        {
            Integer,
            Float,
            Double,
            Long,
        }

        internal static readonly NumberValue One = new NumberValue(1);
        internal static readonly NumberValue Zero = new NumberValue(0);

        internal static readonly Dictionary<int, NumberValue> ParsedIntegerNumbers = new Dictionary<int, NumberValue>();

        internal object Value { get; private set; }
        internal NumberType Type { get; private set; }

        internal NumberValue(object literal) : base()
        {
            Value = literal;
            AssignNumberType(literal);
        }

        internal NumberValue(object literal, NumberType type) : base()
        {
            Value = literal;
            Type = type;
        }

        private void AssignNumberType(object literal)
        {
            Type = literal switch
            {
                int => NumberType.Integer,
                float => NumberType.Float,
                double => NumberType.Double,
                long => NumberType.Long,
                _ => NumberType.Integer,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NumberValue FromInt(int value)
        {
            switch (value)
            {
                case 0: return Zero;
                case 1: return One;
                default:
                    // Removed CollectionsMarshal and Unsafe to prevent Unity compilation and AOT issues
                    if (ParsedIntegerNumbers.TryGetValue(value, out NumberValue parsed))
                    {
                        return parsed;
                    }

                    NumberValue newNum = new NumberValue(value, NumberType.Integer);
                    ParsedIntegerNumbers.Add(value, newNum);
                    return newNum;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NumberValue FromToken(Token token)
        {
            string lexeme = token.Text;

            if (int.TryParse(lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            {
                return FromInt(intVal);
            }
            if (long.TryParse(lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            {
                return new NumberValue(longVal, NumberType.Long);
            }
            if (double.TryParse(lexeme, NumberStyles.Any, CultureInfo.InvariantCulture, out double fallbackVal))
            {
                return new NumberValue(fallbackVal, NumberType.Double);
            }
            if ((lexeme.Contains(".") || lexeme.IndexOf("e", StringComparison.OrdinalIgnoreCase) >= 0) && double.TryParse(lexeme, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleVal))
            {
                return new NumberValue(doubleVal, NumberType.Double);
            }
            if (lexeme.EndsWith("f", StringComparison.OrdinalIgnoreCase) && float.TryParse(lexeme[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out float floatVal))
            {
                return new NumberValue(floatVal, NumberType.Float);
            }
            throw new FormatException($"Invalid number format: '{lexeme}'");
        }

        private string GetTypeShorthand() => Type switch
        {
            NumberType.Double => "Double",
            NumberType.Integer => "Int",
            NumberType.Long => "Long",
            NumberType.Float => "Float",
            _ => throw new NotImplementedException()
        };

        internal override string ToFluenceString() => Value.ToString();
        internal override string ToByteCodeString() => $"{GetTypeShorthand()}_{Value}";
        public override string ToString() => $"NumberValue ({Type}): {Value}";
    }

    /// <summary>A special value indicating a completed statement with no return value.</summary>
    internal sealed class StatementCompleteValue : Value
    {
        internal static readonly StatementCompleteValue StatementCompleted = new StatementCompleteValue();

        internal StatementCompleteValue() : base() { }

        internal override string ToFluenceString() => "<internal: statement_complete>";
        public override string ToString() => "StatementCompletedValue";
    }

    /// <summary>
    /// Represents a variable passed by reference.
    /// </summary>
    internal sealed class ReferenceValue : Value
    {
        internal VariableValue Reference { get; private set; }

        internal ReferenceValue(VariableValue reference) : base()
        {
            Reference = reference;
        }

        internal override string ToFluenceString() => $"<internal: reference_value__{Reference}";
        internal override string ToByteCodeString() => $"Ref__{Reference.ToByteCodeString()}";
        public override string ToString() => $"ReferenceValue: {Reference}";
    }

    /// <summary>
    /// A descriptor representing an access to a struct's static function, or a static and solid field.
    /// </summary>
    internal sealed class StaticStructAccess : Value
    {
        internal StructSymbol Struct { get; private set; }
        internal string Name { get; private set; }

        internal StaticStructAccess(StructSymbol structType, string name) : base()
        {
            Struct = structType;
            Name = name;
        }

        internal override string ToFluenceString() => $"<internal: static_struct__<{Struct}__{Name}>";
        public override string ToString() => $"StaticStructAccessValue: <{Struct}__{Name}>";
    }

    /// <summary>
    /// A descriptor representing an element access operation.
    /// The parser resolves this into GetElement or SetElement bytecode.
    /// </summary>
    internal sealed class ElementAccessValue : Value
    {
        internal Value Target { get; private set; }
        internal Value Index { get; private set; }

        internal ElementAccessValue(Value target, Value index) : base()
        {
            Target = target;
            Index = index;
        }

        internal override string ToFluenceString() => "<internal: element_access>";
        public override string ToString() => "ElementAccessValue";
    }

    /// <summary>A descriptor for a broadcast call template, used in chain assignments.</summary>
    internal sealed class BroadcastCallTemplate : Value
    {
        internal Value Callable { get; private set; }
        internal List<Value> Arguments { get; private set; }
        internal int PlaceholderIndex { get; private set; }

        public BroadcastCallTemplate(Value callable, List<Value> args, int placeholderIndex) : base()
        {
            Callable = callable;
            Arguments = args;
            PlaceholderIndex = placeholderIndex;
        }

        internal override string ToFluenceString() => "<internal: broadcast_template>";
        public override string ToString() => "BroadcastTemplateValue";
    }

    /// <summary>
    /// A descriptor representing a temporary variable generated by the parser.
    /// This should be resolved by the VM and never seen by the user.
    /// </summary>
    internal sealed class TempValue : Value
    {
        internal int TempIndex { get; private set; }
        internal int RegisterIndex { get; set; } = -1;

        internal TempValue(int num)
        {
            TempIndex = num;
            Hash = TempIndex.GetHashCode();
        }

        internal override string ToFluenceString() => "<internal: temp_register>";
        internal override string ToByteCodeString() => $"__Temp{TempIndex}_{RegisterIndex}";
        public override string ToString() => $"TempValue: {TempIndex}, Index: {RegisterIndex}";
    }

    /// <summary>
    /// Represents a variable by its name. The VM resolves this to a value in the current scope.
    /// </summary>
    internal sealed class VariableValue : Value
    {
        internal string Name { get; private set; }
        internal bool IsReadOnly { get; set; }
        internal int RegisterIndex { get; set; } = -1;
        internal bool IsGlobal { get; set; }

        internal static readonly VariableValue SelfVariable = new VariableValue("self");

        internal VariableValue(string identifierValue)
        {
            Name = identifierValue;
            Hash = Name.GetHashCode();
        }

        internal VariableValue(string identifierValue, bool isReadonly)
        {
            Name = identifierValue;
            IsReadOnly = isReadonly;
            Hash = Name.GetHashCode();
        }

        internal override string ToFluenceString() => $"<internal: variable_{(IsReadOnly ? "solid" : "fluid")}>";
        internal override string ToByteCodeString() => $"Var_{Name}_{RegisterIndex}_{IsGlobal}_{IsReadOnly}";
        public override string ToString() => $"VariableValue: {Name}:{(IsReadOnly ? "solid" : "fluid")}, Index: {RegisterIndex}, IsGlobal: {IsGlobal}";
    }

    /// <summary>
    /// Represents a lambda function, holding a reference to its function body.
    /// </summary>
    internal sealed class LambdaValue : Value
    {
        internal FunctionValue Function { get; private set; }

        internal LambdaValue(FunctionValue function) : base()
        {
            Function = function;
        }

        internal override string ToFluenceString() => $"<internal: lambda__{Function.Name}__{Function.Arity}>";
        public override string ToString() => $"LambdaValue: {Function.Name}__{Function.Arity}_{Function.StartAddress}";
    }

    /// <summary>Represents a function's compile-time blueprint, including its bytecode address or native implementation.</summary>
    internal sealed class FunctionValue : Value
    {
        internal string Name { get; private set; }
        internal int Arity { get; private set; }
        internal int StartAddress { get; private set; }
        internal List<string> Arguments { get; private set; }
        internal int RefMask { get; private set; }
        internal int StartAddressInSource { get; private set; }
        internal FluenceScope DefiningScope { get; private set; }
        internal int TotalRegisterSlots { get; set; }
        internal bool BelongsToAStruct { get; private set; }

        internal FunctionValue(string name, bool inStruct, int arity, int startAddress, int lineInSource, List<string> arguments, int refMask, FluenceScope scope)
        {
            BelongsToAStruct = inStruct;
            Name = name;
            Arity = arity;
            StartAddress = startAddress;
            Arguments = arguments;

            RefMask = refMask;
            StartAddressInSource = lineInSource;
            DefiningScope = scope;

            Hash = name.GetHashCode();
        }

        internal void SetStartAddress(int addr) => StartAddress = addr;
        internal void SetName(string name) => Name = name;

        internal override string ToFluenceString() => $"<internal: function__{Name}/{Arity}, RegSize: {TotalRegisterSlots}>";
        internal override string ToByteCodeString() => $"Func_{Name}_{Arity}_{TotalRegisterSlots}_{DefiningScope}_{StartAddress}";

        public override string ToString()
        {
            if (Arguments == null || Arguments.Count == 0)
            {
                return $"FunctionValue: {Name} {FluenceDebug.FormatByteCodeAddress(StartAddress)}, #0 args, RegSize: {TotalRegisterSlots}, Scope: {DefiningScope}";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Arguments.Count; i++)
            {
                bool isRef = (RefMask & (1 << i)) != 0;

                if (isRef) sb.Append("ref ");
                sb.Append(Arguments[i]);

                if (i < Arguments.Count - 1) sb.Append(", ");
            }

            return $"FunctionValue: {Name} {FluenceDebug.FormatByteCodeAddress(StartAddress)}, #{Arity} args: [{sb}], RegSize: {TotalRegisterSlots}, Scope: {DefiningScope}";
        }
    }

    internal sealed class TryCatchValue : Value
    {
        internal int TryGoToIndex { get; set; }
        internal int CatchGoToIndex { get; set; }
        internal string ExceptionVarName { get; set; }
        internal int ExceptionAsVarRegisterIndex { get; set; }
        internal bool HasExceptionVar { get; private set; }
        internal bool CaughtException { get; set; }

        internal TryCatchValue(int tryGoToIndex, string exceptionVarName, int exceptionVarRegisterIndex, int catchGoToIndex, bool hasExceptionVar) : base()
        {
            ExceptionVarName = exceptionVarName;
            TryGoToIndex = tryGoToIndex;
            ExceptionAsVarRegisterIndex = exceptionVarRegisterIndex;
            HasExceptionVar = hasExceptionVar;
            CatchGoToIndex = catchGoToIndex;
        }

        internal override string ToFluenceString() => "<internal: try_catch__value>";
        public override string ToString() => $"TryCatchValue: TryJmp: {TryGoToIndex}, CatchJmp: {CatchGoToIndex}.";
    }

    /// <summary>
    /// A descriptor representing a property access operation.
    /// The parser resolves this into GetField or SetField bytecode.
    /// </summary>
    internal sealed class PropertyAccessValue : Value
    {
        internal Value Target { get; private set; }
        internal string FieldName { get; private set; }

        internal PropertyAccessValue(Value target, string fieldName) : base()
        {
            Target = target;
            FieldName = fieldName;
        }

        internal override string ToFluenceString() => "<internal: property_access>";
        public override string ToString() => $"PropertyAccessValue<{Target}:{FieldName}>";
    }

    /// <summary>Represents a specific member of an enum, holding both its name and integer value.</summary>
    public sealed class EnumValue : Value
    {
        internal string EnumTypeName { get; private set; }
        internal string MemberName { get; private set; }
        internal int Value { get; private set; }

        internal EnumValue(string enumTypeName, string memberName, int value) : base()
        {
            EnumTypeName = enumTypeName;
            MemberName = memberName;
            Value = value;
        }

        internal override string ToFluenceString() => $"{EnumTypeName}.{MemberName}";
        public override string ToString() => $"EnumValue: {EnumTypeName}.{MemberName}";
    }
}