using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fluence.Unity.RuntimeTypes
{
    /// <summary>
    /// A discriminator to identify the fundamental type of data stored in a <see cref="RuntimeValue"/>.
    /// </summary>
    public enum RuntimeValueType : byte
    {
        Nil,
        Boolean,
        Number,
        Object // For all heap-allocated types like strings, lists, and struct instances.
    }

    /// <summary>
    /// A sub-discriminator used when <see cref="RuntimeValueType"/> is <see cref="RuntimeValueType.Number"/>.
    /// </summary>
    public enum RuntimeNumberType : byte
    {
        /// <summary>
        /// System.Int32 (32-bit integer).
        /// </summary>
        Int = 0,

        /// <summary>
        /// System.Int64 (64-bit integer).
        /// </summary>
        Long = 1,

        /// <summary>
        /// System.Single (32-bit floating-point).
        /// </summary>
        Float = 2,

        /// <summary>
        /// System.Double (64-bit floating-point).
        /// </summary>
        Double = 3,

        /// <summary>
        /// Represents a non-numeric or uninitialized state. Should not appear in arithmetic.
        /// </summary>
        Unknown = 255
    }

    /// <summary>
    /// Represents any value that can exist in the Fluence VM at runtime.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct RuntimeValue
    {
        [FieldOffset(0)]
        public readonly long LongValue;
        [FieldOffset(0)]
        public readonly double DoubleValue;
        [FieldOffset(0)]
        public readonly int IntValue;
        [FieldOffset(0)]
        public readonly float FloatValue;

        [FieldOffset(8)]
        internal readonly object ObjectReference;
        [FieldOffset(16)]
        public readonly RuntimeValueType Type;
        [FieldOffset(17)]
        public readonly RuntimeNumberType NumberType;

        public static readonly RuntimeValue Nil = new RuntimeValue(RuntimeValueType.Nil);
        public static readonly RuntimeValue True = new RuntimeValue(true);
        public static readonly RuntimeValue False = new RuntimeValue(false);

        /// <summary>
        /// If the <see cref="RuntimeValue"/> does not represent a numeric value or a boolean, 
        /// then this property returns the complex object reference it holds. This can be from 
        /// <see cref="CharObject"/>, <see cref="StringObject"/> to <see cref="Wrapper"/>.
        /// </summary>
        public object ObjectValue => ObjectReference;

        private RuntimeValue(RuntimeValueType type)
        {
            this = default;
            Type = type;
        }

        internal RuntimeValue(bool value) : this(RuntimeValueType.Boolean)
        {
            IntValue = value ? 1 : 0;
        }

        internal RuntimeValue(double value) : this(RuntimeValueType.Number)
        {
            NumberType = RuntimeNumberType.Double;
            DoubleValue = value;
        }

        internal RuntimeValue(int value) : this(RuntimeValueType.Number)
        {
            NumberType = RuntimeNumberType.Int;
            IntValue = value;
        }

        internal RuntimeValue(float value) : this(RuntimeValueType.Number)
        {
            NumberType = RuntimeNumberType.Float;
            FloatValue = value;
        }

        internal RuntimeValue(long value) : this(RuntimeValueType.Number)
        {
            NumberType = RuntimeNumberType.Long;
            LongValue = value;
        }

        internal RuntimeValue(object? obj) : this(RuntimeValueType.Object)
        {
            ObjectReference ??= obj ?? RuntimeValue.Nil;
        }

        internal bool Is<T>() where T : class => ObjectReference is T;

        internal bool IsNot<T>() where T : class => ObjectReference is not T;

        internal bool Is<T>(out T? value) where T : class
        {
            value = ObjectReference as T;
            return value != null;
        }

        internal bool IsNot<T>(out T? value) where T : class
        {
            if (ObjectReference is T)
            {
                value = null;
                return false;
            }
            value = ObjectReference as T;
            return true;
        }

        public static bool operator !=(RuntimeValue left, RuntimeValue right)
        {
            return !(left == right);
        }

        public static bool operator ==(RuntimeValue left, RuntimeValue right)
        {
            if (left.Type != right.Type) return false;

            return left.Type switch
            {
                RuntimeValueType.Nil => true,
                RuntimeValueType.Boolean => left.IntValue == right.IntValue,
                RuntimeValueType.Number => left.NumberType == right.NumberType && left.NumberType switch
                {
                    RuntimeNumberType.Int => left.IntValue == right.IntValue,
                    RuntimeNumberType.Long => left.LongValue == right.LongValue,
                    RuntimeNumberType.Float => left.FloatValue == right.FloatValue,
                    _ => left.DoubleValue == right.DoubleValue
                },
                RuntimeValueType.Object => Equals(left.ObjectReference, right.ObjectReference),
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly double ToDouble() => NumberType switch
        {
            RuntimeNumberType.Int => IntValue,
            RuntimeNumberType.Long => LongValue,
            RuntimeNumberType.Float => FloatValue,
            RuntimeNumberType.Double => DoubleValue,
            _ => double.NaN,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly float ToFloat() => NumberType switch
        {
            RuntimeNumberType.Int => IntValue,
            RuntimeNumberType.Long => LongValue,
            RuntimeNumberType.Float => FloatValue,
            RuntimeNumberType.Double => (float)DoubleValue,
            _ => float.NaN,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly long ToLong() => NumberType switch
        {
            RuntimeNumberType.Int => IntValue,
            RuntimeNumberType.Long => LongValue,
            RuntimeNumberType.Float => (long)FloatValue,
            RuntimeNumberType.Double => (long)DoubleValue,
            _ => 0,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly int ToInt() => NumberType switch
        {
            RuntimeNumberType.Int => IntValue,
            RuntimeNumberType.Long => (int)LongValue,
            RuntimeNumberType.Float => (int)FloatValue,
            RuntimeNumberType.Double => (int)DoubleValue,
            _ => 0,
        };

        /// <summary>
        /// Safely casts the internal <see cref="ObjectReference"/> to the specified type.
        /// </summary>
        public T As<T>() where T : class => ObjectReference as T;

        /// <summary>
        /// Gets a value indicating whether the <see cref="RuntimeValue"/> is "truthy".
        /// In Fluence, only 'nil' and 'false' are considered falsy.
        /// </summary>
        public bool IsTruthy => !(Type == RuntimeValueType.Nil || (Type == RuntimeValueType.Boolean && IntValue == 0));

        /// <inheritdoc/>
        public override string ToString()
        {
            return Type switch
            {
                RuntimeValueType.Nil => "nil",
                RuntimeValueType.Boolean => IntValue != 0 ? "true" : "false",
                RuntimeValueType.Number => NumberType switch
                {
                    RuntimeNumberType.Int => IntValue.ToString(),
                    RuntimeNumberType.Long => LongValue.ToString(),
                    RuntimeNumberType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
                    RuntimeNumberType.Double => DoubleValue.ToString(CultureInfo.InvariantCulture),
                    _ => "??? (Invalid NumberType)"
                },
                RuntimeValueType.Object => ObjectReference?.ToString() ?? "nil",
                _ => "??? (Undefined Value)",
            };
        }
    }
}