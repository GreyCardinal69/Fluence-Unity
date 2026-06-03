using System.Runtime.CompilerServices;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceByteCode.InstructionLine;
using static Fluence.Unity.FluenceParser;

namespace Fluence.Unity
{
    /// <summary>
    /// A class meant for the optimization of bytecode, done incrementally by the parser during parsing.
    /// </summary>
    internal static class FluenceOptimizer
    {
        private static readonly Dictionary<int, RegisterInfo> _registerInfoMap = new();
        private static readonly Dictionary<int, Value> _constantsMap = new();
        private static readonly List<int> _instructionsToRemove = new();
        private static readonly HashSet<int> _uniqueSymbols = new HashSet<int>();
        private static readonly Dictionary<int, (int Count, Value? ConstVal, int DefIndex)> _varStatsMap = new();

        /// <summary>
        /// A private struct to hold information about a temporary register's assignments.
        /// </summary>
        private struct RegisterInfo
        {
            public int AssignmentCount;
            public int AssignmentIndex;
            public Value? ConstantValue;
        }

        /// <summary>
        /// Incrementally optimizes a segment of the bytecode list..
        /// It scans for optimizable patterns from a given start index, performs fusions, and then compacts the list while realigning all addresses.
        /// </summary>
        /// <param name="bytecode">The list of bytecode instructions to be modified. It is passed by reference and will be modified in-place.</param>
        /// <param name="parseState">The current state of the parser, containing symbol tables which are required for patching function and method start addresses.</param>
        /// <param name="startIndex">The index in the bytecode list from which to start scanning for optimizations.</param>
        internal static void OptimizeChunk(List<InstructionLine> bytecode, ParseState parseState, int startIndex, VirtualMachineConfiguration config)
        {
            bool byteCodeChanged = false;
            bool constantFoldingDidWork = false;

            FuseGotoConditionals(bytecode, startIndex, ref byteCodeChanged);
            ApplyAggressiveConstantPropagation(bytecode, startIndex, ref byteCodeChanged, ref constantFoldingDidWork);
            RemoveConstTempRegisters(bytecode, startIndex, ref byteCodeChanged, ref constantFoldingDidWork);
            FuseCompoundAssignments(bytecode, startIndex, ref byteCodeChanged);
            FuseSimpleAssignments(bytecode, startIndex, ref byteCodeChanged);
            FusePushParams(bytecode, startIndex, ref byteCodeChanged);
            ConvertToIncrementsDecrements(bytecode, startIndex);
            ApplyStrengthReduction(bytecode, startIndex, ref byteCodeChanged);
            FuseComparisonBranches(bytecode, startIndex, ref byteCodeChanged);

            if (byteCodeChanged)
            {
                CompactAndRealignFromBottomUp(bytecode, parseState);
                _uniqueSymbols.Clear();
            }

            if (constantFoldingDidWork)
            {
                _varStatsMap.Clear();
                _registerInfoMap.Clear();
                _constantsMap.Clear();
                _instructionsToRemove.Clear();
            }
        }

        /// <summary>
        /// Scans for modulo or division by 2 and replaces them with bitwise operations (Strength Reduction).
        /// Modulo X, 2 => BitwiseAnd X, 1
        /// Divide X, 2 => BitShiftRight X, 1
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void ApplyStrengthReduction(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged)
        {
            for (int i = startIndex; i < bytecode.Count; i++)
            {
                InstructionLine line = bytecode[i];

                if (line == null) continue;

                if (line.Instruction == InstructionCode.Divide)
                {
                    if (line.Rhs2 is NumberValue num && (int)num.Value == 2)
                    {
                        line.Instruction = InstructionCode.BitwiseRShift;
                        line.Rhs2 = NumberValue.One;
                        byteCodeChanged = true;
                    }
                }
                else if (line.Instruction == InstructionCode.Modulo)
                {
                    if (line.Rhs2 is NumberValue num && (int)num.Value == 2)
                    {
                        line.Instruction = InstructionCode.BitwiseAnd;
                        line.Rhs2 = NumberValue.One;
                        byteCodeChanged = true;
                    }
                }
            }
        }

