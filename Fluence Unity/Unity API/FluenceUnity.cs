using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using UnityEngine;
using static Fluence.Unity.FluenceInterpreter;

namespace Fluence.Unity
{
    public sealed class FluenceUnity
    {
        internal const string NamespaceName = "Unity";

        internal static void Register(FluenceScope unityNamespace, TextOutputMethod outputLine, TextInputMethod input, TextOutputMethod errorOutput)
        {
            // Core Utility Classes.
            RegisterUnityDebugClass(unityNamespace);
            RegisterUnityTimeClass(unityNamespace);
            RegisterUnityInputClass(unityNamespace);
            RegisterUnityPhysicsClass(unityNamespace);
            RegisterUnityMathClass(unityNamespace);

            // Wrappers & Structs.
            RegisterVector3Class(unityNamespace);
            RegisterQuaternionClass(unityNamespace);
            RegisterGameObjectClass(unityNamespace);
            RegisterTransformClass(unityNamespace);
            RegisterCameraClass(unityNamespace);
            RegisterRigidbodyClass(unityNamespace);
            RegisterColliderClass(unityNamespace);
        }

        /// <summary>
        /// Helper to cleanly register an intrinsic method to a StructSymbol.
        /// Automatically formats the symbol name as "name__arity".
        /// </summary>
        internal static void RegisterIntrinsic(
            StructSymbol structSymbol,
            string name,
            int arity,
            IntrinsicMethod callback,
            FluenceScope scope,
            params string[] parameterNames)
        {
            string symbolName = $"{name}__{arity}";
            var func = new FunctionSymbol(symbolName, arity, callback, scope, new List<string>(parameterNames));
            structSymbol.StaticIntrinsics.Add(symbolName, func);
        }

        /// <summary>
        /// Helper to cleanly register a static field to a StructSymbol.
        /// </summary>
        internal static void RegisterField(StructSymbol structSymbol, string name, RuntimeValue value)
        {
            structSymbol.StaticFields.Add(name, value);
        }

        #region Core Classes

        private static void RegisterUnityDebugClass(FluenceScope scope)
        {
            var debug = new StructSymbol("Debug", scope);
            scope.Declare("Debug".GetHashCode(), debug);

            RegisterIntrinsic(debug, "log", 1, (vm, _) =>
            {
                Debug.Log($"[Fluence] {vm.PopStack()}");
                return RuntimeValue.Nil;
            }, scope, "message");

            RegisterIntrinsic(debug, "log_warning", 1, (vm, _) =>
            {
                Debug.LogWarning($"[Fluence] {vm.PopStack()}");
                return RuntimeValue.Nil;
            }, scope, "message");

            RegisterIntrinsic(debug, "log_error", 1, (vm, _) =>
            {
                Debug.LogError($"[Fluence] {vm.PopStack()}");
                return RuntimeValue.Nil;
            }, scope, "message");

            RegisterIntrinsic(debug, "assert", 1, (vm, _) =>
            {
                Debug.Assert(vm.PopStack().IsTruthy, "[Fluence] Assertion failed!");
                return RuntimeValue.Nil;
            }, scope, "condition");

            RegisterIntrinsic(debug, "assert", 2, (vm, _) =>
            {
                var msg = vm.PopStack();
                Debug.Assert(vm.PopStack().IsTruthy, $"[Fluence] {msg}");
                return RuntimeValue.Nil;
            }, scope, "condition", "message");

            RegisterIntrinsic(debug, "break", 0, (vm, _) =>
            {
                Debug.Break();
                return RuntimeValue.Nil;
            }, scope);

            RegisterField(debug, "is_debug_build", new RuntimeValue(Debug.isDebugBuild));

            RegisterIntrinsic(debug, "draw_line", 2, (vm, _) =>
            {
                var end = ExtractVector3(vm.PopStack(), vm);
                var start = ExtractVector3(vm.PopStack(), vm);
                Debug.DrawLine(start, end, Color.white, 0.0f, true);
                return RuntimeValue.Nil;
            }, scope, "start", "end");

            RegisterIntrinsic(debug, "draw_ray", 2, (vm, _) =>
            {
                var dir = ExtractVector3(vm.PopStack(), vm);
                var start = ExtractVector3(vm.PopStack(), vm);
                Debug.DrawRay(start, dir, Color.white, 0.0f, true);
                return RuntimeValue.Nil;
            }, scope, "start", "dir");
        }

