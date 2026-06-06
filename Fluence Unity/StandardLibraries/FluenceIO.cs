using Fluence.Unity.RuntimeTypes;
using System.Collections.Generic;
using System.IO;

namespace Fluence.Unity
{
    /// <summary>
    /// Registers intrinsic functions for the 'FluenceIO' namespace.
    /// </summary>
    internal static class FluenceIO
    {
        internal const string NamespaceName = "FluenceIO";

        internal static void Register(FluenceScope ioNamespace)
        {
            RuntimeValue nilResult = RuntimeValue.Nil;

            //
            // --- File Static Struct ---
            //
            StructSymbol file = new StructSymbol("File", ioNamespace);
            ioNamespace.Declare("File".GetHashCode(), file);

            file.StaticIntrinsics.Add("write__2", new FunctionSymbol("write__2", 2, (vm, argCount) =>
            {
                (string, string) args = IntrinsicHelpers.GetTwoStringArgs(vm, "File.write()");
                File.WriteAllText(args.Item1, args.Item2);
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            file.StaticIntrinsics.Add("append_text__2", new FunctionSymbol("append_text__2", 2, (vm, argCount) =>
            {
                (string, string) args = IntrinsicHelpers.GetTwoStringArgs(vm, "File.append_text()");
                File.AppendAllText(args.Item1, args.Item2);
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            file.StaticIntrinsics.Add("move__2", new FunctionSymbol("move__2", 2, (vm, argCount) =>
            {
                (string, string) args = IntrinsicHelpers.GetTwoStringArgs(vm, "File.move()");
                File.Move(args.Item2, args.Item1);
                return nilResult;
            }, ioNamespace, new List<string>() { "old_path" }));

            file.StaticIntrinsics.Add("read__1", new FunctionSymbol("read__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "File.read()");
                return vm.ResolveStringObjectRuntimeValue(File.ReadAllText(path));
            }, ioNamespace, new List<string>() { "path" }));

            file.StaticIntrinsics.Add("create__1", new FunctionSymbol("create__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "File.create()");
                File.Create(path).Close();
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            file.StaticIntrinsics.Add("delete__1", new FunctionSymbol("delete__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "File.delete()");
                File.Delete(path);
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            file.StaticIntrinsics.Add("exists__1", new FunctionSymbol("exists__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "File.exists()");
                return new RuntimeValue(File.Exists(path));
            }, ioNamespace, new List<string>() { "path" }));

            //
            // --- Path Static Struct ---
            //
            StructSymbol pathStruct = new StructSymbol("Path", ioNamespace);
            ioNamespace.Declare("Path".GetHashCode(), pathStruct);

            pathStruct.StaticIntrinsics.Add("dir_sep_char__0", new FunctionSymbol("dir_sep_char__0", 0, (vm, argCount) =>
            {
                return vm.ResolveCharObjectRuntimeValue(Path.DirectorySeparatorChar);
            }, ioNamespace, new List<string>()));

            pathStruct.StaticIntrinsics.Add("has_extension__1", new FunctionSymbol("has_extension__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Path.has_extension()");
                return new RuntimeValue(Path.HasExtension(path));
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("change_extension__2", new FunctionSymbol("change_extension__2", 2, (vm, argCount) =>
            {
                (string, string) args = IntrinsicHelpers.GetTwoStringArgs(vm, "Path.change_extension()");
                return vm.ResolveStringObjectRuntimeValue(Path.ChangeExtension(args.Item2, args.Item1));
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("exists__1", new FunctionSymbol("exists__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Path.exists()");
                bool exists = File.Exists(path) || Directory.Exists(path);
                return new RuntimeValue(exists);
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("get_dir_name__1", new FunctionSymbol("get_dir_name__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Path.get_dir_name()");
                string dirName = Path.GetDirectoryName(path);
                return dirName == null ? RuntimeValue.Nil : vm.ResolveStringObjectRuntimeValue(dirName);
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("get_file_name__1", new FunctionSymbol("get_file_name__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Path.get_file_name()");
                return vm.ResolveStringObjectRuntimeValue(Path.GetFileName(path));
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("get_file_name_raw__1", new FunctionSymbol("get_file_name_raw__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Path.get_file_name_raw()");
                return vm.ResolveStringObjectRuntimeValue(Path.GetFileNameWithoutExtension(path));
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("get_full_path__1", new FunctionSymbol("get_full_path__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Path.get_full_path()");
                string fullPath = Path.GetFullPath(path);
                return fullPath == null ? RuntimeValue.Nil : vm.ResolveStringObjectRuntimeValue(fullPath);
            }, ioNamespace, new List<string>() { "path" }));

            pathStruct.StaticIntrinsics.Add("get_temp_path__0", new FunctionSymbol("get_temp_path__0", 0, (vm, argCount) =>
            {
                return vm.ResolveStringObjectRuntimeValue(Path.GetTempPath());
            }, ioNamespace, new List<string>()));

            //
            // --- Directory Static Struct ---
            //
            StructSymbol dir = new StructSymbol("Dir", ioNamespace);
            ioNamespace.Declare("Dir".GetHashCode(), dir);

            dir.StaticIntrinsics.Add("create__1", new FunctionSymbol("create__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Dir.create()");
                Directory.CreateDirectory(path);
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            dir.StaticIntrinsics.Add("delete__1", new FunctionSymbol("delete__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Dir.delete()");
                Directory.Delete(path);
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            dir.StaticIntrinsics.Add("exists__1", new FunctionSymbol("exists__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Dir.exists()");
                return new RuntimeValue(Directory.Exists(path));
            }, ioNamespace, new List<string>() { "path" }));

            dir.StaticIntrinsics.Add("get_dirs__1", new FunctionSymbol("get_dirs__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Dir.get_dirs()");
                ListObject list = new ListObject();
                foreach (string item in Directory.GetDirectories(path))
                {
                    list.Elements.Add(vm.ResolveStringObjectRuntimeValue(item));
                }
                return new RuntimeValue(list);
            }, ioNamespace, new List<string>() { "path" }));

            dir.StaticIntrinsics.Add("get_files__1", new FunctionSymbol("get_files__1", 1, (vm, argCount) =>
            {
                string path = IntrinsicHelpers.GetStringArg(vm, "Dir.get_files()");
                ListObject list = new ListObject();
                foreach (string item in Directory.GetFiles(path))
                {
                    list.Elements.Add(vm.ResolveStringObjectRuntimeValue(item));
                }
                return new RuntimeValue(list);
            }, ioNamespace, new List<string>() { "path" }));

            dir.StaticIntrinsics.Add("move__2", new FunctionSymbol("move__2", 2, (vm, argCount) =>
            {
                (string, string) args = IntrinsicHelpers.GetTwoStringArgs(vm, "Dir.move()");
                Directory.Move(args.Item2, args.Item1);
                return nilResult;
            }, ioNamespace, new List<string>() { "path" }));

            dir.StaticIntrinsics.Add("get_current__0", new FunctionSymbol("get_current__0", 0, (vm, argCount) =>
            {
                return vm.ResolveStringObjectRuntimeValue(Directory.GetCurrentDirectory());
            }, ioNamespace, new List<string>()));
        }
    }
}