        /// <summary>
        /// Scans for an arithmetic operation followed by an assignment to a variable, fusing them into a single compound assignment instruction.
        /// The second, now redundant, instruction is replaced with a null placeholder for later removal.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void FuseCompoundAssignments(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged)
        {
            int i = startIndex;
            while (i < bytecode.Count - 1)
            {
                InstructionLine line1 = bytecode[i];
                InstructionLine line2 = bytecode[i + 1];

                if (line1 == null || line2 == null)
                {
                    i++;
                    continue;
                }

                InstructionCode opCode = GetFusedOpcode(line1.Instruction);

                if (opCode != InstructionCode.Unknown &&
                    line2.Instruction == InstructionCode.Assign &&
                    line1.Lhs is TempValue l1Lhs &&
                    line2.Rhs is TempValue l2Rhs &&
                    line2.Lhs is VariableValue &&
                    l1Lhs.Hash == l2Rhs.Hash)
                {
                    line1.Instruction = opCode;
                    line1.Lhs = line2.Lhs;
                    bytecode[i + 1] = null!;
                    byteCodeChanged = true;
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
        }

        /// <summary>
        /// Fuses two PushParam instructions into one.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void FusePushParams(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged)
        {
            int i = startIndex;
            while (i < bytecode.Count - 1)
            {
                InstructionLine insn1 = bytecode[i];

                if (insn1 == null || insn1.Instruction != InstructionCode.PushParam)
                {
                    i++;
                    continue;
                }

                if (i + 3 < bytecode.Count)
                {
                    InstructionLine insn2 = bytecode[i + 1];
                    InstructionLine insn3 = bytecode[i + 2];
                    InstructionLine insn4 = bytecode[i + 3];

                    if (insn2 != null && insn3 != null && insn4 != null &&
                        insn2.Instruction == InstructionCode.PushParam &&
                        insn3.Instruction == InstructionCode.PushParam &&
                        insn4.Instruction == InstructionCode.PushParam)
                    {
                        insn1.Instruction = InstructionCode.PushFourParams;
                        insn1.Rhs = insn2.Lhs;
                        insn1.Rhs2 = insn3.Lhs;
                        insn1.Rhs3 = insn4.Lhs;

                        bytecode[i + 1] = null!;
                        bytecode[i + 2] = null!;
                        bytecode[i + 3] = null!;

                        byteCodeChanged = true;
                        i += 4;
                        continue;
                    }
                }

                if (i + 2 < bytecode.Count)
                {
                    InstructionLine insn2 = bytecode[i + 1];
                    InstructionLine insn3 = bytecode[i + 2];

                    if (insn2 != null && insn3 != null &&
                        insn2.Instruction == InstructionCode.PushParam &&
                        insn3.Instruction == InstructionCode.PushParam)
                    {
                        insn1.Instruction = InstructionCode.PushThreeParams;
                        insn1.Rhs = insn2.Lhs;
                        insn1.Rhs2 = insn3.Lhs;

                        bytecode[i + 1] = null!;
                        bytecode[i + 2] = null!;

                        byteCodeChanged = true;
                        i += 3;
                        continue;
                    }
                }

                if (i + 1 < bytecode.Count)
                {
                    InstructionLine insn2_two = bytecode[i + 1];
                    if (insn2_two != null && insn2_two.Instruction == InstructionCode.PushParam)
                    {
                        insn1.Instruction = InstructionCode.PushTwoParams;
                        insn1.Rhs = insn2_two.Lhs;

                        bytecode[i + 1] = null!;

                        byteCodeChanged = true;
                        i += 2;
                        continue;
                    }
                }

                i++;
            }
        }

        /// <summary>
        /// Converts an Add or a Subtract instruction that simply increments or decrements a variable into a slightly more faster Increment
        /// or Decrement instruction.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void ConvertToIncrementsDecrements(List<InstructionLine> bytecode, int startIndex)
        {
            for (int i = startIndex; i < bytecode.Count - 1; i++)
            {
                InstructionLine line1 = bytecode[i];
                if (line1 == null) continue;

                // Pattern Match:
                // Add/Sub      Var     Var     1
                // =>
                // ++/--        Var     1
                if ((line1.Instruction == InstructionCode.Add || line1.Instruction == InstructionCode.Subtract) &&
                     line1.Lhs is VariableValue var &&
                     line1.Rhs is VariableValue var2 &&
                     var.Hash == var2.Hash &&
                     line1.Rhs2 is NumberValue num &&
                     num.Type == NumberValue.NumberType.Integer &&
                     (int)num.Value == 1)
                {
                    InstructionCode instruction = line1.Instruction == InstructionCode.Add ? InstructionCode.Increment : InstructionCode.Decrement;

                    // This optimization does not change bytecode instructions to a considerable degree, no need to parch addresses.
                    bytecode[i].Instruction = instruction;
                    bytecode[i].Rhs = null!;
                    bytecode[i].Rhs2 = null!;
                }
            }
        }

        /// <summary>
        /// Combines two simple assignment operations into one.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void FuseSimpleAssignments(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged)
        {
            for (int i = startIndex; i < bytecode.Count - 1; i++)
            {
                InstructionLine line1 = bytecode[i];
                InstructionLine line2 = bytecode[i + 1];
                if (line1 == null || line2 == null) continue;

                if (line1.Instruction == InstructionCode.Assign && line2.Instruction == InstructionCode.Assign && line1.Lhs.Hash != line2.Rhs.Hash)
                {
                    byteCodeChanged = true;
                    bytecode[i].Instruction = InstructionCode.AssignTwo;
                    bytecode[i].Rhs2 = line2.Lhs;
                    bytecode[i].Rhs3 = line2.Rhs;
                    bytecode[i + 1] = null!;
                    i++;
                }
            }
        }

        /// <summary>
        /// Performs aggressive constant propagation for local variables.
        /// Analyzes variable usage. Identifies variables that are assigned a constant value exactly once 
        /// and are never modified (reassigned or mutated) within the current scope.
        /// Replaces all usages of these constants with the immediate value. 
        /// Crucially, it performs Dead Code Elimination by removing the original assignment instruction, 
        /// as the value is now propagated.
        /// Handles nested lambdas by tracking depth; variables inside lambdas are ignored to prevent scope collision issues.
        /// </summary>
        /// <param name="bytecode">The reference to the bytecode list.</param>
        /// <param name="startIndex">The start index for the current optimization chunk.</param>
        /// <param name="byteCodeChanged">Ref bool indicating if any changes were made.</param>
        /// <param name="constantFoldingDidWork">Ref bool indicating if this specific pass performed work (used for cleanup).</param>
        private static void ApplyAggressiveConstantPropagation(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged, ref bool constantFoldingDidWork)
        {
            int lambdaDepth = 0;

            for (int i = startIndex; i < bytecode.Count; i++)
            {
                InstructionLine insn = bytecode[i];
                if (insn == null) continue;

                if (insn.Instruction == InstructionCode.SectionLambdaStart)
                {
                    lambdaDepth++;
                    continue;
                }
                if (insn.Instruction == InstructionCode.SectionLambdaEnd)
                {
                    lambdaDepth--;
                    continue;
                }

                if (lambdaDepth > 0) continue;

                if (insn.Lhs is VariableValue varLhs && !varLhs.IsGlobal)
                {
                    if (!_varStatsMap.TryGetValue(varLhs.Hash, out (int Count, Value ConstVal, int DefIndex) stats))
                    {
                        stats = (0, null, 0);
                    }

                    if (insn.Instruction == InstructionCode.Assign)
                    {
                        stats.Count++;
                        if (IsAConstantValue(insn.Rhs))
                        {
                            stats.ConstVal = insn.Rhs;
                            stats.DefIndex = i;
                        }
                        else
                        {
                            stats.ConstVal = null;
                        }
                    }
                    else
                    {
                        stats.Count = 1000;
                        stats.ConstVal = null;
                    }

                    _varStatsMap[varLhs.Hash] = stats;
                }
            }

            lambdaDepth = 0;

            void TryReplace(ref Value operand, bool byteCodeChanged, bool constantFoldingDidWork)
            {
                if (operand is VariableValue varOp && !varOp.IsGlobal && _varStatsMap.TryGetValue(varOp.Hash, out (int Count, Value ConstVal, int DefIndex) stat))
                {
                    if (stat.Count == 1 && stat.ConstVal != null)
                    {
                        operand = stat.ConstVal;
                        byteCodeChanged = true;
                        constantFoldingDidWork = true;

                        if (bytecode[stat.DefIndex] != null)
                        {
                            bytecode[stat.DefIndex] = null!;
                        }
                    }
                }
            }

            for (int i = startIndex; i < bytecode.Count; i++)
            {
                InstructionLine insn = bytecode[i];
                if (insn == null) continue;

                if (insn.Instruction == InstructionCode.SectionLambdaStart)
                {
                    lambdaDepth++;
                    continue;
                }
                if (insn.Instruction == InstructionCode.SectionLambdaEnd)
                {
                    lambdaDepth--;
                    continue;
                }

                if (lambdaDepth > 0) continue;

                TryReplace(ref insn.Rhs, byteCodeChanged, constantFoldingDidWork);
                TryReplace(ref insn.Rhs2, byteCodeChanged, constantFoldingDidWork);
                TryReplace(ref insn.Rhs3, byteCodeChanged, constantFoldingDidWork);
            }
        }

        /// <summary>
        /// If the bytecode contains any assignments to Temporary Registers, where the values assigned are const, we can remove those
        /// and place them directly in instructions where that Temporary Register is used, reducing instruction count.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void RemoveConstTempRegisters(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged, ref bool constantFoldingDidWork)
        {
            for (int i = startIndex; i < bytecode.Count; i++)
            {
                InstructionLine insn = bytecode[i];

                if (insn == null) continue;

                if (insn.Rhs is TempValue temp)
                {
                    bool exists = _registerInfoMap.TryGetValue(temp.Hash, out RegisterInfo info);

                    if (!exists)
                    {
                        info = new RegisterInfo
                        {
                            AssignmentCount = 1,
                            AssignmentIndex = i,
                            ConstantValue = IsAConstantValue(insn.Rhs) ? insn.Rhs : null
                        };
                    }
                    else
                    {
                        info.AssignmentCount++;
                        info.ConstantValue = null;
                    }

                    _registerInfoMap[temp.Hash] = info;
                }
            }

            foreach (KeyValuePair<int, RegisterInfo> kvp in _registerInfoMap)
            {
                if (kvp.Value.AssignmentCount == 1 && kvp.Value.ConstantValue != null)
                {
                    _constantsMap.Add(kvp.Key, kvp.Value.ConstantValue);
                    _instructionsToRemove.Add(kvp.Value.AssignmentIndex);
                }
            }

            if (_constantsMap.Count == 0)
            {
                return;
            }

            for (int i = startIndex; i < bytecode.Count; i++)
            {
                InstructionLine insn = bytecode[i];
                if (insn == null) continue;

                bool changed = false;

                if (insn.Rhs is TempValue tempRhs && _constantsMap.TryGetValue(tempRhs.Hash, out Value constValRhs))
                {
                    insn.Rhs = constValRhs;
                    changed = true;
                }
                if (insn.Rhs2 is TempValue tempRhs2 && _constantsMap.TryGetValue(tempRhs2.Hash, out Value constValRhs2))
                {
                    insn.Rhs2 = constValRhs2;
                    changed = true;
                }
                if (insn.Rhs3 is TempValue tempRhs3 && _constantsMap.TryGetValue(tempRhs3.Hash, out Value constValRhs3))
                {
                    insn.Rhs3 = constValRhs3;
                    changed = true;
                }

                if (changed)
                {
                    constantFoldingDidWork = true;
                    byteCodeChanged = true;
                }
            }

            foreach (int index in _instructionsToRemove)
            {
                bytecode[index] = null!;
            }
        }

        /// <summary>
        /// Scans for a comparison operation followed by a conditional jump that uses its result. Fuses them into a single, more efficient branch instruction.
        /// The second, now redundant, instruction is replaced with a null placeholder for later removal.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        private static void FuseGotoConditionals(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged)
        {
            for (int i = startIndex; i < bytecode.Count - 1; i++)
            {
                InstructionLine line1 = bytecode[i];
                InstructionLine line2 = bytecode[i + 1];
                if (line1 == null || line2 == null) continue;

                InstructionCode op = GetFusedGotoOpCode(line1.Instruction, line2.Instruction);

                // Pattern Match:
                // Not/Equal             TempN    A          B
                // GotoIfTrue/False      JMP      TEMPN      .
                // =>
                // BranchIfEqual/Not     JMP      A          B   
                if (op != InstructionCode.Unknown &&
                    line1.Lhs is TempValue cResult &&
                    line2.Rhs is TempValue jCond &&
                    cResult.Hash == jCond.Hash)
                {
                    byteCodeChanged = true;
                    bytecode[i].Instruction = op;
                    bytecode[i].Lhs = line2.Lhs;
                    bytecode[i].Rhs = line1.Rhs;
                    bytecode[i].Rhs2 = line1.Rhs2;
                    bytecode[i + 1] = null!;
                    i++;
                }
            }
        }

        /// <summary>
        /// Scans for a comparison operation (<, <=, >, >=) followed by a conditional jump
        /// that uses its result, and fuses them into a single, efficient branch instruction.
        /// </summary>
        /// <param name="bytecode">The bytecode list to modify.</param>
        /// <param name="startIndex">The index from which to begin scanning.</param>
        /// <param name="byteCodeChanged">Flag to indicate if the bytecode was modified.</param>
        private static void FuseComparisonBranches(List<InstructionLine> bytecode, int startIndex, ref bool byteCodeChanged)
        {
            int i = startIndex;
            while (i < bytecode.Count - 1)
            {
                InstructionLine line1 = bytecode[i];
                InstructionLine line2 = bytecode[i + 1];

                if (line1 == null || line2 == null)
                {
                    i++;
                    continue;
                }

                InstructionCode fusedOp = GetFusedBranchOpCode(line1.Instruction, line2.Instruction);

                if (fusedOp != InstructionCode.Unknown &&
                    line1.Lhs is TempValue comparisonResult &&
                    line2.Rhs is TempValue jumpCondition &&
                    comparisonResult.Hash == jumpCondition.Hash)
                {
                    line1.Instruction = fusedOp;
                    line1.Lhs = line2.Lhs;
                    bytecode[i + 1] = null!;
                    byteCodeChanged = true;
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
        }

        /// <summary>
        /// Gets the corresponding branch instruction for a given comparison and conditional goto pair.
        /// </summary>
        /// <returns>The fused instruction code, or <see cref="InstructionCode.Unknown"/> if no pattern matches.</returns>
        private static InstructionCode GetFusedBranchOpCode(InstructionCode comparisonOp, InstructionCode jumpOp) => (comparisonOp, jumpOp) switch
        {
            (InstructionCode.GreaterThan, InstructionCode.GotoIfTrue) => InstructionCode.BranchIfGreaterThan,
            (InstructionCode.GreaterThan, InstructionCode.GotoIfFalse) => InstructionCode.BranchIfLessOrEqual,

            (InstructionCode.LessThan, InstructionCode.GotoIfTrue) => InstructionCode.BranchIfLessThan,
            (InstructionCode.LessThan, InstructionCode.GotoIfFalse) => InstructionCode.BranchIfGreaterOrEqual,

            (InstructionCode.GreaterEqual, InstructionCode.GotoIfTrue) => InstructionCode.BranchIfGreaterOrEqual,
            (InstructionCode.GreaterEqual, InstructionCode.GotoIfFalse) => InstructionCode.BranchIfLessThan,

            (InstructionCode.LessEqual, InstructionCode.GotoIfTrue) => InstructionCode.BranchIfLessOrEqual,
            (InstructionCode.LessEqual, InstructionCode.GotoIfFalse) => InstructionCode.BranchIfGreaterThan,

            _ => InstructionCode.Unknown,
        };

        /// <summary>
        /// Checks whether the given <see cref="Value"/> represents a constant value such as strings, chars, nil, bool or numeric.
        /// </summary>
        /// <param name="val">The Value to check.</param>
        /// <returns>True if the <see cref="Value"/> is considered constant.</returns>
        private static bool IsAConstantValue(Value val) => val is
            NumberValue or
            StringValue or
            CharValue or
            BooleanValue or
            NilValue;

        /// <summary>
        /// Gets the corresponding branch instruction for a given comparison and conditional goto pair.
        /// </summary>
        /// <returns>The fused instruction code, or <see cref="InstructionCode.Unknown"/> if no pattern matches.</returns>
        private static InstructionCode GetFusedGotoOpCode(InstructionCode op1, InstructionCode op2) => (op1, op2) switch
        {
            (InstructionCode.Equal, InstructionCode.GotoIfTrue) or (InstructionCode.NotEqual, InstructionCode.GotoIfFalse) => InstructionCode.BranchIfEqual,
            (InstructionCode.Equal, InstructionCode.GotoIfFalse) or (InstructionCode.NotEqual, InstructionCode.GotoIfTrue) => InstructionCode.BranchIfNotEqual,
            _ => InstructionCode.Unknown,
        };

        /// <summary>
        /// Gets the corresponding compound assignment instruction for a given arithmetic operation.
        /// </summary>
        /// <returns>The fused instruction code, or <see cref="InstructionCode.Unknown"/> if no pattern matches.</returns>
        private static InstructionCode GetFusedOpcode(InstructionCode op) => op switch
        {
            InstructionCode.Add => InstructionCode.AddAssign,
            InstructionCode.Subtract => InstructionCode.SubAssign,
            InstructionCode.Multiply => InstructionCode.MulAssign,
            InstructionCode.Divide => InstructionCode.DivAssign,
            InstructionCode.Modulo => InstructionCode.ModAssign,
            _ => InstructionCode.Unknown
        };

        /// <summary>
        /// Checks if the given instruction code is a type of jump.
        /// </summary>
        /// <returns>True if the instruction is a jump, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsJumpInstruction(InstructionCode op) => op is >= InstructionCode.Goto and <= InstructionCode.BranchIfLessOrEqual;

        /// <summary>
        /// Compacts the bytecode list by removing all null placeholders and realigns all absolute addresses.
        /// It iterates from the end of the list to the beginning, which provides a stable and correct approach for in-place removal and patching.
        /// </summary>
        private static void CompactAndRealignFromBottomUp(List<InstructionLine> bytecode, ParseState state)
        {
            for (int i = bytecode.Count - 1; i >= 0; i--)
            {
                if (bytecode[i] == null)
                {
                    bytecode.RemoveAt(i);
                    PatchAllAddressesAfterRemoval(bytecode, state, i);
                }
            }
        }

        /// <summary>
        /// Patches all absolute addresses in the bytecode and symbol tables after a single instruction has been removed.
        /// </summary>
        /// <param name="bytecode">The bytecode list, now one element shorter.</param>
        /// <param name="state">The parse state containing symbols to patch.</param>
        /// <param name="removedIndex">The index of the instruction that was just removed. All addresses greater than this index will be decremented.</param>
        private static void PatchAllAddressesAfterRemoval(List<InstructionLine> bytecode, ParseState state, int removedIndex)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int MapAddr(int oldAddr)
            {
                return oldAddr > removedIndex ? oldAddr - 1 : oldAddr;
            }

            for (int i = 0; i < bytecode.Count; i++)
            {
                InstructionLine insn = bytecode[i];

                if (insn == null) continue;

                if (IsJumpInstruction(insn.Instruction) && insn.Lhs is GoToValue targetAddr)
                {
                    targetAddr.Address = MapAddr(targetAddr.Address);
                    // Goto here, no need to check other cases.
                    continue;
                }
                else if (insn.Lhs is TryCatchValue tryCatch)
                {
                    tryCatch.TryGoToIndex = MapAddr(tryCatch.TryGoToIndex);
                    tryCatch.CatchGoToIndex = MapAddr(tryCatch.CatchGoToIndex);
                    // Try catch, same as goto, no rhs+ components.
                    continue;
                }

                if (insn.Rhs is FunctionValue fvRhs)
                {
                    fvRhs.SetStartAddress(MapAddr(fvRhs.StartAddress));
                }
                else if (insn.Rhs is LambdaValue lambda)
                {
                    lambda.Function.SetStartAddress(MapAddr(lambda.Function.StartAddress));
                }

                if (insn.Rhs2 is FunctionValue fvRhs2)
                {
                    fvRhs2.SetStartAddress(MapAddr(fvRhs2.StartAddress));
                }
            }

            foreach (FluenceScope scope in state.NameSpaces.Values)
            {
                if (scope.IsIntrinsicScope)
                {
                    continue;
                }
                foreach (Symbol symbol in scope.Symbols.Values)
                {
                    if (symbol is FunctionSymbol f)
                    {
                        f.SetStartAddress(MapAddr(f.StartAddress));
                    }
                    else if (symbol is StructSymbol s)
                    {
                        foreach (KeyValuePair<string, FunctionValue> item in s.Constructors)
                        {
                            item.Value.SetStartAddress(MapAddr(item.Value.StartAddress));
                        }
                        foreach (FunctionValue m in s.Functions.Values)
                        {
                            m.SetStartAddress(MapAddr(m.StartAddress));
                        }
                    }
                }
            }
        }
    }
}