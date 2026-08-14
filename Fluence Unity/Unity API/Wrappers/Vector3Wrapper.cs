using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using UnityEngine;
using static Fluence.Unity.FluenceUnity;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class Vector3Wrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static Vector3Wrapper()
        {
            _getters["x"] = w => new RuntimeValue((double)((Vector3)w.Instance).x);
            _getters["y"] = w => new RuntimeValue((double)((Vector3)w.Instance).y);
            _getters["z"] = w => new RuntimeValue((double)((Vector3)w.Instance).z);

            _getters["magnitude"] = w => new RuntimeValue((double)((Vector3)w.Instance).magnitude);
            _getters["sqrMagnitude"] = w => new RuntimeValue((double)((Vector3)w.Instance).sqrMagnitude);
            _getters["normalized"] = w => new RuntimeValue(Create(((Vector3)w.Instance).normalized));

            _setters["x"] = (w, val) => { Vector3 v = (Vector3)w.Instance; v.x = (float)val.DoubleValue; w.Instance = v; };
            _setters["y"] = (w, val) => { Vector3 v = (Vector3)w.Instance; v.y = (float)val.DoubleValue; w.Instance = v; };
            _setters["z"] = (w, val) => { Vector3 v = (Vector3)w.Instance; v.z = (float)val.DoubleValue; w.Instance = v; };

            _methods["normalize__0"] = (vm, self) =>
            {
                Vector3 v = (Vector3)self.As<Wrapper>().Instance;
                v.Normalize();
                self.As<Wrapper>().Instance = v;
                return RuntimeValue.Nil;
            };

            _methods["sqr_magnitude__0"] = (vm, self) =>
            {
                Vector3 v = (Vector3)((Wrapper)self.ObjectReference).Instance;
                return new RuntimeValue((double)v.sqrMagnitude);
            };

            _methods["magnitude__0"] = (vm, self) =>
            {
                Vector3 v = (Vector3)((Wrapper)self.ObjectReference).Instance;
                return new RuntimeValue((double)v.magnitude);
            };

            _methods["normalized__0"] = (vm, self) =>
            {
                Vector3 v = (Vector3)((Wrapper)self.ObjectReference).Instance;
                return new RuntimeValue(Vector3Wrapper.Create(v.normalized));
            };
        }

        public static RuntimeValue ProjectOnPlane(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 planeNormal = ExtractVector3(vm.PopStack(), vm);
            Vector3 vector = ExtractVector3(vm.PopStack(), vm);
            return new RuntimeValue(Vector3Wrapper.Create(Vector3.ProjectOnPlane(vector, planeNormal)));
        }

        public static RuntimeValue Project(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 onNormal = ExtractVector3(vm.PopStack(), vm);
            Vector3 vector = ExtractVector3(vm.PopStack(), vm);
            return new RuntimeValue(Vector3Wrapper.Create(Vector3.Project(vector, onNormal)));
        }

        public static RuntimeValue Cross(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 rhs = ExtractVector3(vm.PopStack(), vm);
            Vector3 lhs = ExtractVector3(vm.PopStack(), vm);
            return new RuntimeValue(Vector3Wrapper.Create(Vector3.Cross(lhs, rhs)));
        }

        public static RuntimeValue Dot(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 rhs = ExtractVector3(vm.PopStack(), vm);
            Vector3 lhs = ExtractVector3(vm.PopStack(), vm);
            return new RuntimeValue((double)Vector3.Dot(lhs, rhs));
        }

        public static RuntimeValue MoveTowards(FluenceVirtualMachine vm, RuntimeValue self)
        {
            float maxDelta = (float)vm.PopStack().DoubleValue;
            Vector3 target = ExtractVector3(vm.PopStack(), vm);
            Vector3 current = ExtractVector3(vm.PopStack(), vm);
            return new RuntimeValue(Vector3Wrapper.Create(Vector3.MoveTowards(current, target, maxDelta)));
        }

        internal static Wrapper Create(Vector3 vec) => new Wrapper(vec, _methods, _getters, _setters);

        internal static void RegisterStatics(StructSymbol vec3, FluenceScope scope)
        {
            RegisterField(vec3, "up", new RuntimeValue(Create(Vector3.up)));
            RegisterField(vec3, "down", new RuntimeValue(Create(Vector3.down)));
            RegisterField(vec3, "left", new RuntimeValue(Create(Vector3.left)));
            RegisterField(vec3, "right", new RuntimeValue(Create(Vector3.right)));
            RegisterField(vec3, "forward", new RuntimeValue(Create(Vector3.forward)));
            RegisterField(vec3, "back", new RuntimeValue(Create(Vector3.back)));
            RegisterField(vec3, "zero", new RuntimeValue(Create(Vector3.zero)));
            RegisterField(vec3, "one", new RuntimeValue(Create(Vector3.one)));

            RegisterIntrinsic(vec3, "distance", 2, (vm, _) =>
            {
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue((double)Vector3.Distance(a, b));
            }, scope, "a", "b");

            RegisterIntrinsic(vec3, "angle", 2, (vm, _) =>
            {
                Vector3 to = ExtractVector3(vm.PopStack(), vm);
                Vector3 from = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue((double)Vector3.Angle(from, to));
            }, scope, "from", "to");

            RegisterIntrinsic(vec3, "signed_angle", 3, (vm, _) =>
            {
                Vector3 axis = ExtractVector3(vm.PopStack(), vm);
                Vector3 to = ExtractVector3(vm.PopStack(), vm);
                Vector3 from = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue((double)Vector3.SignedAngle(from, to, axis));
            }, scope, "from", "to", "axis");

            RegisterIntrinsic(vec3, "dot", 2, (vm, _) =>
            {
                Vector3 rhs = ExtractVector3(vm.PopStack(), vm);
                Vector3 lhs = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue((double)Vector3.Dot(lhs, rhs));
            }, scope, "lhs", "rhs");

            RegisterIntrinsic(vec3, "cross", 2, (vm, _) =>
            {
                Vector3 rhs = ExtractVector3(vm.PopStack(), vm);
                Vector3 lhs = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Cross(lhs, rhs)));
            }, scope, "lhs", "rhs");

            RegisterIntrinsic(vec3, "lerp", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Lerp(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(vec3, "lerp_unclamped", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.LerpUnclamped(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(vec3, "slerp", 3, (vm, _) =>
            {
                float t = ExtractFloat(vm.PopStack());
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Slerp(a, b, t)));
            }, scope, "a", "b", "t");

            RegisterIntrinsic(vec3, "move_towards", 3, (vm, _) =>
            {
                float maxDelta = ExtractFloat(vm.PopStack());
                Vector3 target = ExtractVector3(vm.PopStack(), vm);
                Vector3 current = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.MoveTowards(current, target, maxDelta)));
            }, scope, "current", "target", "maxDistanceDelta");

            RegisterIntrinsic(vec3, "project", 2, (vm, _) =>
            {
                Vector3 onNormal = ExtractVector3(vm.PopStack(), vm);
                Vector3 vector = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Project(vector, onNormal)));
            }, scope, "vector", "onNormal");

            RegisterIntrinsic(vec3, "project_on_plane", 2, (vm, _) =>
            {
                Vector3 planeNormal = ExtractVector3(vm.PopStack(), vm);
                Vector3 vector = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.ProjectOnPlane(vector, planeNormal)));
            }, scope, "vector", "planeNormal");

            RegisterIntrinsic(vec3, "reflect", 2, (vm, _) =>
            {
                Vector3 normal = ExtractVector3(vm.PopStack(), vm);
                Vector3 dir = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Reflect(dir, normal)));
            }, scope, "direction", "normal");

            RegisterIntrinsic(vec3, "scale", 2, (vm, _) =>
            {
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Scale(a, b)));
            }, scope, "a", "b");

            RegisterIntrinsic(vec3, "min", 2, (vm, _) =>
            {
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Min(a, b)));
            }, scope, "a", "b");

            RegisterIntrinsic(vec3, "max", 2, (vm, _) =>
            {
                Vector3 b = ExtractVector3(vm.PopStack(), vm);
                Vector3 a = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.Max(a, b)));
            }, scope, "a", "b");

            RegisterIntrinsic(vec3, "clamp_magnitude", 2, (vm, _) =>
            {
                float maxLength = ExtractFloat(vm.PopStack());
                Vector3 vector = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(Vector3.ClampMagnitude(vector, maxLength)));
            }, scope, "vector", "maxLength");

            RegisterIntrinsic(vec3, "normalize", 1, (vm, _) =>
            {
                Vector3 v = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Create(v.normalized));
            }, scope, "vector");
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
            {
                new FunctionSymbol("Vector3__0", 0, (vm, _) => new RuntimeValue(Create(Vector3.zero)), scope, new List<string>()),

                new FunctionSymbol("Vector3__2", 2, (vm, _) => {
                    float y = (float)vm.PopStack().DoubleValue;
                    float x = (float)vm.PopStack().DoubleValue;
                    return new RuntimeValue(Create(new Vector3(x, y, 0)));
                }, scope, new List<string>() {"x", "y"}),

                new FunctionSymbol("Vector3__3", 3, (vm, _) => {
                    float z = (float)vm.PopStack().DoubleValue;
                    float y = (float)vm.PopStack().DoubleValue;
                    float x = (float)vm.PopStack().DoubleValue;
                    return new RuntimeValue(Create(new Vector3(x, y, z)));
                }, scope, new List<string>() {"x", "y", "z"})
            };
        }
    }
}