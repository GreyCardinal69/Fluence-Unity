using Fluence.Unity.RuntimeTypes;
using System.Runtime.CompilerServices;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceByteCode.InstructionLine;

namespace Fluence.Unity.VirtualMachine
{
    internal static class InlineCacheManager
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue AddValues(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                double leftVal = left.ToDouble();
                double rightVal = right.ToDouble();
                return new RuntimeValue(leftVal + rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                long rightVal = right.ToLong();
                return new RuntimeValue(leftVal + rightVal);
            }

            return new RuntimeValue(left.IntValue + right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue SubValues(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                double leftVal = left.ToDouble();
                double rightVal = right.ToDouble();
                return new RuntimeValue(leftVal - rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                long rightVal = right.ToLong();
                return new RuntimeValue(leftVal - rightVal);
            }

            return new RuntimeValue(left.IntValue - right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue DivValues(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                double leftVal = left.ToDouble();
                double rightVal = right.ToDouble();
                return new RuntimeValue(leftVal / rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                long rightVal = right.ToLong();
                return new RuntimeValue(leftVal / rightVal);
            }

            return new RuntimeValue(left.IntValue / right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue MulValues(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                double leftVal = left.ToDouble();
                double rightVal = right.ToDouble();
                return new RuntimeValue(leftVal * rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                long rightVal = right.ToLong();
                return new RuntimeValue(leftVal * rightVal);
            }

            return new RuntimeValue(left.IntValue * right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue ModuloValues(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                double leftVal = left.ToDouble();
                double rightVal = right.ToDouble();
                return new RuntimeValue(leftVal % rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                long rightVal = right.ToLong();
                return new RuntimeValue(leftVal % rightVal);
            }

            return new RuntimeValue(left.IntValue % right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue PowerValues(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                double leftVal = left.ToDouble();
                double rightVal = right.ToDouble();
                return new RuntimeValue(Math.Pow(leftVal, rightVal));
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                long rightVal = right.ToLong();
                return new RuntimeValue(Math.Pow(leftVal, rightVal));
            }

            return new RuntimeValue(Math.Pow(left.IntValue, right.IntValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue BitwiseShiftRight(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                long leftVal = (long)left.ToDouble();
                int rightVal = (int)right.ToDouble();
                return new RuntimeValue(leftVal >> rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                int rightVal = (int)right.ToLong();
                return new RuntimeValue(leftVal >> rightVal);
            }

            return new RuntimeValue(left.IntValue >> right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue BitwiseShiftLeft(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                long leftVal = (long)left.ToDouble();
                int rightVal = (int)right.ToDouble();
                return new RuntimeValue(leftVal << rightVal);
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                long leftVal = left.ToLong();
                int rightVal = (int)right.ToLong();
                return new RuntimeValue(leftVal << rightVal);
            }

            return new RuntimeValue(left.IntValue << right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue BitwiseXor(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                return new RuntimeValue((long)left.ToDouble() ^ (long)right.ToDouble());
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                return new RuntimeValue(left.ToLong() ^ right.ToLong());
            }

            return new RuntimeValue(left.IntValue ^ right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue BitwiseOr(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                return new RuntimeValue((long)left.ToDouble() | (long)right.ToDouble());
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                return new RuntimeValue(left.ToLong() | right.ToLong());
            }

            return new RuntimeValue(left.IntValue | right.IntValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RuntimeValue BitwiseAnd(FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right)
        {
            if (left.NumberType >= RuntimeNumberType.Float || right.NumberType >= RuntimeNumberType.Float)
            {
                return new RuntimeValue((long)left.ToDouble() & (long)right.ToDouble());
            }

            if (left.NumberType == RuntimeNumberType.Long || right.NumberType == RuntimeNumberType.Long)
            {
                return new RuntimeValue(left.ToLong() & right.ToLong());
            }

            return new RuntimeValue(left.IntValue & right.IntValue);
        }

        private static bool AttemptToModifyAReadonlyVariable(InstructionLine insn, FluenceVirtualMachine vm, out string name)
        {
            if (insn.Instruction == InstructionCode.Assign)
            {
                return IsReadonlyVariable(insn.Lhs, vm, out name);
            }
            else // Assign two.
            {
                if (IsReadonlyVariable(insn.Lhs, vm, out name))
                {
                    return true;
                }

                if (IsReadonlyVariable(insn.Rhs2, vm, out name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A helper that checks if a Value is a variable and if that variable is readonly.
        /// If it is, it extracts the name and returns true. Otherwise, it returns false.
        /// </summary>
        private static bool IsReadonlyVariable(Value value, FluenceVirtualMachine vm, out string name)
        {
            name = "";

            if (value is VariableValue variable && vm.VariableIsReadonly(variable))
            {
                name = variable.Name;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AreEqual(RuntimeValue left, RuntimeValue right)
        {
            if (left.Type == RuntimeValueType.Number && right.Type == RuntimeValueType.Number)
            {
                return left.DoubleValue == right.DoubleValue;
            }

            if (left.Type == RuntimeValueType.Boolean && right.Type == RuntimeValueType.Boolean)
            {
                return left.IntValue == right.IntValue;
            }

            if (left.Type is RuntimeValueType.Nil || right.Type is RuntimeValueType.Nil)
                return left.Type is RuntimeValueType.Nil && right.Type is RuntimeValueType.Nil;

            return left.Equals(right);
        }

        private static SpecializedOpcodeHandler? CreateBinaryNumericHandler(
            InstructionLine insn,
            RuntimeValue left,
            RuntimeValue right,
            FluenceVirtualMachine vm,
            Func<FluenceVirtualMachine, RuntimeValue, RuntimeValue, RuntimeValue> opFunction)
        {
            if (left.Type != RuntimeValueType.Number || right.Type != RuntimeValueType.Number) return null;

            if (AttemptToModifyAReadonlyVariable(insn, vm, out string name))
            {
                vm.CreateAndThrowRuntimeException($"Runtime Error: Cannot assign to the readonly solid variable '{name}'.");
                return null;
            }

            if (insn.Rhs2 is NumberValue num)
            {
                bool isZero = false;
                switch (num.Type)
                {
                    case NumberValue.NumberType.Integer:
                        isZero = (int)num.Value == 0;
                        break;
                    case NumberValue.NumberType.Float:
                        isZero = (float)num.Value == 0;
                        break;
                    case NumberValue.NumberType.Double:
                        isZero = (double)num.Value == 0;
                        break;
                    case NumberValue.NumberType.Long:
                        isZero = (long)num.Value == 0;
                        break;
                }
                if (isZero && (insn.Instruction is InstructionCode.Divide or InstructionCode.Modulo))
                {
                    vm.SignalError($"Runtime Error: Division by zero in {(insn.Instruction == InstructionCode.Modulo ? "Modulo" : "Division")} operation.");
                }
            }

            Value lhsOperand = insn.Rhs;
            Value rhsOperand = insn.Rhs2;
            Value destOperand = insn.Lhs;
            RuntimeValue[] globalRegisters = vm.GlobalRegisters;

            if (insn.Lhs is TempValue destTemp)
            {
                int destIndex = destTemp.RegisterIndex;

                if (lhsOperand is VariableValue varLeft && rhsOperand is VariableValue varRight)
                {
                    int leftIndex = varLeft.RegisterIndex;
                    int rightIndex = varRight.RegisterIndex;

                    if (varLeft.IsGlobal && varRight.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], globalRegisters[rightIndex]);

                    if (varLeft.IsGlobal && !varRight.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]);

                    if (!varLeft.IsGlobal && varRight.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is TempValue tempLeft && rhsOperand is TempValue tempRight)
                {
                    int leftIndex = tempLeft.RegisterIndex;
                    int rightIndex = tempRight.RegisterIndex;
                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is TempValue tempLeft2 && rhsOperand is VariableValue varRight2)
                {
                    int leftIndex = tempLeft2.RegisterIndex;
                    int rightIndex = varRight2.RegisterIndex;

                    if (varRight2.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is VariableValue varLeft2 && rhsOperand is TempValue tempRight2)
                {
                    int leftIndex = varLeft2.RegisterIndex;
                    int rightIndex = tempRight2.RegisterIndex;

                    if (varLeft2.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is TempValue tempLeft3 && rhsOperand is NumberValue num2)
                {
                    int leftIndex = tempLeft3.RegisterIndex;
                    RuntimeValue rightConst = vm.GetRuntimeValue(num2, insn);
                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], rightConst);
                }

                if (lhsOperand is VariableValue varOp2 && rhsOperand is NumberValue numConst)
                {
                    int leftIndex = varOp2.RegisterIndex;
                    RuntimeValue rightConst = vm.GetRuntimeValue(numConst, insn);

                    if (varOp2.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], rightConst);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], rightConst);
                }

                if (lhsOperand is NumberValue num1 && rhsOperand is NumberValue num3)
                {
                    RuntimeValue precalculated = opFunction(vm, vm.GetRuntimeValue(num1, insn), vm.GetRuntimeValue(num3, insn));
                    return (i, v) => v.CurrentRegisters[destIndex] = precalculated;
                }

                if (lhsOperand is NumberValue num4 && rhsOperand is TempValue temp4)
                {
                    RuntimeValue leftConst = vm.GetRuntimeValue(num4, insn);
                    int rightIndex = temp4.RegisterIndex;
                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, leftConst, v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is NumberValue num5 && rhsOperand is VariableValue varRight3)
                {
                    RuntimeValue leftConst = vm.GetRuntimeValue(num5, insn);
                    int rightIndex = varRight3.RegisterIndex;

                    if (varRight3.IsGlobal)
                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, leftConst, globalRegisters[rightIndex]);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, leftConst, v.CurrentRegisters[rightIndex]);
                }
            }
            else if (insn.Lhs is VariableValue destVar)
            {
                int destIndex = destVar.RegisterIndex;
                bool destIsGlobal = destVar.IsGlobal;

                if (lhsOperand is VariableValue varLeft && rhsOperand is VariableValue varRight)
                {
                    int leftIndex = varLeft.RegisterIndex;
                    int rightIndex = varRight.RegisterIndex;

                    if (destIsGlobal)
                    {
                        if (varLeft.IsGlobal && varRight.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], globalRegisters[rightIndex]);

                        if (varLeft.IsGlobal && !varRight.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]);

                        if (!varLeft.IsGlobal && varRight.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]);

                        return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                    }
                    else
                    {
                        if (varLeft.IsGlobal && varRight.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], globalRegisters[rightIndex]);

                        if (varLeft.IsGlobal && !varRight.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]);

                        if (!varLeft.IsGlobal && varRight.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]);

                        return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                    }
                }

                if (lhsOperand is TempValue tempLeft && rhsOperand is TempValue tempRight)
                {
                    int leftIndex = tempLeft.RegisterIndex;
                    int rightIndex = tempRight.RegisterIndex;

                    if (destIsGlobal)
                        return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is TempValue tempLeft2 && rhsOperand is VariableValue varRight2)
                {
                    int leftIndex = tempLeft2.RegisterIndex;
                    int rightIndex = varRight2.RegisterIndex;

                    if (destIsGlobal)
                    {
                        if (varRight2.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]);
                        else
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                    }
                    else
                    {
                        if (varRight2.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]);
                        else
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                    }
                }

                if (lhsOperand is VariableValue varLeft2 && rhsOperand is TempValue tempRight2)
                {
                    int leftIndex = varLeft2.RegisterIndex;
                    int rightIndex = tempRight2.RegisterIndex;

                    if (destIsGlobal)
                    {
                        if (varLeft2.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                        else
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                    }
                    else
                    {
                        if (varLeft2.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                        else
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]);
                    }
                }

                if (lhsOperand is TempValue tempLeft3 && rhsOperand is NumberValue num2)
                {
                    int leftIndex = tempLeft3.RegisterIndex;
                    RuntimeValue rightConst = vm.GetRuntimeValue(num2, insn);

                    if (destIsGlobal)
                        return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], rightConst);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], rightConst);
                }

                if (lhsOperand is VariableValue varOp2 && rhsOperand is NumberValue numConst)
                {
                    int leftIndex = varOp2.RegisterIndex;
                    RuntimeValue rightConst = vm.GetRuntimeValue(numConst, insn);

                    if (destIsGlobal)
                    {
                        if (varOp2.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], rightConst);
                        else
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], rightConst);
                    }
                    else
                    {
                        if (varOp2.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, globalRegisters[leftIndex], rightConst);
                        else
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, v.CurrentRegisters[leftIndex], rightConst);
                    }
                }

                if (lhsOperand is NumberValue && rhsOperand is NumberValue)
                {
                    RuntimeValue precalculated = opFunction(vm, vm.GetRuntimeValue(lhsOperand, insn), vm.GetRuntimeValue(rhsOperand, insn));

                    if (destIsGlobal)
                        return (i, v) => globalRegisters[destIndex] = precalculated;

                    return (i, v) => v.CurrentRegisters[destIndex] = precalculated;
                }

                if (lhsOperand is NumberValue num4 && rhsOperand is TempValue temp4)
                {
                    RuntimeValue leftConst = vm.GetRuntimeValue(num4, insn);
                    int rightIndex = temp4.RegisterIndex;

                    if (destIsGlobal)
                        return (i, v) => globalRegisters[destIndex] = opFunction(v, leftConst, v.CurrentRegisters[rightIndex]);

                    return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, leftConst, v.CurrentRegisters[rightIndex]);
                }

                if (lhsOperand is NumberValue num5 && rhsOperand is VariableValue varRight3)
                {
                    RuntimeValue leftConst = vm.GetRuntimeValue(num5, insn);
                    int rightIndex = varRight3.RegisterIndex;

                    if (destIsGlobal)
                    {
                        if (varRight3.IsGlobal)
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, leftConst, globalRegisters[rightIndex]);
                        else
                            return (i, v) => globalRegisters[destIndex] = opFunction(v, leftConst, v.CurrentRegisters[rightIndex]);
                    }
                    else
                    {
                        if (varRight3.IsGlobal)
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, leftConst, globalRegisters[rightIndex]);
                        else
                            return (i, v) => v.CurrentRegisters[destIndex] = opFunction(v, leftConst, v.CurrentRegisters[rightIndex]);
                    }
                }
            }

            return null;
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedAddHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, AddValues);

        internal static SpecializedOpcodeHandler? CreateSpecializedSubtractionHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, SubValues);

        internal static SpecializedOpcodeHandler? CreateSpecializedDivHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, DivValues);

        internal static SpecializedOpcodeHandler? CreateSpecializedMulHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, MulValues);

        internal static SpecializedOpcodeHandler? CreateSpecializedModuloHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, ModuloValues);

        internal static SpecializedOpcodeHandler? CreateSpecializedPowerHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, PowerValues);

        internal static SpecializedOpcodeHandler? CreateBitwiseRightShiftHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, BitwiseShiftRight);

        internal static SpecializedOpcodeHandler? CreateBitwiseLeftShiftHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, BitwiseShiftLeft);

        internal static SpecializedOpcodeHandler? CreateBitwiseXorHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, BitwiseXor);

        internal static SpecializedOpcodeHandler? CreateBitwiseOrHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, BitwiseOr);

        internal static SpecializedOpcodeHandler? CreateBitwiseAndHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue left, RuntimeValue right) =>
            CreateBinaryNumericHandler(insn, left, right, vm, BitwiseAnd);

        /// <summary>
        /// Defines the type of comparison for a specialized branch handler.
        /// </summary>
        internal enum ComparisonOperation
        {
            GreaterThan,
            GreaterOrEqual,
            LessThan,
            LessOrEqual
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareNumeric(RuntimeValue left, RuntimeValue right)
        {
            RuntimeNumberType lt = left.NumberType;
            RuntimeNumberType rt = right.NumberType;

            if (lt == RuntimeNumberType.Int && rt == RuntimeNumberType.Int)
                return left.IntValue.CompareTo(right.IntValue);

            if (lt == RuntimeNumberType.Long && rt == RuntimeNumberType.Long)
                return left.LongValue.CompareTo(right.LongValue);

            if (lt == RuntimeNumberType.Float && rt == RuntimeNumberType.Float)
                return left.FloatValue.CompareTo(right.FloatValue);

            if (lt == RuntimeNumberType.Double && rt == RuntimeNumberType.Double)
                return left.DoubleValue.CompareTo(right.DoubleValue);

            RuntimeNumberType promoted = (RuntimeNumberType)Math.Max((byte)lt, (byte)rt);

            return promoted switch
            {
                RuntimeNumberType.Long =>
                    left.ToLong().CompareTo(right.ToLong()),

                RuntimeNumberType.Float =>
                    left.ToFloat().CompareTo(right.ToFloat()),

                RuntimeNumberType.Double =>
                    left.ToDouble().CompareTo(right.ToDouble()),

                _ => throw new InvalidOperationException("Invalid numeric promotion."),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsGreaterThan(RuntimeValue left, RuntimeValue right)
            => CompareNumeric(left, right) > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsGreaterOrEqual(RuntimeValue left, RuntimeValue right)
            => CompareNumeric(left, right) >= 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLessThan(RuntimeValue left, RuntimeValue right)
            => CompareNumeric(left, right) < 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLessOrEqual(RuntimeValue left, RuntimeValue right)
            => CompareNumeric(left, right) <= 0;

        internal static SpecializedOpcodeHandler? CreateSpecializedBranchHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue right, bool target)
        {
            Value lhsOperand = insn.Rhs;
            Value rhsOperand = insn.Rhs2;
            int jumpTarget = ((GoToValue)insn.Lhs).Address;
            RuntimeValue[] globalRegisters = vm.GlobalRegisters;

            if ((lhsOperand is VariableValue || lhsOperand is TempValue) &&
                (rhsOperand is VariableValue || rhsOperand is TempValue))
            {
                VariableValue? leftVar = lhsOperand as VariableValue;
                int leftIndex = leftVar?.RegisterIndex ?? ((TempValue)lhsOperand).RegisterIndex;
                bool leftIsGlobal = leftVar?.IsGlobal ?? false;

                VariableValue? rightVar = rhsOperand as VariableValue;
                int rightIndex = rightVar?.RegisterIndex ?? ((TempValue)rhsOperand).RegisterIndex;
                bool rightIsGlobal = rightVar?.IsGlobal ?? false;

                if (leftIsGlobal && rightIsGlobal)
                    return (i, v) =>
                    {
                        if (AreEqual(globalRegisters[leftIndex], globalRegisters[rightIndex]) == target)
                            v.SetInstructionPointer(jumpTarget);
                    };

                if (leftIsGlobal && !rightIsGlobal)
                    return (i, v) =>
                    {
                        if (AreEqual(globalRegisters[leftIndex], v.CurrentRegisters[rightIndex]) == target)
                            v.SetInstructionPointer(jumpTarget);
                    };

                if (!leftIsGlobal && rightIsGlobal)
                    return (i, v) =>
                    {
                        if (AreEqual(v.CurrentRegisters[leftIndex], globalRegisters[rightIndex]) == target)
                            v.SetInstructionPointer(jumpTarget);
                    };

                return (i, v) =>
                {
                    if (AreEqual(v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex]) == target)
                        v.SetInstructionPointer(jumpTarget);
                };
            }

            if ((lhsOperand is VariableValue || lhsOperand is TempValue) && rhsOperand is NumberValue)
            {
                VariableValue? leftVar = lhsOperand as VariableValue;
                int leftIndex = leftVar?.RegisterIndex ?? ((TempValue)lhsOperand).RegisterIndex;
                bool leftIsGlobal = leftVar?.IsGlobal ?? false;
                RuntimeValue constValue = right;

                if (leftIsGlobal)
                    return (i, v) =>
                    {
                        if (AreEqual(globalRegisters[leftIndex], constValue) == target)
                            v.SetInstructionPointer(jumpTarget);
                    };

                return (i, v) =>
                {
                    if (AreEqual(v.CurrentRegisters[leftIndex], constValue) == target)
                        v.SetInstructionPointer(jumpTarget);
                };
            }

            if (lhsOperand is NumberValue && (rhsOperand is VariableValue || rhsOperand is TempValue))
            {
                RuntimeValue constValue = vm.GetRuntimeValue(lhsOperand, insn);
                VariableValue? rightVar = rhsOperand as VariableValue;
                int rightIndex = rightVar?.RegisterIndex ?? ((TempValue)rhsOperand).RegisterIndex;
                bool rightIsGlobal = rightVar?.IsGlobal ?? false;

                if (rightIsGlobal)
                    return (i, v) =>
                    {
                        if (AreEqual(constValue, globalRegisters[rightIndex]) == target)
                            v.SetInstructionPointer(jumpTarget);
                    };

                return (i, v) =>
                {
                    if (AreEqual(constValue, v.CurrentRegisters[rightIndex]) == target)
                        v.SetInstructionPointer(jumpTarget);
                };
            }

            if (lhsOperand is NumberValue && rhsOperand is NumberValue)
            {
                RuntimeValue leftConst = vm.GetRuntimeValue(lhsOperand, insn);
                RuntimeValue rightConst = right;

                if (AreEqual(leftConst, rightConst) == target)
                {
                    return (i, v) => v.SetInstructionPointer(jumpTarget);
                }
                else
                {
                    // The condition is always false, so this instruction does nothing.
                    return (i, v) => { /*  */ };
                }
            }

            return null;
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedComparisonBranchHandler(InstructionLine insn, FluenceVirtualMachine vm, ComparisonOperation op)
        {
            Value lhsOperand = insn.Rhs;
            Value rhsOperand = insn.Rhs2;
            int jumpTarget = ((GoToValue)insn.Lhs).Address;
            RuntimeValue[] globalRegisters = vm.GlobalRegisters;

            Func<RuntimeValue, RuntimeValue, bool> comparisonFunc = op switch
            {
                ComparisonOperation.GreaterThan => IsGreaterThan,
                ComparisonOperation.GreaterOrEqual => IsGreaterOrEqual,
                ComparisonOperation.LessThan => IsLessThan,
                ComparisonOperation.LessOrEqual => IsLessOrEqual,
                _ => throw new NotImplementedException(),
            };

            if (lhsOperand is VariableValue or TempValue && rhsOperand is VariableValue or TempValue)
            {
                VariableValue? leftVar = lhsOperand as VariableValue;
                int leftIndex = leftVar?.RegisterIndex ?? ((TempValue)lhsOperand).RegisterIndex;
                bool leftIsGlobal = leftVar?.IsGlobal ?? false;

                VariableValue? rightVar = rhsOperand as VariableValue;
                int rightIndex = rightVar?.RegisterIndex ?? ((TempValue)rhsOperand).RegisterIndex;
                bool rightIsGlobal = rightVar?.IsGlobal ?? false;

                if (leftIsGlobal && rightIsGlobal)
                    return (i, v) =>
                    {
                        if (comparisonFunc(globalRegisters[leftIndex], globalRegisters[rightIndex])) v.SetInstructionPointer(jumpTarget);
                    };
                if (leftIsGlobal && !rightIsGlobal)
                    return (i, v) =>
                    {
                        if (comparisonFunc(globalRegisters[leftIndex], v.CurrentRegisters[rightIndex])) v.SetInstructionPointer(jumpTarget);
                    };
                if (!leftIsGlobal && rightIsGlobal)
                    return (i, v) =>
                    {
                        if (comparisonFunc(v.CurrentRegisters[leftIndex], globalRegisters[rightIndex])) v.SetInstructionPointer(jumpTarget);
                    };

                return (i, v) =>
                {
                    if (comparisonFunc(v.CurrentRegisters[leftIndex], v.CurrentRegisters[rightIndex])) v.SetInstructionPointer(jumpTarget);
                };
            }

            if (lhsOperand is VariableValue or TempValue && rhsOperand is NumberValue)
            {
                VariableValue? leftVar = lhsOperand as VariableValue;
                int leftIndex = leftVar?.RegisterIndex ?? ((TempValue)lhsOperand).RegisterIndex;
                bool leftIsGlobal = leftVar?.IsGlobal ?? false;
                RuntimeValue constValue = vm.GetRuntimeValue(rhsOperand, insn);

                if (leftIsGlobal)
                    return (i, v) =>
                    {
                        if (comparisonFunc(globalRegisters[leftIndex], constValue)) v.SetInstructionPointer(jumpTarget);
                    };

                return (i, v) =>
                {
                    if (comparisonFunc(v.CurrentRegisters[leftIndex], constValue)) v.SetInstructionPointer(jumpTarget);
                };
            }

            if (lhsOperand is NumberValue && rhsOperand is VariableValue or TempValue)
            {
                RuntimeValue constValue = vm.GetRuntimeValue(lhsOperand, insn);
                VariableValue? rightVar = rhsOperand as VariableValue;
                int rightIndex = rightVar?.RegisterIndex ?? ((TempValue)rhsOperand).RegisterIndex;
                bool rightIsGlobal = rightVar?.IsGlobal ?? false;

                if (rightIsGlobal)
                    return (i, v) =>
                    {
                        if (comparisonFunc(constValue, globalRegisters[rightIndex])) v.SetInstructionPointer(jumpTarget);
                    };

                return (i, v) =>
                {
                    if (comparisonFunc(constValue, v.CurrentRegisters[rightIndex])) v.SetInstructionPointer(jumpTarget);
                };
            }

            if (lhsOperand is NumberValue && rhsOperand is NumberValue)
            {
                RuntimeValue leftConst = vm.GetRuntimeValue(lhsOperand, insn);
                RuntimeValue rightConst = vm.GetRuntimeValue(rhsOperand, insn);

                if (comparisonFunc(leftConst, rightConst))
                {
                    return (i, v) => v.SetInstructionPointer(jumpTarget);
                }
                else
                {
                    return (i, v) => { /* always false. */ };
                }
            }

            return null;
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedIncrementIntUnrestrictedHandler(InstructionLine insn, FluenceVirtualMachine vm)
        {
            int index;

            if (insn.Lhs is TempValue temp)
            {
                index = temp.RegisterIndex;
            }
            else if (insn.Lhs is VariableValue var)
            {
                index = var.RegisterIndex;
            }
            else
            {
                return null;
            }

            return (i, v) =>
            {
                ref RuntimeValue reg = ref v.CurrentRegisters[index];
                reg = new RuntimeValue(reg.IntValue + 1);
            };
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedIterNextHandler(InstructionLine insn, IteratorObject iterator)
        {
            TempValue iteratorReg = (TempValue)insn.Lhs;
            TempValue valueReg = (TempValue)insn.Rhs;
            TempValue continueFlagReg = (TempValue)insn.Rhs2;

            if (iterator.Iterable is RangeObject range)
            {
                int start = range.Start.IntValue;
                int end = range.End.IntValue;
                int step = start <= end ? 1 : -1;

                return (instruction, vm) =>
                {
                    RuntimeValue iterVal = vm.CurrentRegisters[iteratorReg.RegisterIndex];
                    IteratorObject iter = (IteratorObject)iterVal.ObjectReference;

                    int currentValue = start + iter.CurrentIndex;

                    if (start <= end ? currentValue <= end : currentValue >= end)
                    {
                        vm.SetRegister(valueReg, new RuntimeValue(currentValue));
                        vm.SetRegister(continueFlagReg, RuntimeValue.True);
                        iter.CurrentIndex += step;
                    }
                    else
                    {
                        vm.SetRegister(valueReg, RuntimeValue.Nil);
                        vm.SetRegister(continueFlagReg, RuntimeValue.False);
                    }
                };
            }

            if (iterator.Iterable is ListObject)
            {
                return (instruction, vm) =>
                {
                    RuntimeValue iterVal = vm.CurrentRegisters[iteratorReg.RegisterIndex];
                    IteratorObject iter = (IteratorObject)iterVal.ObjectReference;
                    ListObject listRef = (ListObject)iter.Iterable;

                    if (iter.CurrentIndex < listRef!.Elements.Count)
                    {
                        vm.SetRegister(valueReg, listRef.Elements[iter.CurrentIndex]);
                        vm.SetRegister(continueFlagReg, RuntimeValue.True);
                        iter.CurrentIndex++;
                    }
                    else
                    {
                        vm.SetRegister(valueReg, RuntimeValue.Nil);
                        vm.SetRegister(continueFlagReg, RuntimeValue.False);
                    }
                };
            }

            return null;
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedGetElementHandler(InstructionLine insn, FluenceVirtualMachine vm, RuntimeValue collection, RuntimeValue index)
        {
            if (index.Type != RuntimeValueType.Number) return null;

            Value collectionOperand = insn.Rhs;
            Value indexOperand = insn.Rhs2;
            int destIndex = ((TempValue)insn.Lhs).RegisterIndex;
            RuntimeValue[] globalRegisters = vm.GlobalRegisters;

            if (collection.ObjectReference is ListObject)
            {
                VariableValue? collectionVar = collectionOperand as VariableValue;
                int collectionIndex = collectionVar?.RegisterIndex ?? ((TempValue)collectionOperand).RegisterIndex;

                if (indexOperand is NumberValue num)
                {
                    int constIndex = (int)num.Value;
                    if (collectionVar?.IsGlobal ?? false)
                        return (i, v) => v.CurrentRegisters[destIndex] = ((ListObject)globalRegisters[collectionIndex].ObjectReference).Elements[constIndex];
                    else
                        return (i, v) => v.CurrentRegisters[destIndex] = ((ListObject)v.CurrentRegisters[collectionIndex].ObjectReference).Elements[constIndex];
                }

                if (indexOperand is VariableValue or TempValue)
                {
                    VariableValue? indexVar = indexOperand as VariableValue;
                    int indexRegIndex = indexVar?.RegisterIndex ?? ((TempValue)indexOperand).RegisterIndex;

                    if (collectionVar?.IsGlobal ?? false)
                    {
                        if (indexVar?.IsGlobal ?? false)
                            return (i, v) => v.CurrentRegisters[destIndex] = ((ListObject)globalRegisters[collectionIndex].ObjectReference).Elements[globalRegisters[indexRegIndex].IntValue];
                        else
                            return (i, v) => v.CurrentRegisters[destIndex] = ((ListObject)globalRegisters[collectionIndex].ObjectReference).Elements[v.CurrentRegisters[indexRegIndex].IntValue];
                    }
                    else
                    {
                        if (indexVar?.IsGlobal ?? false)
                            return (i, v) => v.CurrentRegisters[destIndex] = ((ListObject)v.CurrentRegisters[collectionIndex].ObjectReference).Elements[globalRegisters[indexRegIndex].IntValue];
                        else
                            return (i, v) => v.CurrentRegisters[destIndex] = ((ListObject)v.CurrentRegisters[collectionIndex].ObjectReference).Elements[v.CurrentRegisters[indexRegIndex].IntValue];
                    }
                }
            }

            if (collection.ObjectReference is StringObject)
            {
                VariableValue? collectionVar = collectionOperand as VariableValue;
                int collectionIndex = collectionVar?.RegisterIndex ?? ((TempValue)collectionOperand).RegisterIndex;

                if (indexOperand is NumberValue num)
                {
                    int constIndex = (int)num.Value;
                    if (collectionVar?.IsGlobal ?? false)
                        return (i, v) =>
                        {
                            vm.TryReturnRegisterReferenceToPool(destIndex);
                            v.CurrentRegisters[destIndex] = v.ResolveCharObjectRuntimeValue(((StringObject)globalRegisters[collectionIndex].ObjectReference).Value[constIndex]);
                        };
                    else
                        return (i, v) =>
                        {
                            vm.TryReturnRegisterReferenceToPool(destIndex);
                            v.CurrentRegisters[destIndex] = v.ResolveCharObjectRuntimeValue(((StringObject)v.CurrentRegisters[collectionIndex].ObjectReference).Value[constIndex]);
                        };
                }

                if (indexOperand is VariableValue or TempValue)
                {
                    VariableValue? indexVar = indexOperand as VariableValue;
                    int indexRegIndex = indexVar?.RegisterIndex ?? ((TempValue)indexOperand).RegisterIndex;

                    if (collectionVar?.IsGlobal ?? false)
                    {
                        if (indexVar?.IsGlobal ?? false)
                            return (i, v) =>
                            {
                                vm.TryReturnRegisterReferenceToPool(destIndex);
                                v.CurrentRegisters[destIndex] = v.ResolveCharObjectRuntimeValue(((StringObject)globalRegisters[collectionIndex].ObjectReference).Value[globalRegisters[indexRegIndex].IntValue]);
                            };
                        else
                            return (i, v) =>
                            {
                                vm.TryReturnRegisterReferenceToPool(destIndex);
                                v.CurrentRegisters[destIndex] = v.ResolveCharObjectRuntimeValue(((StringObject)globalRegisters[collectionIndex].ObjectReference).Value[v.CurrentRegisters[indexRegIndex].IntValue]);
                            };
                    }
                    else
                    {
                        if (indexVar?.IsGlobal ?? false)
                            return (i, v) =>
                            {
                                vm.TryReturnRegisterReferenceToPool(destIndex);
                                v.CurrentRegisters[destIndex] = v.ResolveCharObjectRuntimeValue(((StringObject)v.CurrentRegisters[collectionIndex].ObjectReference).Value[globalRegisters[indexRegIndex].IntValue]);
                            };
                        else
                            return (i, v) =>
                            {
                                vm.TryReturnRegisterReferenceToPool(destIndex);
                                v.CurrentRegisters[destIndex] = v.ResolveCharObjectRuntimeValue(((StringObject)v.CurrentRegisters[collectionIndex].ObjectReference).Value[v.CurrentRegisters[indexRegIndex].IntValue]);
                            };
                    }
                }
            }

            return null;
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedAssignHandler(InstructionLine insn, FluenceVirtualMachine vm)
        {
            if (AttemptToModifyAReadonlyVariable(insn, vm, out string name))
            {
                vm.CreateAndThrowRuntimeException($"Runtime Error: Cannot assign to the readonly solid variable '{((VariableValue)insn.Lhs).Name}'.");
                return null;
            }

            Value dest = insn.Lhs;
            Value source = insn.Rhs;
            RuntimeValue[] globalRegisters = vm.GlobalRegisters;

            if (dest is not VariableValue and not TempValue)
            {
                return null;
            }

            VariableValue? destVar = dest as VariableValue;
            int destIndex = destVar?.RegisterIndex ?? ((TempValue)dest).RegisterIndex;
            bool destIsGlobal = destVar?.IsGlobal ?? false;

            if (source is VariableValue sourceVar || source is TempValue)
            {
                int sourceIndex = (source as VariableValue)?.RegisterIndex ?? ((TempValue)source).RegisterIndex;
                bool sourceIsGlobal = (source as VariableValue)?.IsGlobal ?? false;

                if (!destIsGlobal && !sourceIsGlobal)
                    return (i, v) => v.CurrentRegisters[destIndex] = v.CurrentRegisters[sourceIndex];

                if (destIsGlobal && sourceIsGlobal)
                    return (i, v) => globalRegisters[destIndex] = globalRegisters[sourceIndex];

                if (!destIsGlobal && sourceIsGlobal)
                    return (i, v) => v.CurrentRegisters[destIndex] = globalRegisters[sourceIndex];

                if (destIsGlobal && !sourceIsGlobal)
                    return (i, v) => globalRegisters[destIndex] = v.CurrentRegisters[sourceIndex];
            }

            if (IsAConstantValue(source))
            {
                RuntimeValue constValue = vm.GetRuntimeValue(source, insn);

                if (!destIsGlobal)
                    return (i, v) => v.CurrentRegisters[destIndex] = constValue;

                if (destIsGlobal)
                    return (i, v) => globalRegisters[destIndex] = constValue;
            }

            return null;
        }

        internal static SpecializedOpcodeHandler CreateSpecializedIncrementDecrementHandler(InstructionLine insn, FluenceVirtualMachine vm, bool increment)
        {
            VariableValue var = (VariableValue)insn.Lhs;
            int regIndex = var.RegisterIndex;
            int amount = increment ? 1 : -1;

            if (var.IsReadOnly)
            {
                vm.CreateAndThrowRuntimeException($"Runtime Error: Cannot increment the readonly solid variable '{var.Name}'.");
                return null;
            }

            if (var.IsGlobal)
            {
                RuntimeValue[] globalRegisters = vm.GlobalRegisters;
                return (Instruction, vm) =>
                {
                    globalRegisters[regIndex] = new RuntimeValue(globalRegisters[regIndex].IntValue + amount);
                };
            }

            return (Instruction, vm) =>
            {
                vm.CurrentRegisters[regIndex] = new RuntimeValue(vm.CurrentRegisters[regIndex].IntValue + amount);
            };
        }

        internal static SpecializedOpcodeHandler? CreateSpecializedAssignTwoHandler(InstructionLine insn, FluenceVirtualMachine vm)
        {
            if (AttemptToModifyAReadonlyVariable(insn, vm, out string name))
            {
                vm.CreateAndThrowRuntimeException($"Runtime Error: Cannot assign to the readonly solid variable '{((VariableValue)insn.Lhs).Name}'.");
                return null;
            }

            Value dest1 = insn.Lhs;
            Value source1 = insn.Rhs;
            Value dest2 = insn.Rhs2;
            Value source3 = insn.Rhs3;
            RuntimeValue[] globalRegisters = vm.GlobalRegisters;

            if (dest1 is VariableValue v1 && v1.IsReadOnly)
            {
                vm.MarkWritableCacheAsReadonly(v1);
            }

            if (dest2 is VariableValue v2 && v2.IsReadOnly)
            {
                vm.MarkWritableCacheAsReadonly(v2);
            }

            VariableValue? dest1Var = dest1 as VariableValue;
            int dest1Index = dest1Var?.RegisterIndex ?? ((TempValue)dest1).RegisterIndex;
            bool dest1IsGlobal = dest1Var?.IsGlobal ?? false;

            VariableValue? source1Var = source1 as VariableValue;
            int? source1Index = source1Var?.RegisterIndex ?? (source1 as TempValue)?.RegisterIndex;
            bool source1IsGlobal = source1Var?.IsGlobal ?? false;
            RuntimeValue? source1Const = IsAConstantValue(source1) ? vm.GetRuntimeValue(source1, insn) : null;

            VariableValue? dest2Var = dest2 as VariableValue;
            int dest2Index = dest2Var?.RegisterIndex ?? ((TempValue)dest2).RegisterIndex;
            bool dest2IsGlobal = dest2Var?.IsGlobal ?? false;

            VariableValue? source2Var = source3 as VariableValue;
            int? source2Index = source2Var?.RegisterIndex ?? (source3 as TempValue)?.RegisterIndex;
            bool source2IsGlobal = source2Var?.IsGlobal ?? false;
            RuntimeValue? source2Const = IsAConstantValue(source3) ? vm.GetRuntimeValue(source3, insn) : null;

            if (dest1IsGlobal)
            {
                if (dest2IsGlobal)
                {
                    if (source1Const.HasValue && source2Const.HasValue)
                    {
                        return (i, v) =>
                        {
                            globalRegisters[dest1Index] = source1Const.Value;
                            globalRegisters[dest2Index] = source2Const.Value;
                        };
                    }
                    if (source1Const.HasValue && source2Index.HasValue)
                    {
                        if (source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = source1Const.Value;
                                globalRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = source1Const.Value;
                                globalRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Const.HasValue)
                    {
                        if (source1IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Index.HasValue)
                    {
                        if (source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        if (source1IsGlobal && !source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                        if (!source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                }
                else
                {
                    if (source1Const.HasValue && source2Const.HasValue)
                    {
                        return (i, v) =>
                        {
                            globalRegisters[dest1Index] = source1Const.Value;
                            v.CurrentRegisters[dest2Index] = source2Const.Value;
                        };
                    }
                    if (source1Const.HasValue && source2Index.HasValue)
                    {
                        if (source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = source1Const.Value;
                                v.CurrentRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = source1Const.Value;
                                v.CurrentRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Const.HasValue)
                    {
                        if (source1IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Index.HasValue)
                    {
                        if (source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        if (source1IsGlobal && !source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                        if (!source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                globalRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                }
            }
            else
            {
                if (dest2IsGlobal)
                {
                    if (source1Const.HasValue && source2Const.HasValue)
                    {
                        return (i, v) =>
                        {
                            v.CurrentRegisters[dest1Index] = source1Const.Value;
                            globalRegisters[dest2Index] = source2Const.Value;
                        };
                    }
                    if (source1Const.HasValue && source2Index.HasValue)
                    {
                        if (source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = source1Const.Value;
                                globalRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = source1Const.Value;
                                globalRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Const.HasValue)
                    {
                        if (source1IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Index.HasValue)
                    {
                        if (source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        if (source1IsGlobal && !source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                        if (!source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                globalRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                }
                else
                {
                    if (source1Const.HasValue && source2Const.HasValue)
                    {
                        return (i, v) =>
                        {
                            v.CurrentRegisters[dest1Index] = source1Const.Value;
                            v.CurrentRegisters[dest2Index] = source2Const.Value;
                        };
                    }
                    if (source1Const.HasValue && source2Index.HasValue)
                    {
                        if (source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = source1Const.Value;
                                v.CurrentRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = source1Const.Value;
                                v.CurrentRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Const.HasValue)
                    {
                        if (source1IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = source2Const.Value;
                            };
                        }
                    }
                    if (source1Index.HasValue && source2Index.HasValue)
                    {
                        if (source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        if (source1IsGlobal && !source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = globalRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                        if (!source1IsGlobal && source2IsGlobal)
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = globalRegisters[source2Index.Value];
                            };
                        }
                        else
                        {
                            return (i, v) =>
                            {
                                v.CurrentRegisters[dest1Index] = v.CurrentRegisters[source1Index.Value];
                                v.CurrentRegisters[dest2Index] = v.CurrentRegisters[source2Index.Value];
                            };
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsAConstantValue(Value val) => val is
            NumberValue or
            StringValue or
            CharValue or
            BooleanValue or
            NilValue;

        internal static SpecializedOpcodeHandler? CreateSpecializedCallFunctionHandler(FluenceVirtualMachine vm, InstructionLine insn, FunctionObject func)
        {
            if (func.BluePrint == null) return null;

            FunctionSymbol functionBlueprint = func.BluePrint;
            TempValue destinationRegister = (TempValue)insn.Lhs;
            int argCount = functionBlueprint.Arguments.Count;
            int destIndex = destinationRegister.RegisterIndex;

            if (func.IsIntrinsic)
            {
                IntrinsicMethod? intrinsicBody = functionBlueprint.IntrinsicBody;

                return (instruction, vm) =>
                {
                    RuntimeValue resultValue = intrinsicBody!(vm, argCount);
                    vm.CurrentRegisters[destIndex] = resultValue;
                };
            }

            int refMask = functionBlueprint.RefMask;
            int baseArgRegisterIndex = func.BelongsToAStruct ? 1 : 0;

            FunctionObject function = vm.CreateFunctionObject(functionBlueprint);

            if (refMask == 0)
            {
                // By returning a specific delegate for each count, we eliminate the 'for' loop and 'isRef' checks entirely.
                switch (argCount)
                {
                    case 0:
                        return (instruction, vmContext) =>
                        {
                            CallFrame newFrame = vmContext.GetCallframe();
                            newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);
                            vmContext.PrepareFunctionCall(newFrame, function);
                        };

                    case 1:
                        int p0 = baseArgRegisterIndex;
                        return (instruction, vmContext) =>
                        {
                            CallFrame newFrame = vmContext.GetCallframe();
                            newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);
                            newFrame.Registers[p0] = vmContext.PopStack();
                            vmContext.PrepareFunctionCall(newFrame, function);
                        };

                    case 2:
                        int p1_2 = baseArgRegisterIndex + 1;
                        int p0_2 = baseArgRegisterIndex;
                        return (instruction, vmContext) =>
                        {
                            CallFrame newFrame = vmContext.GetCallframe();
                            newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);
                            newFrame.Registers[p1_2] = vmContext.PopStack();
                            newFrame.Registers[p0_2] = vmContext.PopStack();
                            vmContext.PrepareFunctionCall(newFrame, function);
                        };

                    case 3:
                        int p2_3 = baseArgRegisterIndex + 2;
                        int p1_3 = baseArgRegisterIndex + 1;
                        int p0_3 = baseArgRegisterIndex;
                        return (instruction, vmContext) =>
                        {
                            CallFrame newFrame = vmContext.GetCallframe();
                            newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);
                            newFrame.Registers[p2_3] = vmContext.PopStack();
                            newFrame.Registers[p1_3] = vmContext.PopStack();
                            newFrame.Registers[p0_3] = vmContext.PopStack();
                            vmContext.PrepareFunctionCall(newFrame, function);
                        };

                    case 4:
                        int p3_4 = baseArgRegisterIndex + 3;
                        int p2_4 = baseArgRegisterIndex + 2;
                        int p1_4 = baseArgRegisterIndex + 1;
                        int p0_4 = baseArgRegisterIndex;
                        return (instruction, vmContext) =>
                        {
                            CallFrame newFrame = vmContext.GetCallframe();
                            newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);
                            newFrame.Registers[p3_4] = vmContext.PopStack();
                            newFrame.Registers[p2_4] = vmContext.PopStack();
                            newFrame.Registers[p1_4] = vmContext.PopStack();
                            newFrame.Registers[p0_4] = vmContext.PopStack();
                            vmContext.PrepareFunctionCall(newFrame, function);
                        };

                    default:
                        // Fallback for 5+ arguments.
                        return (instruction, vmContext) =>
                        {
                            CallFrame newFrame = vmContext.GetCallframe();
                            newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);

                            for (int i = argCount - 1; i >= 0; i--)
                            {
                                newFrame.Registers[baseArgRegisterIndex + i] = vmContext.PopStack();
                            }

                            vmContext.PrepareFunctionCall(newFrame, function);
                        };
                }
            }

            // Function with ref params.
            return (instruction, vmContext) =>
            {
                CallFrame newFrame = vmContext.GetCallframe();
                newFrame.Initialize(vmContext, function, vmContext.CurrentInstructionPointer, destinationRegister);

                for (int i = argCount - 1; i >= 0; i--)
                {
                    int paramIndex = baseArgRegisterIndex + i;
                    RuntimeValue argValue = vmContext.PopStack();

                    if ((refMask & (1 << i)) != 0)
                    {
                        if (argValue.ObjectReference is ReferenceValue reference)
                        {
                            newFrame.RefParameterMap[paramIndex] = reference.Reference.RegisterIndex;
                            newFrame.Registers[paramIndex] = vmContext.GetRuntimeValue(reference.Reference, instruction);
                        }
                        else
                        {
                            vmContext.SignalError($"Internal VM Error: Argument '{function.Arguments[i]}' in function: \"{function.ToCodeLikeString()}\" must be passed by reference ('ref').");
                            return;
                        }
                    }
                    else
                    {
                        newFrame.Registers[paramIndex] = argValue;
                    }
                }

                vmContext.PrepareFunctionCall(newFrame, function);
            };
        }
    }
}