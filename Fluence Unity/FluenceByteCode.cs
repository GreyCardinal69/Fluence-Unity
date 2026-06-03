using Fluence.Unity.VirtualMachine;

namespace Fluence.Unity
{
    /// <summary>
    /// Defines the bytecode instruction set for the Fluence Virtual Machine.
    /// Provides structured representation of executable instructions with opcodes and operands.
    /// </summary>
    internal static class FluenceByteCode
    {
        /// <summary>
        /// Represents a single executable instruction in Fluence bytecode.
        /// Each instruction consists of an <see cref="InstructionCode"/> opcode and up to four operands.
        /// </summary>
        internal sealed class InstructionLine
        {
            /// <summary>
            /// Defines all operation codes supported by the Fluence Virtual Machine.
            /// </summary>
            internal enum InstructionCode
            {
                Skip,           // Only used in VM setup strictly.
                Unknown,

                Return,
                Terminate,      // Halt program execution.

                Assign,
                AssignIfNil,    // Assigns value to nil ( initializes ) if a value is not already set ( not initialized ), this is for global variable declarations.

                Add,
                Subtract,
                Multiply,
                Divide,
                Modulo,
                Power,
                Negate,         // Unary negation, '-(x)'.

                Equal,
                NotEqual,
                LessThan,
                GreaterThan,
                LessEqual,
                GreaterEqual,

                And,
                Or,
                Not,            // Logical NOT (!).

                BitwiseAnd,
                BitwiseOr,
                BitwiseXor,
                BitwiseNot,
                BitwiseLShift,
                BitwiseRShift,

                NewIterator,
                IterNext,

                GetType,

                TryBlock,
                CatchBlock,

                // Function & Method Calls.
                PushParam,
                CallFunction,
                CallMethod,

                // Object & Struct Operations.
                NewInstance,
                GetField,
                SetField,

                IsType,
                Throw,

                NotImplemented,

                // List & Collection Operations.
                NewList,
                NewDictionary,
                PushKeyValuePair,
                NewRange,
                PushElement,
                GetElement,
                SetElement,
                GetLength,

                CallStatic,
                GetStatic,
                SetStatic,

                // Type Operations.
                ToString,

                NewLambda,
                LoadAddress,    // Argument passed by reference.

                AddAssign,
                SubAssign,
                MulAssign,
                DivAssign,
                ModAssign,

                Increment,
                Decrement,

                AssignTwo,

                Goto,
                GotoIfTrue,
                GotoIfFalse,
                BranchIfEqual,
                BranchIfNotEqual,
                BranchIfGreaterThan,
                BranchIfGreaterOrEqual,
                BranchIfLessThan,
                BranchIfLessOrEqual,

                PushTwoParams,
                PushThreeParams,
                PushFourParams,

                NewCoroutine,
                Yield,
                Resume,

                //      ==!!==
                // Low-Level/Internal Operations.

                /// <summary> Increments an integer variable, even if it is readonly.</summary>
                IncrementIntUnrestricted,

                /// <summary>Global setup section marker.</summary>
                SectionGlobal,
                SectionLambdaStart,
                SectionLambdaEnd,

                // More performant way to get the total amount of opcodes in the VM ctor, relies on first opcode being 0.
                NumberOfOpcodes,
            }

            /// <summary>The operation code for this instruction.</summary>
            internal InstructionCode Instruction;

            /// <summary>The primary operand, often the destination or target of the operation.</summary>
            internal Value Lhs;

            /// <summary>The first source operand.</summary>
            internal Value Rhs;

            /// <summary>The second source operand.</summary>
            internal Value Rhs2;

            /// <summary>The third source operand, used only in specialized instructions and generated strictly by the optimizer.</summary>
            internal Value Rhs3;

            /// <summary>
            /// Defines the signature for a specialized opcode handler that bypasses
            /// the generic logic for improved performance on subsequent calls.
            /// </summary>
            internal delegate void SpecializedOpcodeHandler(InstructionLine instruction, FluenceVirtualMachine vm);

            /// <summary>
            /// The cached, optimized "fast path" for this instruction.
            /// If this is not null, it is executed by the generic opcode handler.
            /// </summary>
            internal SpecializedOpcodeHandler? SpecializedHandler { get; set; } = null!;

            /// <summary>The approximate line location the instruction points to in the source file.</summary>
            internal int LineInSourceCode { get; private set; }

            /// <summary>The approximate column location the instruction points to in the source file.</summary>
            internal int ColumnInSourceCode { get; private set; }

            /// <summary>
            /// In a multi-file project, this is the index into the project's file path table
            /// that identifies the source file for this instruction.
            /// </summary>
            internal int ProjectFileIndex { get; private set; }

            /// <summary>
            /// Indicates that the following instruction assigns a value to a variable, and that the assignment has passed safety
            /// checks of solid ( readonly ) variables, allowing the virtual machine to skip those expensive checks on the next iteration.
            /// </summary>
            internal bool AssignsVariableSafely;

            internal static readonly InstructionLine LambdaEntrance = new InstructionLine(InstructionCode.SectionLambdaStart, null!);
            internal static readonly InstructionLine LambdaClosure = new InstructionLine(InstructionCode.SectionLambdaEnd, null!);

            /// <summary>
            /// Sets debugging metadata for source code correlation.
            /// </summary>
            /// <param name="column">Source column number.</param>
            /// <param name="line">Source line number.</param>
            /// <param name="fileIndex">Project file index.</param>
            public void SetDebugInfo(int column, int line, int fileIndex)
            {
                ColumnInSourceCode = column;
                LineInSourceCode = line;
                ProjectFileIndex = fileIndex;
            }

            /// <summary>
            /// Creates a new instruction with the specified opcode and operands.
            /// </summary>
            /// <param name="instruction">The operation code.</param>
            /// <param name="lhs">Primary operand (destination/target).</param>
            /// <param name="rhs">First source operand. Can be null for unary operations.</param>
            /// <param name="rhs2">Second source operand. Can be null.</param>
            /// <param name="rhs3">Third source operand (optimizer use only). Can be null.</param>
            internal InstructionLine(
                InstructionCode instruction,
                Value lhs,
                Value rhs = null!,
                Value rhs2 = null!,
                Value rhs3 = null!)
            {
                Instruction = instruction;
                Lhs = lhs;
                Rhs = rhs;
                Rhs2 = rhs2;
                Rhs3 = rhs3;
            }

            /// <inheritdoc />
            public override string ToString()
            {
                string[] parts =
                new string[] {
                    Instruction.ToString().PadRight(25),
                    (Lhs?.ToByteCodeString() ?? "null").PadRight(40),
                    (Rhs?.ToByteCodeString() ?? "null").PadRight(55),
                    (Rhs2?.ToByteCodeString() ?? "null").PadRight(40),
                    (Rhs3?.ToByteCodeString() ?? "null").PadRight(40),
                };

                return string.Join(" ", parts);
            }
        }
    }
}