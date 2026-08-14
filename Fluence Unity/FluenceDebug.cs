using Fluence.Unity.RuntimeTypes;
using System.Text;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceInterpreter;

namespace Fluence.Unity
{
    /// <summary>
    /// Provides various debug functions.
    /// </summary>
    internal static class FluenceDebug
    {
        /// <summary>
        /// Formats a function's integer start address into a convenient string format. Limited to 1000 as of now.
        /// </summary>
        /// <param name="startAddress">The start address.</param>
        /// <returns>The formatted start address string.</returns>
        internal static string FormatByteCodeAddress(int startAddress)
        {
            if (startAddress < 10) return $"000{startAddress}";
            if (startAddress < 100) return $"00{startAddress}";
            if (startAddress < 1000) return $"0{startAddress}";
            if (startAddress < 10000) return $"{startAddress}";
            return "-1";
        }

        internal static string TruncateLine(string line, int maxLength = 75)
        {
            if (string.IsNullOrEmpty(line) || line.Length <= maxLength)
            {
                return line;
            }
            return string.Concat(line[..(maxLength - 3)], "...");
        }

        /// <summary>
        /// Dumps a list of bytecode instructions to the console in a formatted table.
        /// </summary>
        /// <param name="instructions">The list of instructions to dump.</param>
        internal static void DumpByteCodeInstructions(List<InstructionLine> instructions, TextOutputMethod outMethod)
        {
            outMethod("--- Compiled Bytecode ---\n");
            outMethod("Value types with unique print format:");
            outMethod("VariableValue: Var_{Name}_{Register Index}_{Is Global?}_{Is Readonly?}");
            outMethod("TempValue: {Name}_{Register Index}");
            outMethod("FunctionValue: Func_{Name}_{Arity}_{TotalRegisters}_{Scope}_{StartAddress}\n");
            outMethod(string.Format("{0,-5} {1,-25} {2,-40} {3,-55} {4,-40} {5, -40}", "", "TYPE", "LHS", "RHS", "RHS2", "RHS3"));
            outMethod("");

            if (instructions == null || instructions.Count == 0)
            {
                outMethod("(No instructions generated)");
                return;
            }

            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i] == null) outMethod($"{i:D4}: NULL");
                else outMethod($"{i:D4}: {instructions[i].ToString().Replace("\n", "")}");
            }

            outMethod("\n--- End of Bytecode ---");
        }

        /// <summary>
        /// A helper debug function to print all tokens starting from the first token at the start index up to the token at the end index.
        /// </summary>
        /// <param name="start">The index of the first token.</param>
        /// <param name="end">The index of the last token.</param>
        internal static void DumpTokensFromTo(int start, int end, TextOutputMethod outMethod, FluenceLexer lexer)
        {
            for (int i = start; i < end; i++)
            {
                outMethod(lexer.PeekAheadByN(i).ToString());
            }
        }

#if DEBUG
        internal static void DumpSymbolTables(FluenceParser.ParseState parseState, TextOutputMethod outMethod)
        {
            return;
            StringBuilder sb = new StringBuilder("------------------------------------\n\nGenerated Symbol Hierarchy:\n\n");

            DumpScope(sb, parseState.GlobalScope, "Global Scope", 0);

            // If there are any namespaces, dump them as separate top-level scopes.
            if (parseState.NameSpaces.Count != 0)
            {
                sb.AppendLine();
                foreach (KeyValuePair<string, FluenceScope> ns in parseState.NameSpacesDebug)
                {
                    DumpScope(sb, ns.Value, $"Namespace: {ns.Key}", 0);
                    outMethod("\n");
                }
            }

            sb.AppendLine("------------------------------------");
            outMethod(sb.ToString());
        }
