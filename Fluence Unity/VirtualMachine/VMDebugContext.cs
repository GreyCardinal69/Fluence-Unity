using Fluence.Unity.RuntimeTypes;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceByteCode.InstructionLine;

namespace Fluence.Unity.VirtualMachine
{
    /// <summary>
    /// Captures the state of the virtual machine at the point of the creation of the object.
    /// Provides a human-readable snapshot of local variables by mapping register indices back to their names.
    /// </summary>
    internal sealed class VMDebugContext
    {
        internal int InstructionPointer { get; }
        internal InstructionLine CurrentInstruction { get; }
        internal IReadOnlyDictionary<string, RuntimeValue> CurrentLocals { get; }
        internal IReadOnlyList<RuntimeValue> OperandStackSnapshot { get; }
        internal int CallStackDepth { get; }
        internal string CurrentFunctionName { get; }

        internal VMDebugContext(FluenceVirtualMachine vm, CallFrame currentFrame, List<InstructionLine> bytecode, Stack<RuntimeValue> operandStack, int callStackDepth)
        {
            InstructionPointer = vm.CurrentInstructionPointer > 0 ? vm.CurrentInstructionPointer - 1 : 0;
            CurrentInstruction = bytecode[InstructionPointer];
            OperandStackSnapshot = new List<RuntimeValue>(operandStack);
            CallStackDepth = callStackDepth;
            CurrentFunctionName = currentFrame.Function.Name;

            Dictionary<string, RuntimeValue> locals = new Dictionary<string, RuntimeValue>();
            Dictionary<int, string> indexToNameMap = new Dictionary<int, string>();
            FunctionObject func = currentFrame.Function;

            if (!func.IsIntrinsic)
            {
                int jumpInstructionIndex = func.StartAddress - 1;

                // The instruction at StartAddress - 1 is always a Goto that points to the EndAddress.
                int endAddress = 0;

                if (jumpInstructionIndex >= 0 &&
                    bytecode[jumpInstructionIndex].Instruction == InstructionCode.Goto &&
                    bytecode[jumpInstructionIndex].Lhs is GoToValue jumpTarget)
                {
                    endAddress = jumpTarget.Address;
                }
                else
                {
                    // Fallback.
                    endAddress = bytecode.Count;
                }

                for (int i = func.StartAddress; i < endAddress; i++)
                {
                    InstructionLine insn = bytecode[i];
                    MapIndexToName(insn.Lhs, indexToNameMap);
                    MapIndexToName(insn.Rhs, indexToNameMap);
                    MapIndexToName(insn.Rhs2, indexToNameMap);
                    MapIndexToName(insn.Rhs3, indexToNameMap);
                }
            }

            if (func.BelongsToAStruct)
            {
                indexToNameMap[0] = "self";
            }

            for (int i = 0; i < currentFrame.Registers.Length; i++)
            {
                if (indexToNameMap.TryGetValue(i, out string name))
                {
                    if (!locals.ContainsKey(name))
                    {
                        locals[name] = currentFrame.Registers[i];
                    }
                }
                else
                {
                    locals[$"<unnamed_reg_{i}>"] = currentFrame.Registers[i];
                }
            }

            CurrentLocals = locals;
        }

        /// <summary>
        /// Helper method to populate the index-to-name map from a Value object.
        /// </summary>
        private static void MapIndexToName(Value val, Dictionary<int, string> map)
        {
            if (val is VariableValue var)
            {
                if (!var.IsGlobal)
                {
                    map[var.RegisterIndex] = var.Name;
                }
            }
            else if (val is TempValue temp)
            {
                map[temp.RegisterIndex] = $"__Temp{temp.TempIndex}";
            }
        }
    }
}