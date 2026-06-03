using System.Text;

namespace Fluence.Unity
{
    /// <summary>
    /// Provides context for an error that occurred during the parsing phase.
    /// </summary>
    internal sealed class ParserExceptionContext : ExceptionContext
    {
        /// <summary>The line number where the error occurred.</summary>
        internal int LineNum { get; set; }

        /// <summary>The column number where the error occurred.</summary>
        internal int Column { get; set; }

        /// <summary>
        /// The Fluence script file where the error occurred.
        /// </summary>
        internal string FileName { get; set; }

        /// <summary>The source code of the line where the error occurred.</summary>
        internal string FaultyLine { get; set; }

        /// <summary>Gets the token that the parser could not process.</summary>
        internal Token UnexpectedToken { get; set; }

        internal override string Format()
        {
            StringBuilder stringBuilder = new StringBuilder();

            if (LineNum > 0 && FaultyLine != null && Column > 0)
            {
                int lineNumLen = LineNum.ToString().Length;
                stringBuilder
                    .AppendLine($"\nException occurred in: {(string.IsNullOrEmpty(FileName) ? "Script" : FileName)}.")
                    .AppendLine($"PARSER ERROR at: line {LineNum}, Column {Column}")
                    .AppendLine($"\n{LineNum}.│ {FaultyLine}")
                    .AppendLine($"{new string(' ', lineNumLen + 1)}│{new string(' ', Column - lineNumLen)}^")
                    .AppendLine($"{new string('─', lineNumLen + 1)}┴{new string('─', Column - lineNumLen)}┴{new string('─', FaultyLine.Length)}");
            }

            string tokenText = (UnexpectedToken.Text is "\r" or "\n" or "\r\n" or ";\r\n")
                ? "NewLine"
                : UnexpectedToken.ToDisplayString();

            if (UnexpectedToken.Type != Token.TokenType.NO_USE)
            {
                stringBuilder.AppendLine($"Error: Unexpected token '{tokenText}' (Type: {UnexpectedToken.Type}).");
            }

            return stringBuilder.ToString();
        }
    }
}