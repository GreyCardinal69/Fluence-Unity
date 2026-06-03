namespace Fluence.Unity
{
    /// <summary>
    /// Defines how the Virtual Machine handles script lifecycle after completion.
    /// </summary>
    public enum FluenceExecutionMode
    {
        /// <summary>
        /// The script is treated as a single-run program. 
        /// Calling 'Run' after the script finishes will automatically reset the VM and run it again from the beginning.
        /// Ideal for command-line tools or stateless logic evaluation.
        /// </summary>
        Stateless,

        /// <summary>
        /// The script is treated as a persistent state container.
        /// After the main execution finishes, the VM remains alive. Calling 'Run' again does nothing, 
        /// but Global Variables remain accessible and Functions can still be called manually.
        /// </summary>
        Persistent
    }
}