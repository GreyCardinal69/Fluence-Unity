namespace Fluence.Unity.VirtualMachine
{
    /// <summary>
    /// Represents a single frame in the virtual machine's call stack, used primarily for generating stack traces during exceptions.
    /// </summary>
    /// <param name="FunctionName">The mangled or original name of the executing function.</param>
    /// <param name="FileName">The source file where the function is defined.</param>
    /// <param name="LineNumber">The line number in the source file where the function was called or declared.</param>
    internal readonly struct StackFrameInfo
    {
        internal readonly string FunctionName;
        internal readonly string FileName;
        internal readonly int LineNumber;

        internal StackFrameInfo(string functionName, string fileName, int lineNumber)
        {
            FunctionName = functionName;
            FileName = fileName;
            LineNumber = lineNumber;
        }

        public override string ToString() => $"StackFrameInfo: Line:{LineNumber}, Function:{FunctionName}, File:{FileName}";
    }
}