using Fluence.Unity.RuntimeTypes;
using UnityEngine;
using static Fluence.Unity.FluenceUnity;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class QuaternionWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static QuaternionWrapper()
        {
            _getters["x"] = w => new RuntimeValue((double)((Quaternion)w.Instance).x);
            _getters["y"] = w => new RuntimeValue((double)((Quaternion)w.Instance).y);
            _getters["z"] = w => new RuntimeValue((double)((Quaternion)w.Instance).z);
            _getters["w"] = w => new RuntimeValue((double)((Quaternion)w.Instance).w);
            _getters["eulerAngles"] = w => new RuntimeValue(Vector3Wrapper.Create(((Quaternion)w.Instance).eulerAngles));
            _getters["normalized"] = w => new RuntimeValue(Create(((Quaternion)w.Instance).normalized));

            _setters["x"] = (w, val) => { Quaternion q = (Quaternion)w.Instance; q.x = ExtractFloat(val); w.Instance = q; };
            _setters["y"] = (w, val) => { Quaternion q = (Quaternion)w.Instance; q.y = ExtractFloat(val); w.Instance = q; };
            _setters["z"] = (w, val) => { Quaternion q = (Quaternion)w.Instance; q.z = ExtractFloat(val); w.Instance = q; };
            _setters["w"] = (w, val) => { Quaternion q = (Quaternion)w.Instance; q.w = ExtractFloat(val); w.Instance = q; };
        }

        internal static Wrapper Create(Quaternion quat) => new Wrapper(quat, _methods, _getters, _setters);

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
            {
                new FunctionSymbol("Quaternion__0", 0, (vm, _) => new RuntimeValue(Create(Quaternion.identity)), scope, new List<string>()),
                new FunctionSymbol("Quaternion__4", 4, (vm, _) => new RuntimeValue(Create(new Quaternion(ExtractFloat(vm.PopStack()), ExtractFloat(vm.PopStack()), ExtractFloat(vm.PopStack()), ExtractFloat(vm.PopStack())))), scope, new List<string>() {"x", "y", "z", "w"})
            };
        }

        internal static void RegisterStatics(StructSymbol quat, FluenceScope scope)
        {
            RegisterField(quat, "identity", new RuntimeValue(Create(Quaternion.identity)));

            RegisterIntrinsic(quat, "euler", 3, (vm, _) =>
            {
                float z = ExtractFloat(vm.PopStack());
                float y = ExtractFloat(vm.PopStack());
                float x = ExtractFloat(vm.PopStack());
                return new RuntimeValue(Create(Quaternion.Euler(x, y, z)));
            }, scope, "x", "y", "z");

            RegisterIntrinsic(quat, "angle_axis", 2, (vm, _) =>
            {
                Vector3 axis = ExtractVector3(vm.PopStack(), vm);
                float angle = ExtractFloat(vm.PopStack());
                return new RuntimeValue(Create(Quaternion.AngleAxis(angle, axis)));
            }, scope, "angle", "axis");

            RegisterIntrinsic(quat, "from_to_rotation", 2, (vm, _) =>
            {
                Vector3 to = ExtractVector3(vm.PopStack(), vm);
                Vector3 from = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.FromToRotation(from, to)));
            }, scope, "from", "to");

            RegisterIntrinsic(quat, "look_rotation", 1, (vm, _) =>
            {
                Vector3 forward = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.LookRotation(forward)));
            }, scope, "forward");

            RegisterIntrinsic(quat, "look_rotation", 2, (vm, _) =>
            {
                Vector3 up = ExtractVector3(vm.PopStack(), vm);
                Vector3 forward = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.LookRotation(forward, up)));
            }, scope, "forward", "up");

            RegisterIntrinsic(quat, "slerp", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.Slerp(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(quat, "slerp_unclamped", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.SlerpUnclamped(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(quat, "lerp", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.Lerp(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(quat, "lerp_unclamped", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.LerpUnclamped(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(quat, "rotate_towards", 2, (vm, _) =>
            {
                float maxDeg = ExtractFloat(vm.PopStack());
                Quaternion to = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion from = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.RotateTowards(from, to, maxDeg)));
            }, scope, "from", "to", "maxDegreesDelta");

            RegisterIntrinsic(quat, "inverse", 1, (vm, _) =>
            {
                Quaternion rot = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(Quaternion.Inverse(rot)));
            }, scope, "rotation");

            RegisterIntrinsic(quat, "angle", 2, (vm, _) =>
            {
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue((double)Quaternion.Angle(a, b));
            }, scope, "a", "b");

            RegisterIntrinsic(quat, "dot", 2, (vm, _) =>
            {
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue((double)Quaternion.Dot(a, b));
            }, scope, "a", "b");

            RegisterIntrinsic(quat, "multiply", 2, (vm, _) =>
            {
                Quaternion b = ExtractQuaternion(vm.PopStack(), vm);
                Quaternion a = ExtractQuaternion(vm.PopStack(), vm);
                return new RuntimeValue(Create(a * b));
            }, scope, "a", "b");
        }
    }
}