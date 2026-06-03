using System.Text;

namespace Fluence.Unity.Exceptions
{
    /// <summary>
    /// Provides context for an error that occurred during the lexing phase.
    /// </summary>
    internal sealed class LexerExceptionContext : ExceptionContext
    {
        /// <summary>The line number where the error occurred.</summary>
        internal int LineNum { get; set; }

        /// <summary>The column number where the error occurred.</summary>
        internal int Column { get; set; }

        /// <summary>
        /// The Fluence script file where the error occurred.
        /// </summary>
        internal string FileName { get; set; }

        /// <summary>
        /// Last parsed token.
        /// </summary>
        internal Token Token { get; set; }

        /// <summary>The source code of the line where the error occurred.</summary>
        internal string FaultyLine { get; set; }

        internal override string Format()
        {
            StringBuilder stringBuilder = new StringBuilder();

            if (LineNum > 0 && FaultyLine != null && Column > 0)
            {
                int lineNumLen = LineNum.ToString().Length;
                stringBuilder
                    .AppendLine($"\nException occurred in: {(string.IsNullOrEmpty(FileName) ? "Script" : FileName)}.")
                    .AppendLine($"LEXER ERROR at: line {LineNum}, Column {Column}")
                    .AppendLine($"\n{LineNum}.│ {FaultyLine}")
                    .AppendLine($"{new string(' ', lineNumLen + 1)}│{new string(' ', Column - 1)}^")
                    .AppendLine($"{new string('─', lineNumLen + 1)}┴{new string('─', Column - 1)}┴{new string('─', FaultyLine.Length)}");
            }

            string tokenText = Token.Text is "\r" or "\n" or "\r\n" or ";\r\n" ? "NewLine" : Token.Text;
            string tokenLiteral = (string)(Token.Literal is (object)"\r" or (object)"\n" or (object)"\r\n" ? "NewLine" : Token.Literal);

            tokenText ??= "Null";
            tokenLiteral ??= "Null";

            stringBuilder.AppendLine($"Last Token scanned <Type, Text, Literal>: <{Token.Type}, {tokenText}, {tokenLiteral}>");

            return stringBuilder.ToString();
        }
    }
}