using System;
using System.Collections.Generic;

namespace Fluence.Unity
{
    /// <summary>
    /// Provides a set of configurable options to control the behavior and performance
    /// characteristics of the Fluence virtual machine.
    /// </summary>
    public sealed class VirtualMachineConfiguration
    {
        /// <summary>
        /// Gets or sets a collection of active conditional compilation symbols.
        /// Code inside an '#IF SYMBOL {...}' block will only be parsed if the symbol
        /// is present in this set. Symbols are case-sensitive, all uppercase.
        /// </summary>
        /// <remarks>
        /// Common symbols include: DEBUG, RELEASE, UNITY_EDITOR, WINDOWS, LINUX, IOS, ANDROID, WEB.
        /// </remarks>
        public HashSet<string> CompilationSymbols { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets a value indicating whether the generated Fluence bytecode should be
        /// run through an incremental optimization pass before execution.
        /// This is primarily a debug option.
        /// </summary>
        /// <remarks>
        /// When enabled, the optimizer may merge, replace, or reorder instructions to improve
        /// execution speed. This can result in a slightly longer compilation phase but
        /// leads to better runtime performance.
        /// <para>The default value is <c>true</c>.</para>
        /// </remarks>
        public bool OptimizeByteCode { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the parser should emit a <see cref="FluenceByteCode.InstructionLine.InstructionCode.SectionGlobal"/>
        /// instruction after the main bytecode instructions marking the start of the setup phase of the script. This is only a debug option used for tests.
        /// </summary>
        /// <remarks>
        /// This value is absolutely crucial for the correct generation of bytecode and must not be set to false outside of parser tests.
        /// </remarks>
        internal bool EmitSectionGlobal { get; set; } = true;

        /// <summary>
        /// Gets or sets the global timeout for script execution when the VM is instructed to run until completion.
        /// If the script exceeds this duration, the Virtual Machine will pause or terminate.
        /// </summary>
        /// <remarks>
        /// This prevents scripts with infinite loops from freezing the host application. 
        /// Defaults to infinite (<see cref="TimeSpan.MaxValue"/>, no timeout).
        /// </remarks>
        public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.MaxValue;

        /// <summary>
        /// Gets or sets the lifecycle mode of the Virtual Machine.
        /// Defaults to <see cref="FluenceExecutionMode.Stateless"/>.
        /// </summary>
        public FluenceExecutionMode ExecutionMode { get; set; } = FluenceExecutionMode.Stateless;

        /// <summary>
        /// Defines logical halt points within the compilation and execution pipeline.
        /// </summary>
        internal enum ExecutionPipelineEndpoint
        {
            /// <summary>
            /// Halts execution immediately after the Lexer finishes tokenizing the source code.
            /// The Parser and Virtual Machine will not run.
            /// </summary>
            StopAtLexer,

            /// <summary>
            /// Halts execution after the Parser generates and optimizes the bytecode.
            /// The Virtual Machine will not run.
            /// </summary>
            StopAtParser,

            /// <summary>
            /// Allows the pipeline to run completely from Lexing through Virtual Machine execution.
            /// </summary>
            DontStop,
        }

        /// <summary>
        /// Gets or sets an internal flag indicating where the interpreter should halt its pipeline.
        /// </summary>
        /// <remarks>
        /// This is utilized only for granular benchmarking and low-level debugging without triggering subsequent compilation phases.
        /// Defaults to <see cref="ExecutionPipelineEndpoint.DontStop"/>.
        /// </remarks>
        internal ExecutionPipelineEndpoint ExecutionEndPoint { get; set; } = ExecutionPipelineEndpoint.DontStop;
    }
}