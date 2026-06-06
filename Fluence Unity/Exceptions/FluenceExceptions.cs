using System;
using System.Text;

namespace Fluence.Unity.Exceptions
{
    /// <summary>
    /// The base class for all exceptions thrown by the Fluence VM.
    /// </summary>
    public class FluenceException : Exception
    {
        internal readonly ExceptionContext? _context;

        public FluenceException(string message) : base(message)
        {
            _context = null;
        }

        internal FluenceException(string message, ExceptionContext context) : base(message)
        {
            _context = context;
        }

        public override string Message
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append(_context?.Format());
                if (_context is not RuntimeExceptionContext)
                {
                    stringBuilder.AppendLine($"Exception: {base.Message}");
                }
                return stringBuilder.ToString();
            }
        }
    }

    /// <summary>
    /// Represents an exception that occurs during the execution of a Fluence script by the VM.
    /// </summary>
    public sealed class FluenceRuntimeException : FluenceException
    {
        internal FluenceRuntimeException(string message, RuntimeExceptionContext context)
            : base(message, context) { }
    }

    /// <summary>
    /// Represents an error that occurs during lexical analysis.
    /// </summary>
    public sealed class FluenceLexerException : FluenceException
    {
        internal FluenceLexerException(string message, LexerExceptionContext context)
            : base(message, context) { }
    }

    /// <summary>
    /// Represents an error that occurs during parsing.
    /// </summary>
    public sealed class FluenceParserException : FluenceException
    {
        internal FluenceParserException(string message, ParserExceptionContext context)
            : base(message, context) { }
    }
}