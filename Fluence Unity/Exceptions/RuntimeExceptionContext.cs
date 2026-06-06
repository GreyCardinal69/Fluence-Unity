using Fluence.Unity.Exceptions;
using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Fluence.Unity.FluenceByteCode;

namespace Fluence.Unity
{
    internal sealed class RuntimeExceptionContext : ExceptionContext
    {
        internal string ExceptionMessage { get; set; }
        internal VMDebugContext DebugContext { get; set; }
        internal List<StackFrameInfo> StackTraces { get; set; }
        internal InstructionLine InstructionLine { get; set; }
        internal FluenceParser Parser { get; set; }
        internal RuntimeExceptionType ExceptionType { get; set; }

        private void ElaborateOnUndefinedFunction(FluenceScope scope, StringBuilder stringBuilder, out bool foundMatch)
        {
            string undefinedVariable = ExceptionMessage.Split('\'')[1];
            Mangler.Demangle(undefinedVariable, out int deMangledArity);
            string deMangledVar = Mangler.Demangle(undefinedVariable);

            int errorlLineNum = InstructionLine.LineInSourceCode;
            int lineNumLen = errorlLineNum.ToString().Length;
            foundMatch = false;
            string leftPad = new string(' ', lineNumLen + 1);

            foreach (Symbol symbol in scope.Symbols.Values)
            {
                if (symbol is FunctionSymbol func)
                {
                    string deMangledFunc = Mangler.Demangle(func.Name);

                    if (string.Equals(deMangledFunc, deMangledVar, StringComparison.Ordinal) && func.Arity != deMangledArity)
                    {
                        if (!foundMatch)
                        {
                            foundMatch = true;
                            stringBuilder.AppendLine($"Runtime Error: Function \"{deMangledFunc}\" does not accept {deMangledArity} argument(s).");
                            stringBuilder.AppendLine($"{leftPad}│\tAvailable signatures are:");
                            stringBuilder.AppendLine($"{leftPad}│\t\t- func {Mangler.Demangle(func.Name)}({(func.Arguments != null ? string.Join(", ", func.Arguments) : "None")})");
                        }
                        else
                        {
                            stringBuilder.AppendLine($"{leftPad}│\t\t- func {Mangler.Demangle(func.Name)}({(func.Arguments != null ? string.Join(", ", func.Arguments) : "None")})");
                        }
                    }
                }
            }
        }

        private void ElaborateOnContext(RuntimeExceptionType excType)
        {
            int errorlLineNum = InstructionLine.LineInSourceCode;
            int lineNumLen = errorlLineNum.ToString().Length;
            bool replaceErrorMessage = false;
            StringBuilder stringBuilder = new StringBuilder();

            switch (excType)
            {
                case RuntimeExceptionType.UnknownVariable:

                    foreach (FluenceScope scope in Parser.CurrentParseState.NameSpaces.Values)
                    {
                        ElaborateOnUndefinedFunction(scope, stringBuilder, out replaceErrorMessage);
                    }

                    if (!replaceErrorMessage)
                    {
                        ElaborateOnUndefinedFunction(Parser.CurrentParserStateGlobalScope, stringBuilder, out replaceErrorMessage);
                    }
                    break;
                case RuntimeExceptionType.ScriptException:
                    replaceErrorMessage = true;
                    stringBuilder.AppendLine($"Script Exception: \"{ExceptionMessage}\"");
                    break;
                default:
                    break;
            }

            stringBuilder.Append($"{new string(' ', lineNumLen + 1)}│");
            if (replaceErrorMessage)
            {
                ExceptionMessage = stringBuilder.ToString();
            }
        }

