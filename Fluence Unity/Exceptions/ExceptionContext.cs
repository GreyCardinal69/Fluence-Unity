namespace Fluence.Unity
{
    /// <summary>
    /// Provides detailed, context-rich information about an error.
    /// </summary>
    internal abstract class ExceptionContext
    {
        /// <summary>
        /// Formats the exception context into a user-friendly, multi-line error message.
        /// </summary>
        /// <returns>A formatted string detailing the error context.</returns>
        internal abstract string Format();
    }
}