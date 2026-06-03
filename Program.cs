using Fluence.Unity;
using Fluence.Unity.Exceptions;
using System.Diagnostics;

namespace Fluence
{
    internal class Program
    {
        internal sealed class ProgramExecutionConfiguration
        {
            internal bool ShowProfile { get; set; }
            internal bool IsProject { get; set; }
            internal bool DumpByteCode { get; set; }
        }

        private static int Main(string[] args)
        {
            if (args.Length == 0 || IsHelpFlag(args[0]))
            {
                PrintHelp();
                return 0;
            }

            ProgramExecutionConfiguration config = new ProgramExecutionConfiguration();

            string targetPath = "";

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.Equals("-run", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) targetPath = args[++i];
                }
                else if (arg.Equals("-project", StringComparison.OrdinalIgnoreCase))
                {
                    config.IsProject = true;
                    if (i + 1 < args.Length) targetPath = args[++i];
                }
                else if (arg.Equals("-profile", StringComparison.OrdinalIgnoreCase))
                {
                    config.ShowProfile = true;
                }
                else if (arg.Equals("-bytecode", StringComparison.OrdinalIgnoreCase))
                {
                    config.DumpByteCode = true;
                }
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                PrintError("No input file or project directory specified.");
                return 1;
            }

            return RunInterpreter(targetPath, config);
        }

        private static int RunInterpreter(string path, ProgramExecutionConfiguration config)
        {
            if (config.IsProject)
            {
                if (!Directory.Exists(path))
                {
                    PrintError($"Error: The project directory '{path}' was not found.");
                    return 1;
                }
            }
            else
            {
                if (!File.Exists(path))
                {
                    PrintError($"Error: The file '{path}' was not found.");
                    return 1;
                }
            }

            try
            {
                FluenceInterpreter interpreter = new FluenceInterpreter();

                if (config.ShowProfile)
                {
                    interpreter.Configuration.OptimizeByteCode = true;
                }

                Console.WriteLine(config.IsProject ? $"Compiling Project: {path}..." : $"Compiling File: {path}...");

                Stopwatch sw = Stopwatch.StartNew();
                bool success = config.IsProject
                    ? interpreter.CompileProject(path)
                    : interpreter.Compile(File.ReadAllText(path));

                sw.Stop();

                if (!success)
                {
                    PrintError("Compilation failed.");
                    return 1;
                }

                if (config.DumpByteCode)
                {
                    FluenceDebug.DumpByteCodeInstructions(interpreter.ParseState.CodeInstructions, Console.WriteLine);
                }

                if (config.ShowProfile)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[Build Success] Compilation took: {sw.Elapsed.TotalMilliseconds:F2} ms");
                    Console.ResetColor();
                }

                sw.Restart();
                interpreter.RunUntilDone();
                sw.Stop();

                if (config.ShowProfile)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n------------------------------------------------");
                    Console.WriteLine($"[Execution Complete]");
                    Console.WriteLine($"Total Time: {sw.Elapsed.TotalMilliseconds:F4} ms");
                    Console.WriteLine("------------------------------------------------");
                    Console.ResetColor();
                }

                return 0;
            }
            catch (FluenceException ex)
            {
                PrintError($"Runtime Error:\n{ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                PrintError($"An unexpected internal error occurred: {ex.Message}");
                return 1;
            }
        }

        private static bool IsHelpFlag(string arg)
        {
            return arg.Equals("-help", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-?", StringComparison.Ordinal);
        }

        private static void PrintHelp()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Fluence Programming Language (v0.1.1-alpha)");
            Console.ResetColor();
            Console.WriteLine("Usage:");
            Console.WriteLine("  fluence -run <file.fl>       Run a single script file.");
            Console.WriteLine("  fluence -project <dir>       Run a multi-file project (looks for Main).");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  -profile                     Show compilation and execution timing statistics.");
            Console.WriteLine("  -help                        Show this help message.");
            Console.WriteLine("  -bytecode                    Show the final, compiled bytecode of the script or project.");
        }

        private static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}