        internal override string Format()
        {
            StringBuilder stringBuilder = new StringBuilder();

            string fileName = null;
            string filepath = null;
            string faultyLine;

            if (Parser.IsMultiFileProject)
            {
                filepath = Parser.CurrentParseState.ProjectFilePaths[InstructionLine.ProjectFileIndex];
                fileName = Path.GetFileName(filepath);
                string[] lines = File.ReadAllLines(filepath);
                faultyLine = lines[InstructionLine.LineInSourceCode == -1 ? 0 : (InstructionLine.LineInSourceCode == 0 ? 0 : InstructionLine.LineInSourceCode - 1)];
            }
            else
            {
                faultyLine = Parser.Lexer.SourceCode.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[InstructionLine.LineInSourceCode - 1];
            }

            int errorlLineNum = InstructionLine.LineInSourceCode;
            int errorColumnNum = InstructionLine.ColumnInSourceCode;
            int lineNumLen = errorlLineNum.ToString().Length;

            if (ExceptionType != RuntimeExceptionType.NonSpecific)
            {
                ElaborateOnContext(ExceptionType);
            }

            stringBuilder.AppendLine("Fluence.Exceptions.FluenceRuntimeException:");
            stringBuilder.AppendLine($"\nException occurred in: {(string.IsNullOrEmpty(fileName) ? "Script" : fileName)}.");

            if (!string.IsNullOrEmpty(filepath))
            {
                stringBuilder.AppendLine($"Exact path: {filepath}");
            }

            string leftPad = new string(' ', lineNumLen + 1);

            if (errorlLineNum > 0 && faultyLine != null && errorColumnNum > 0)
            {
                stringBuilder
                    .AppendLine($"RUNTIME ERROR at approximately: line {errorlLineNum}, Column {errorColumnNum}")
                    .AppendLine($"\nMost likely line where the error occurred:")
                    .AppendLine($"\n{leftPad}│")
                    .AppendLine($"{leftPad}│")
                    .AppendLine($"{leftPad}│\t{ExceptionMessage}")
                    .AppendLine($"{leftPad}│")
                    .AppendLine($"{errorlLineNum}.│ {faultyLine}")
                    .AppendLine($"{leftPad}│{new string(' ', errorColumnNum - lineNumLen)}^")
                    .AppendLine($"{new string('─', lineNumLen + 1)}┴{new string('─', errorColumnNum - lineNumLen)}┴{new string('─', faultyLine.Length)}");
            }

            stringBuilder.AppendLine($"\nState at the moment of the exception:\n")
                .AppendLine($"In Function       :│ \"{Mangler.Demangle(DebugContext.CurrentFunctionName, out _)}\"")
                .Append($"                   │ \n");

            if (DebugContext.CurrentLocals.Count > 0)
            {
                bool isFirstLocal = true;
                List<KeyValuePair<string, RuntimeValue>> displayItems = new List<KeyValuePair<string, RuntimeValue>>();
                foreach (KeyValuePair<string, RuntimeValue> local in DebugContext.CurrentLocals)
                {
                    displayItems.Add(local);
                }

                displayItems.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

                int maxKeyLength = 0;
                if (displayItems.Count > 0)
                {
                    maxKeyLength = displayItems.Max(item => item.Key.Length);
                }

                foreach (KeyValuePair<string, RuntimeValue> kvp in displayItems)
                {
                    string value = kvp.Value.ToString();
                    string end = value.Length > 150 ? "...\"" : "\"";

                    // Unity-safe substring slice replacement for standard BCL
                    string slicedValue = value[..Math.Min(150, value.Length)];
                    string formattedValue = $"\"{slicedValue}{end}".Replace("\n", "\\n").Replace("\r\n", "\\r\\n");

                    string paddedKey = kvp.Key.PadRight(maxKeyLength);

                    if (isFirstLocal)
                    {
                        stringBuilder.AppendLine($"Local Variables   :│ {paddedKey} = {formattedValue}");
                        isFirstLocal = false;
                    }
                    else
                    {
                        stringBuilder.AppendLine($"                   │ {paddedKey} = {formattedValue}");
                    }
                }
            }
            else
            {
                stringBuilder.AppendLine("EMPTY");
            }

            stringBuilder.AppendLine("                   │");

            stringBuilder.Append("Operand Stack     :│");

            if (DebugContext.OperandStackSnapshot.Count > 0)
            {
                bool isFirstOperand = true;
                foreach (RuntimeValue item in DebugContext.OperandStackSnapshot)
                {
                    if (isFirstOperand)
                    {
                        stringBuilder.AppendLine($" [{item}]");
                        isFirstOperand = false;
                    }
                    else
                    {
                        stringBuilder.AppendLine($"                   │ [{item}]");
                    }
                }
            }
            else
            {
                stringBuilder.AppendLine(" EMPTY");
            }

            string separator = new string('─', 50);
            stringBuilder.AppendLine(separator);
            stringBuilder.AppendLine("\nLast Virtual Machine Instruction and Function:\n");
            stringBuilder.AppendLine($"IP: {DebugContext.InstructionPointer:D4}   Function: {Mangler.Demangle(DebugContext.CurrentFunctionName, out _)}   Call Stack Depth: {DebugContext.CallStackDepth}");
            stringBuilder.AppendLine($"\nThe Error occurred at the following bytecode instruction:\n");
            stringBuilder.AppendLine($"{string.Format("{0,-25} {1,-40} {2,-55} {3,-40} {4, -25}", "TYPE", "LHS", "RHS", "RHS2", "RHS3")}");
            stringBuilder.AppendLine($"{DebugContext.CurrentInstruction}");

            stringBuilder.AppendLine($"\nStack Trace (most recent call last):");

            foreach (StackFrameInfo trace in StackTraces)
            {
                stringBuilder.AppendLine($"\tat {Mangler.Demangle(trace.FunctionName, out _)} ({trace.FileName} : {trace.LineNumber})");
            }

            return stringBuilder.ToString();
        }
    }
}