        private static void RegisterUnityTimeClass(FluenceScope scope)
        {
            var time = new StructSymbol("Time", scope);
            scope.Declare("Time".GetHashCode(), time);

            RegisterIntrinsic(time, "time", 0, (vm, _) => new RuntimeValue(Time.time), scope);
            RegisterIntrinsic(time, "delta_time", 0, (vm, _) => new RuntimeValue(Time.deltaTime), scope);
            RegisterIntrinsic(time, "fixed_time", 0, (vm, _) => new RuntimeValue(Time.fixedTime), scope);
            RegisterIntrinsic(time, "fixed_delta_time", 0, (vm, _) => new RuntimeValue(Time.fixedDeltaTime), scope);
            RegisterIntrinsic(time, "unscaled_time", 0, (vm, _) => new RuntimeValue(Time.unscaledTime), scope);
            RegisterIntrinsic(time, "unscaled_delta_time", 0, (vm, _) => new RuntimeValue(Time.unscaledDeltaTime), scope);
            RegisterIntrinsic(time, "smooth_delta_time", 0, (vm, _) => new RuntimeValue(Time.smoothDeltaTime), scope);
            RegisterIntrinsic(time, "time_scale", 0, (vm, _) => new RuntimeValue(Time.timeScale), scope);
            RegisterIntrinsic(time, "frame_count", 0, (vm, _) => new RuntimeValue(Time.frameCount), scope);
            RegisterIntrinsic(time, "realtime_since_startup", 0, (vm, _) => new RuntimeValue(Time.realtimeSinceStartup), scope);

            RegisterIntrinsic(time, "set_time_scale", 1, (vm, _) =>
            {
                Time.timeScale = ExtractFloat(vm.PopStack());
                return RuntimeValue.Nil;
            }, scope, "scale");
        }

        private static void RegisterUnityInputClass(FluenceScope scope)
        {
            var input = new StructSymbol("Input", scope);
            scope.Declare("Input".GetHashCode(), input);

            RegisterIntrinsic(input, "get_axis", 1, (vm, _) => new RuntimeValue(Input.GetAxis(vm.PopStack().ToString())), scope, "axis");
            RegisterIntrinsic(input, "get_axis_raw", 1, (vm, _) => new RuntimeValue(Input.GetAxisRaw(vm.PopStack().ToString())), scope, "axis");

            RegisterIntrinsic(input, "get_button", 1, (vm, _) => new RuntimeValue(Input.GetButton(vm.PopStack().ToString())), scope, "button");
            RegisterIntrinsic(input, "get_button_down", 1, (vm, _) => new RuntimeValue(Input.GetButtonDown(vm.PopStack().ToString())), scope, "button");
            RegisterIntrinsic(input, "get_button_up", 1, (vm, _) => new RuntimeValue(Input.GetButtonUp(vm.PopStack().ToString())), scope, "button");

            RegisterIntrinsic(input, "get_key", 1, (vm, _) =>
            {
                return Enum.TryParse(vm.PopStack().ToString(), true, out KeyCode key)
                    ? new RuntimeValue(Input.GetKey(key))
                    : new RuntimeValue(false);
            }, scope, "key");

            RegisterIntrinsic(input, "get_key_down", 1, (vm, _) =>
            {
                return Enum.TryParse(vm.PopStack().ToString(), true, out KeyCode key)
                    ? new RuntimeValue(Input.GetKeyDown(key))
                    : new RuntimeValue(false);
            }, scope, "key");

            RegisterIntrinsic(input, "get_key_up", 1, (vm, _) =>
            {
                return Enum.TryParse(vm.PopStack().ToString(), true, out KeyCode key)
                    ? new RuntimeValue(Input.GetKeyUp(key))
                    : new RuntimeValue(false);
            }, scope, "key");

            RegisterIntrinsic(input, "get_mouse_button", 1, (vm, _) => new RuntimeValue(Input.GetMouseButton(vm.PopStack().IntValue)), scope, "button");
            RegisterIntrinsic(input, "get_mouse_button_down", 1, (vm, _) => new RuntimeValue(Input.GetMouseButtonDown(vm.PopStack().IntValue)), scope, "button");
            RegisterIntrinsic(input, "get_mouse_button_up", 1, (vm, _) => new RuntimeValue(Input.GetMouseButtonUp(vm.PopStack().IntValue)), scope, "button");

            RegisterIntrinsic(input, "get_mouse_position", 0, (vm, _) => new RuntimeValue(Vector3Wrapper.Create(Input.mousePosition)), scope);
            RegisterIntrinsic(input, "mouse_present", 0, (vm, _) => new RuntimeValue(Input.mousePresent), scope);

            RegisterIntrinsic(input, "any_key", 0, (vm, _) => new RuntimeValue(Input.anyKey), scope);
            RegisterIntrinsic(input, "any_key_down", 0, (vm, _) => new RuntimeValue(Input.anyKeyDown), scope);
        }

