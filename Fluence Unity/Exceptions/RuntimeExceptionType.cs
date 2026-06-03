namespace Fluence.Unity.Exceptions
{
    public enum RuntimeExceptionType
    {
        NonSpecific,
        UnknownVariable,

        Custom,

        /// <summary>
        /// Indicates an exception that was thrown from the script itself by the programmer using the 'throw' keyword.
        /// </summary>
        ScriptException
    }
}