#endif

        /// <summary>
        /// A recursive helper to dump the contents of a single scope and its children.
        /// </summary>
        /// <param name="sb">The StringBuilder to append to.</param>
        /// <param name="scope">The scope to dump.</param>
        /// <param name="scopeName">The display name for this scope.</param>
        /// <param name="indentationLevel">The current level of indentation.</param>
        private static void DumpScope(StringBuilder sb, FluenceScope scope, string scopeName, int indentationLevel)
        {
            string indent = new string(' ', indentationLevel * 4);

            sb.Append(indent).Append(scopeName).AppendLine(" {");

            if (scope.Symbols.Count == 0)
            {
                sb.Append(indent).AppendLine("    (empty)");
            }
            else
            {
                // Dump all symbols within the current scope.
                foreach (KeyValuePair<int, Symbol> item in scope.Symbols)
                {
                    DumpSymbol(sb, item.Value.Name, item.Value, indentationLevel + 1);
                }
            }

            sb.Append(indent).AppendLine("}").AppendLine();
        }

        /// <summary>
        /// Helper to dump a single symbol's details with proper indentation.
        /// </summary>
        private static void DumpSymbol(StringBuilder sb, string symbolName, Symbol symbol, int indentationLevel)
        {
            string indent = new string(' ', indentationLevel * 4);
            string innerIndent = new string(' ', (indentationLevel + 1) * 4);

            switch (symbol)
            {
                case EnumSymbol enumSymbol:
                    sb.Append(indent).Append($"Symbol: {symbolName}, type Enum {{").AppendLine();
                    foreach (KeyValuePair<string, EnumValue> member in enumSymbol.Members)
                    {
                        sb.Append(innerIndent).Append(member.Value.MemberName).Append(", ").Append(member.Value.Value).AppendLine();
                    }
                    sb.Append(indent).AppendLine("}");
                    break;

                case FunctionSymbol functionSymbol:
                    string scope = functionSymbol.DefiningScope == null || functionSymbol.Arguments == null ? $"None {(functionSymbol.IsIntrinsic ? "(Intrinsic)" : "Global?")}" : functionSymbol.DefiningScope.Name;

                    List<string> argList = new List<string>();
                    if (functionSymbol.Arguments != null)
                    {
                        for (int i = 0; i < functionSymbol.Arguments.Count; i++)
                        {
                            bool isRef = (functionSymbol.RefMask & (1 << i)) != 0;
                            string arg = functionSymbol.Arguments[i];
                            argList.Add(isRef ? $"ref {arg}" : arg);
                        }
                    }

                    string argsStr = argList.Count > 0 ? string.Join(", ", argList) : "None";

                    sb.Append(indent).Append($"Symbol: {symbolName}, type: Function Header {{");
                    sb.Append($" Arity: {functionSymbol.Arity}, Scope: {scope}, StartAddress: {FluenceDebug.FormatByteCodeAddress(functionSymbol.StartAddress)},");
                    sb.Append($" Signature: {argsStr}, ").Append($" LocationInSource: {functionSymbol.StartAddressInSource}").AppendLine();
                    break;
                case VariableSymbol variableSymbol:
                    sb.Append(indent).Append($"Symbol: {symbolName}, {variableSymbol}.").AppendLine();
                    break;

                case TraitSymbol traitSymbol:
                    sb.Append(indent).Append($"TraitSymbol: {traitSymbol.Name} \n{indent}{indent} Variable Signatures: \n");

                    foreach (KeyValuePair<string, List<Token>> item in traitSymbol.DefaultFieldValuesAsTokens)
                    {
                        sb.Append(indent).Append(indent).Append(indent).Append($"{item.Key} : {(item.Value == null ? "None (Nil)" : (item.Value.Count == 0 ? "None (Nil)." : string.Join(", ", item.Value)))}\n");
                    }

                    if (traitSymbol.FieldSignatures.Count == 0)
                    {
                        sb.Append(indent).Append(indent).Append(indent).Append("None.").AppendLine();
                    }

                    sb.Append(indent).Append(indent).Append("Function Signatures: \n");

                    TraitSymbol.FunctionSignature[] fValues = traitSymbol.FunctionSignatures.Values.ToArray();
                    sb.Append(indent).Append(indent).Append(indent);

                    if (traitSymbol.FunctionSignatures.Count == 0)
                    {
                        sb.Append("None.");
                    }
                    else
                    {
                        for (int i = 0; i < traitSymbol.FunctionSignatures.Count; i++)
                        {
                            sb.Append($"{(i < traitSymbol.FunctionSignatures.Count - 1 ? $"{fValues[i].Name}-{fValues[i].Arity}, " : $"{fValues[i].Name}-{fValues[i].Arity}")}");
                        }
                    }
                    sb.AppendLine();
                    break;

                case StructSymbol structSymbol:
                    sb.Append(indent).Append($"Symbol: {symbolName}, type Struct {{").AppendLine();
                    sb.Append(innerIndent).Append("Fields: ").Append(structSymbol.Fields.Count != 0 ? string.Join(", ", structSymbol.Fields) : "None").AppendLine(".");

                    foreach (KeyValuePair<string, FunctionValue> function in structSymbol.Functions)
                    {
                        FunctionValue fv = function.Value;
                        List<string> fArgs = new List<string>();

                        if (fv.Arguments != null)
                        {
                            for (int i = 0; i < fv.Arguments.Count; i++)
                            {
                                bool isRef = (fv.RefMask & (1 << i)) != 0;
                                fArgs.Add(isRef ? $"ref {fv.Arguments[i]}" : fv.Arguments[i]);
                            }
                        }

                        string fArgsStr = fArgs.Count > 0 ? string.Join(", ", fArgs) : "None";

                        sb.Append(innerIndent).Append($"    Name: {function.Key}, Arity: {fv.Arity}, Start Address: {FormatByteCodeAddress(fv.StartAddress)}")
                          .Append($" Signature: {fArgsStr}, ").Append($"Scope: {fv.DefiningScope}, ").Append($"Registers Size: {fv.TotalRegisterSlots}").AppendLine();
                    }

                    sb.Append("\tDefault Values of Fields:");
                    if (structSymbol.DefaultFieldValuesAsTokens.Count != 0) sb.Append('\n');

                    foreach (KeyValuePair<string, List<Token>> item in structSymbol.DefaultFieldValuesAsTokens)
                    {
                        sb.Append($"\t\t{item.Key} : {(item.Value == null ? "None (Nil)" : (item.Value.Count == 0 ? "None (Nil)." : string.Join(", ", item.Value)))}\n");
                    }

                    if (structSymbol.DefaultFieldValuesAsTokens.Count == 0) sb.Append(" None.\n");

                    sb.Append(indent).Append(indent).Append($"Constructors: {(structSymbol.Functions.Count == 0 ? "None.\n" : "\n")}");
                    foreach (KeyValuePair<string, FunctionValue> item in structSymbol.Constructors)
                    {
                        sb.Append(indent).Append(indent).Append(indent).Append(item).AppendLine();
                    }

                    sb.Append(indent).Append(indent).Append($"Functions: {(structSymbol.Functions.Count == 0 ? "None.\n" : "\n")}");
                    foreach (KeyValuePair<string, FunctionValue> item in structSymbol.Functions)
                    {
                        sb.Append(indent).Append(indent).Append(indent).Append(item).AppendLine();
                    }

                    sb.Append(indent).Append($"Static Intrinsics: {(structSymbol.StaticIntrinsics.Count == 0 ? "None.\n" : "\n")}");
                    foreach (KeyValuePair<string, FunctionSymbol> item in structSymbol.StaticIntrinsics)
                    {
                        sb.Append(indent).Append(indent).Append(item).AppendLine();
                    }

                    sb.Append(indent).Append($"Static Fields: {(structSymbol.StaticFields.Count == 0 ? "None.\n" : "\n")}");
                    foreach (KeyValuePair<string, RuntimeValue> item in structSymbol.StaticFields)
                    {
                        sb.Append(indent).Append(indent).Append(item).AppendLine();
                    }

                    sb.Append(indent).AppendLine("}");
                    break;
            }
        }

        /// <summary>
        /// Generates a C# code string that declares and initializes a <see cref="List{InstructionLine}"/>
        /// with the exact content of the provided bytecode list.
        /// </summary>
        /// <param name="bytecode">The list of instructions to convert into C# code.</param>
        /// <param name="variableName">The name for the C# list variable.</param>
        /// <returns>A formatted string of C# code.</returns>
        internal static void GenerateCSharpCodeForInstructionList(List<InstructionLine> bytecode, TextOutputMethod outLine, string variableName = "expectedCode")
        {
            outLine("\n----------Code String For Tests----------\n");

            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"List<InstructionLine> {variableName} = new List<InstructionLine>");
            sb.AppendLine("{");

            foreach (InstructionLine instruction in bytecode)
            {
                if (instruction == null)
                {
                    sb.AppendLine("    null,");
                    continue;
                }

                string instructionType = $"InstructionCode.{instruction.Instruction}";
                string lhs = GenerateValueCode(instruction.Lhs);
                string rhs = GenerateValueCode(instruction.Rhs);
                string rhs2 = GenerateValueCode(instruction.Rhs2);
                string rhs3 = GenerateValueCode(instruction.Rhs3);

                sb.AppendLine($"    new({instructionType}, {(lhs == "" ? "null!," : $"{lhs},")} {(rhs == "" ? "null!," : $"{rhs},")} {(rhs2 == "" ? "null!," : $"{rhs2},")} {(rhs3 == "" ? "null!" : $"{rhs3}")}),");
            }

            sb.AppendLine("};");
            outLine(sb.ToString());
            outLine("\n----------Code String For Tests End----------\n\n\n");
        }

        private static string GenerateValueCode(Value value)
        {
            switch (value)
            {
                case null: return "";
                case NumberValue numVal: return $"new NumberValue({numVal.Value})";
                case StringValue strVal:
                    string escapedString = strVal.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    return $"new StringValue(\"{escapedString}\")";
                case NilValue: return "new NilValue()";
                case TempValue tempVal: return $"new TempValue({tempVal.TempIndex})";
                case VariableValue varVal: return $"new VariableValue(\"{varVal.Name}\")";
                case FunctionValue funcVal: return $"new FunctionValue(\"{funcVal.Name}\", {funcVal.StartAddress}, {funcVal.Arity}, {funcVal.StartAddressInSource}, [], [], null!)";
                default: return "";
            }
        }
    }
}