        /// <summary>
        /// Registers a single-float-argument math function.
        /// </summary>
        private static void RegisterMathFunc1(
            StructSymbol math,
            string name,
            Func<float, float> func,
            FluenceScope scope,
            string paramName = "f")
        {
            RegisterIntrinsic(math, name, 1, (vm, _) =>
                new RuntimeValue(func(ExtractFloat(vm.PopStack()))),
                scope, paramName);
        }

        /// <summary>
        /// Registers a two-float-argument math function.
        /// </summary>
        private static void RegisterMathFunc2(
            StructSymbol math,
            string name,
            Func<float, float, float> func,
            FluenceScope scope,
            string paramA = "a",
            string paramB = "b")
        {
            RegisterIntrinsic(math, name, 2, (vm, _) =>
            {
                float b = ExtractFloat(vm.PopStack());
                float a = ExtractFloat(vm.PopStack());
                return new RuntimeValue(func(a, b));
            }, scope, paramA, paramB);
        }

        /// <summary>
        /// Registers a three-float-argument math function.
        /// </summary>
        private static void RegisterMathFunc3(
            StructSymbol math,
            string name,
            Func<float, float, float, float> func,
            FluenceScope scope,
            string paramA = "a",
            string paramB = "b",
            string paramC = "c")
        {
            RegisterIntrinsic(math, name, 3, (vm, _) =>
            {
                float c = ExtractFloat(vm.PopStack());
                float b = ExtractFloat(vm.PopStack());
                float a = ExtractFloat(vm.PopStack());
                return new RuntimeValue(func(a, b, c));
            }, scope, paramA, paramB, paramC);
        }

        private static void RegisterUnityPhysicsClass(FluenceScope scope)
        {
            var physics = new StructSymbol("Physics", scope);
            scope.Declare("Physics".GetHashCode(), physics);

            var forceMode = new StructSymbol("ForceMode", scope);
            scope.Declare("ForceMode".GetHashCode(), forceMode);

            RegisterField(forceMode, "Force", new RuntimeValue((double)ForceMode.Force));
            RegisterField(forceMode, "Impulse", new RuntimeValue((double)ForceMode.Impulse));
            RegisterField(forceMode, "VelocityChange", new RuntimeValue((double)ForceMode.VelocityChange));
            RegisterField(forceMode, "Acceleration", new RuntimeValue((double)ForceMode.Acceleration));

            RegisterIntrinsic(physics, "get_gravity", 0, (vm, _) =>
                new RuntimeValue(Vector3Wrapper.Create(Physics.gravity)), scope);

            RegisterIntrinsic(physics, "set_gravity", 1, (vm, _) =>
            {
                Physics.gravity = ExtractVector3(vm.PopStack(), vm);
                return RuntimeValue.Nil;
            }, scope, "gravity");

            RegisterIntrinsic(physics, "raycast", 4, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                float maxDist = ExtractFloat(vm.PopStack());
                Vector3 dir = ExtractVector3(vm.PopStack(), vm);
                Vector3 origin = ExtractVector3(vm.PopStack(), vm);

                if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, layerMask))
                    return new RuntimeValue(RaycastHitWrapper.Create(hit));
                return RuntimeValue.Nil;
            }, scope, "origin", "direction", "maxDistance", "layerMask");

            RegisterIntrinsic(physics, "sphere_cast", 5, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                float maxDist = ExtractFloat(vm.PopStack());
                Vector3 dir = ExtractVector3(vm.PopStack(), vm);
                float radius = ExtractFloat(vm.PopStack());
                Vector3 origin = ExtractVector3(vm.PopStack(), vm);

                if (Physics.SphereCast(origin, radius, dir, out RaycastHit hit, maxDist, layerMask))
                    return new RuntimeValue(RaycastHitWrapper.Create(hit));
                return RuntimeValue.Nil;
            }, scope, "origin", "radius", "direction", "maxDistance", "layerMask");

            RegisterIntrinsic(physics, "capsule_cast", 6, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                float maxDist = ExtractFloat(vm.PopStack());
                Vector3 dir = ExtractVector3(vm.PopStack(), vm);
                float radius = ExtractFloat(vm.PopStack());
                Vector3 point1 = ExtractVector3(vm.PopStack(), vm);
                Vector3 point0 = ExtractVector3(vm.PopStack(), vm);

                if (Physics.CapsuleCast(point0, point1, radius, dir, out RaycastHit hit, maxDist, layerMask))
                    return new RuntimeValue(RaycastHitWrapper.Create(hit));
                return RuntimeValue.Nil;
            }, scope, "point1", "point2", "radius", "direction", "maxDistance", "layerMask");

            RegisterIntrinsic(physics, "linecast", 4, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                Vector3 end = ExtractVector3(vm.PopStack(), vm);
                Vector3 start = ExtractVector3(vm.PopStack(), vm);

                if (Physics.Linecast(start, end, out RaycastHit hit, layerMask))
                    return new RuntimeValue(RaycastHitWrapper.Create(hit));
                return RuntimeValue.Nil;
            }, scope, "start", "end", "layerMask");

            RegisterIntrinsic(physics, "overlap_sphere", 3, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                float radius = ExtractFloat(vm.PopStack());
                Vector3 pos = ExtractVector3(vm.PopStack(), vm);

                Collider[] hits = Physics.OverlapSphere(pos, radius, layerMask);
                var list = new ListObject();
                if (hits != null)
                    foreach (var hit in hits)
                        list.Elements.Add(new RuntimeValue(ColliderWrapper.Create(hit)));
                return new RuntimeValue(list);
            }, scope, "position", "radius", "layerMask");

            RegisterIntrinsic(physics, "overlap_box", 4, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                Vector3 halfExtents = ExtractVector3(vm.PopStack(), vm);
                Quaternion rotation = ExtractQuaternion(vm.PopStack(), vm);
                Vector3 center = ExtractVector3(vm.PopStack(), vm);

                Collider[] hits = Physics.OverlapBox(center, halfExtents, rotation, layerMask);
                var list = new ListObject();
                if (hits != null)
                    foreach (var hit in hits)
                        list.Elements.Add(new RuntimeValue(ColliderWrapper.Create(hit)));
                return new RuntimeValue(list);
            }, scope, "center", "rotation", "halfExtents", "layerMask");

            RegisterIntrinsic(physics, "check_sphere", 3, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                float radius = ExtractFloat(vm.PopStack());
                Vector3 pos = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Physics.CheckSphere(pos, radius, layerMask));
            }, scope, "position", "radius", "layerMask");

            RegisterIntrinsic(physics, "check_box", 3, (vm, _) =>
            {
                int layerMask = vm.PopStack().IntValue;
                Vector3 halfExtents = ExtractVector3(vm.PopStack(), vm);
                Vector3 center = ExtractVector3(vm.PopStack(), vm);
                return new RuntimeValue(Physics.CheckBox(center, halfExtents, Quaternion.identity, layerMask));
            }, scope, "center", "halfExtents", "layerMask");

            RegisterIntrinsic(physics, "ignore_collision", 2, (vm, _) =>
            {
                var col2 = vm.PopStack().As<Wrapper>().Instance as Collider;
                var col1 = vm.PopStack().As<Wrapper>().Instance as Collider;
                if (col1 != null && col2 != null)
                    Physics.IgnoreCollision(col1, col2);
                return RuntimeValue.Nil;
            }, scope, "collider1", "collider2");

            RegisterIntrinsic(physics, "ignore_layer_collision", 3, (vm, _) =>
            {
                bool ignore = vm.PopStack().IsTruthy;
                int layer2 = vm.PopStack().IntValue;
                int layer1 = vm.PopStack().IntValue;
                Physics.IgnoreLayerCollision(layer1, layer2, ignore);
                return RuntimeValue.Nil;
            }, scope, "layer1", "layer2", "ignore");
        }

        private static void RegisterUnityMathClass(FluenceScope scope)
        {
            var math = new StructSymbol("Mathf", scope);
            scope.Declare("Mathf".GetHashCode(), math);

            RegisterField(math, "pi", new RuntimeValue(Mathf.PI));
            RegisterField(math, "deg2_rad", new RuntimeValue(Mathf.Deg2Rad));
            RegisterField(math, "rad2_deg", new RuntimeValue(Mathf.Rad2Deg));
            RegisterField(math, "epsilon", new RuntimeValue(Mathf.Epsilon));
            RegisterField(math, "infinity", new RuntimeValue(Mathf.Infinity));

            RegisterMathFunc1(math, "sin", Mathf.Sin, scope);
            RegisterMathFunc1(math, "cos", Mathf.Cos, scope);
            RegisterMathFunc1(math, "tan", Mathf.Tan, scope);
            RegisterMathFunc1(math, "asin", Mathf.Asin, scope);
            RegisterMathFunc1(math, "acos", Mathf.Acos, scope);
            RegisterMathFunc1(math, "atan", Mathf.Atan, scope);
            RegisterMathFunc2(math, "atan2", Mathf.Atan2, scope, "y", "x");

            RegisterMathFunc1(math, "sqrt", Mathf.Sqrt, scope);
            RegisterMathFunc2(math, "pow", Mathf.Pow, scope, "base", "exponent");
            RegisterMathFunc1(math, "exp", Mathf.Exp, scope);
            RegisterMathFunc1(math, "log", Mathf.Log, scope);
            RegisterMathFunc1(math, "log10", Mathf.Log10, scope);

            RegisterMathFunc1(math, "round", Mathf.Round, scope);
            RegisterMathFunc1(math, "floor", Mathf.Floor, scope);
            RegisterMathFunc1(math, "ceil", Mathf.Ceil, scope);

            RegisterMathFunc1(math, "abs", Mathf.Abs, scope);
            RegisterMathFunc1(math, "sign", Mathf.Sign, scope);

            RegisterMathFunc2(math, "min", Mathf.Min, scope);
            RegisterMathFunc2(math, "max", Mathf.Max, scope);

            RegisterMathFunc3(math, "clamp", Mathf.Clamp, scope, "value", "min", "max");
            RegisterMathFunc1(math, "clamp01", Mathf.Clamp01, scope, "value");

            RegisterMathFunc3(math, "lerp", Mathf.Lerp, scope, "a", "b", "t");
            RegisterMathFunc3(math, "lerp_angle", Mathf.LerpAngle, scope, "a", "b", "t");
            RegisterMathFunc3(math, "inverse_lerp", Mathf.InverseLerp, scope, "a", "b", "value");
            RegisterMathFunc3(math, "smooth_step", Mathf.SmoothStep, scope, "from", "to", "t");

            RegisterMathFunc3(math, "move_towards", Mathf.MoveTowards, scope, "current", "target", "maxDelta");
            RegisterMathFunc3(math, "move_towards_angle", Mathf.MoveTowardsAngle, scope, "current", "target", "maxDelta");

            RegisterMathFunc2(math, "repeat", Mathf.Repeat, scope, "t", "length");
            RegisterMathFunc2(math, "ping_pong", Mathf.PingPong, scope, "t", "length");
            RegisterMathFunc2(math, "delta_angle", Mathf.DeltaAngle, scope, "current", "target");
        }

        #endregion

        #region Class Registrations

        private static void RegisterVector3Class(FluenceScope unityNamespace)
        {
            StructSymbol vec3Static = new StructSymbol("Vector3", unityNamespace);
            unityNamespace.Declare("Vector3".GetHashCode(), vec3Static);

            // Constructors are declared in the namespace scope, not the struct scope.
            foreach (FunctionSymbol ctor in Vector3Wrapper.CreateConstructors(unityNamespace))
                unityNamespace.Declare(ctor.Hash, ctor);

            Vector3Wrapper.RegisterStatics(vec3Static, unityNamespace);
        }

        private static void RegisterQuaternionClass(FluenceScope unityNamespace)
        {
            StructSymbol quatStatic = new StructSymbol("Quaternion", unityNamespace);
            unityNamespace.Declare("Quaternion".GetHashCode(), quatStatic);

            foreach (FunctionSymbol ctor in QuaternionWrapper.CreateConstructors(unityNamespace))
                unityNamespace.Declare(ctor.Hash, ctor);

            QuaternionWrapper.RegisterStatics(quatStatic, unityNamespace);
        }

        private static void RegisterGameObjectClass(FluenceScope unityNamespace)
        {
            StructSymbol goStatic = new StructSymbol("GameObject", unityNamespace);
            unityNamespace.Declare("GameObject".GetHashCode(), goStatic);

            foreach (FunctionSymbol ctor in GameObjectWrapper.CreateConstructors(unityNamespace))
                unityNamespace.Declare(ctor.Hash, ctor);

            GameObjectWrapper.RegisterStatics(goStatic, unityNamespace);
        }

        private static void RegisterTransformClass(FluenceScope unityNamespace)
        {
            StructSymbol tStatic = new StructSymbol("Transform", unityNamespace);
            unityNamespace.Declare("Transform".GetHashCode(), tStatic);
            // Transform has no public constructors exposed to the VM.
        }

        private static void RegisterCameraClass(FluenceScope unityNamespace)
        {
            StructSymbol camStatic = new StructSymbol("Camera", unityNamespace);
            unityNamespace.Declare("Camera".GetHashCode(), camStatic);
            CameraWrapper.RegisterStatics(camStatic, unityNamespace);
        }

        private static void RegisterRigidbodyClass(FluenceScope unityNamespace)
        {
            StructSymbol rbStatic = new StructSymbol("Rigidbody", unityNamespace);
            unityNamespace.Declare("Rigidbody".GetHashCode(), rbStatic);
        }

        private static void RegisterColliderClass(FluenceScope unityNamespace)
        {
            StructSymbol colStatic = new StructSymbol("Collider", unityNamespace);
            unityNamespace.Declare("Collider".GetHashCode(), colStatic);
        }

        #endregion

        #region Extractors

        internal static Vector3 ExtractVector3(RuntimeValue value, FluenceVirtualMachine vm)
        {
            if (value.ObjectReference is Wrapper wrapper && wrapper.Instance is Vector3 vec) return vec;
            if (value.ObjectReference is ListObject list && list.Elements.Count >= 3)
            {
                return new Vector3((float)list.Elements[0].DoubleValue, (float)list.Elements[1].DoubleValue, (float)list.Elements[2].DoubleValue);
            }
            vm?.SignalError<object>($"Expected Vector3 object or [x, y, z] list, but got: {FluenceVirtualMachine.GetDetailedTypeName(value)}");
            return Vector3.zero;
        }

        internal static Quaternion ExtractQuaternion(RuntimeValue value, FluenceVirtualMachine vm)
        {
            if (value.ObjectReference is Wrapper wrapper && wrapper.Instance is Quaternion rot) return rot;
            if (value.ObjectReference is ListObject list && list.Elements.Count >= 4)
            {
                return new Quaternion((float)list.Elements[0].DoubleValue, (float)list.Elements[1].DoubleValue, (float)list.Elements[2].DoubleValue, (float)list.Elements[3].DoubleValue);
            }
            vm?.SignalError<object>($"Expected Quaternion object or [x, y, z, w] list, but got: {FluenceVirtualMachine.GetDetailedTypeName(value)}");
            return Quaternion.identity;
        }

        internal static Vector2 ExtractVector2(RuntimeValue value, FluenceVirtualMachine vm)
        {
            if (value.ObjectReference is Wrapper wrapper && wrapper.Instance is Vector2 vec) return vec;
            if (value.ObjectReference is ListObject list && list.Elements.Count >= 2)
            {
                return new Vector2((float)list.Elements[0].DoubleValue, (float)list.Elements[1].DoubleValue);
            }
            vm?.SignalError<object>($"Expected Vector2 object or [x, y] list, but got: {FluenceVirtualMachine.GetDetailedTypeName(value)}");
            return Vector2.zero;
        }

        internal static Color ExtractColor(RuntimeValue value, FluenceVirtualMachine vm)
        {
            if (value.ObjectReference is Wrapper wrapper && wrapper.Instance is Color col) return col;
            if (value.ObjectReference is ListObject list && list.Elements.Count >= 3)
            {
                float r = (float)list.Elements[0].DoubleValue;
                float g = (float)list.Elements[1].DoubleValue;
                float b = (float)list.Elements[2].DoubleValue;
                float a = list.Elements.Count > 3 ? (float)list.Elements[3].DoubleValue : 1.0f;
                return new Color(r, g, b, a);
            }
            vm?.SignalError<object>($"Expected Color object or [r, g, b, a] list, but got: {FluenceVirtualMachine.GetDetailedTypeName(value)}");
            return Color.white;
        }

        internal static float ExtractFloat(RuntimeValue val)
        {
            if (val.Type != RuntimeValueType.Number) return 0f;
            return val.NumberType switch
            {
                RuntimeNumberType.Int => val.IntValue,
                RuntimeNumberType.Long => val.LongValue,
                RuntimeNumberType.Double => (float)val.DoubleValue,
                _ => val.FloatValue
            };
        }

        #endregion
    }
}