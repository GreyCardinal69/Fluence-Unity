using Fluence.Unity.RuntimeTypes;
using UnityEngine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;
using Object = UnityEngine.Object;

namespace Fluence.Unity
{
    internal static class GameObjectWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static GameObjectWrapper()
        {
            _getters["name"] = w => new RuntimeValue(((GameObject)w.Instance).name);
            _getters["tag"] = w => new RuntimeValue(((GameObject)w.Instance).tag);
            _getters["layer"] = w => new RuntimeValue(((GameObject)w.Instance).layer);
            _getters["active"] = w => new RuntimeValue(((GameObject)w.Instance).activeSelf);
            _getters["transform"] = w => new RuntimeValue(TransformWrapper.Create(((GameObject)w.Instance).transform));
            _getters["instance_id"] = w => new RuntimeValue(((GameObject)w.Instance).GetInstanceID());

            _setters["name"] = (w, val) => ((GameObject)w.Instance).name = val.ToString();
            _setters["tag"] = (w, val) => ((GameObject)w.Instance).tag = val.ToString();
            _setters["layer"] = (w, val) => ((GameObject)w.Instance).layer = (int)FluenceUnity.ExtractFloat(val);
            _setters["active"] = (w, val) => ((GameObject)w.Instance).SetActive(val.IsTruthy);

            _methods["destroy__0"] = (vm, self) => { UnityEngine.Object.Destroy((GameObject)self.As<Wrapper>().Instance); return RuntimeValue.Nil; };
            _methods["set_active__1"] = (vm, self) => { ((GameObject)self.As<Wrapper>().Instance).SetActive(vm.PopStack().IsTruthy); return RuntimeValue.Nil; };
            _methods["compare_tag__1"] = (vm, self) => new RuntimeValue(((GameObject)self.As<Wrapper>().Instance).CompareTag(vm.PopStack().ToString()));

            _methods["get_component__1"] = (vm, self) =>
            {
                string typeName = vm.PopStack().ToString().Trim().ToLowerInvariant();
                GameObject go = (GameObject)self.As<Wrapper>().Instance;
                Component comp = null;

                if (typeName == "rigidbody") comp = go.GetComponent<Rigidbody>();
                else if (typeName is "collider" or "boxcollider" or "spherecollider" or "capsulecollider" or "meshcollider") comp = go.GetComponent<Collider>();
                else if (typeName == "transform") comp = go.transform;
                else if (typeName == "camera") comp = go.GetComponent<Camera>();
                else comp = go.GetComponent(typeName);

                if (comp == null) return RuntimeValue.Nil;
                if (comp is Rigidbody rb) return new RuntimeValue(RigidbodyWrapper.Create(rb));
                if (comp is Collider col) return new RuntimeValue(ColliderWrapper.Create(col));
                if (comp is Transform t) return new RuntimeValue(TransformWrapper.Create(t));
                if (comp is Camera cam) return new RuntimeValue(CameraWrapper.Create(cam));
                return new RuntimeValue(new Wrapper(comp, new Dictionary<string, IntrinsicRuntimeMethod>()));
            };
        }

        internal static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
            {
                new FunctionSymbol("GameObject__0", 0, (vm, _) => new RuntimeValue(Create(new GameObject())), scope, new List<string>()),
                new FunctionSymbol("GameObject__1", 1, (vm, _) => new RuntimeValue(Create(new GameObject(vm.PopStack().ToString()))), scope, new List<string>() {"name"})
            };
        }

        internal static void RegisterStatics(StructSymbol go, FluenceScope scope)
        {
            FluenceUnity.RegisterIntrinsic(go, "find", 1, (vm, _) =>
            {
                string name = vm.PopStack().ToString();
                GameObject result = GameObject.Find(name);
                return result != null ? new RuntimeValue(Create(result)) : RuntimeValue.Nil;
            }, scope, "name");

            FluenceUnity.RegisterIntrinsic(go, "find_with_tag", 1, (vm, _) =>
            {
                string tag = vm.PopStack().ToString();
                GameObject result = GameObject.FindWithTag(tag);
                return result != null ? new RuntimeValue(Create(result)) : RuntimeValue.Nil;
            }, scope, "tag");

            FluenceUnity.RegisterIntrinsic(go, "find_game_objects_with_tag", 1, (vm, _) =>
            {
                string tag = vm.PopStack().ToString();
                GameObject[] results = GameObject.FindGameObjectsWithTag(tag);

                var list = new ListObject();
                if (results != null)
                    foreach (var obj in results)
                        list.Elements.Add(new RuntimeValue(Create(obj)));

                return new RuntimeValue(list);
            }, scope, "tag");

            FluenceUnity.RegisterIntrinsic(go, "instantiate", 1, (vm, _) =>
            {
                GameObject prefab = (GameObject)vm.PopStack().As<Wrapper>().Instance;
                return new RuntimeValue(Create(Object.Instantiate(prefab)));
            }, scope, "prefab");

            FluenceUnity.RegisterIntrinsic(go, "instantiate", 3, (vm, _) =>
            {
                Quaternion rot = FluenceUnity.ExtractQuaternion(vm.PopStack(), vm);
                Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
                GameObject prefab = (GameObject)vm.PopStack().As<Wrapper>().Instance;
                return new RuntimeValue(Create(Object.Instantiate(prefab, pos, rot)));
            }, scope, "prefab", "position", "rotation");

            FluenceUnity.RegisterIntrinsic(go, "instantiate", 4, (vm, _) =>
            {
                bool worldPos = vm.PopStack().IsTruthy;
                Transform parent = vm.PopStack().As<Wrapper>()?.Instance as Transform;
                GameObject prefab = (GameObject)vm.PopStack().As<Wrapper>().Instance;

                GameObject result = Object.Instantiate(prefab, parent, worldPos);
                return new RuntimeValue(Create(result));
            }, scope, "prefab", "parent", "instantiateInWorldSpace");

            FluenceUnity.RegisterIntrinsic(go, "destroy", 1, (vm, _) =>
            {
                var wrapper = vm.PopStack().As<Wrapper>();
                if (wrapper?.Instance is GameObject target)
                    Object.Destroy(target);
                return RuntimeValue.Nil;
            }, scope, "gameObject");

            FluenceUnity.RegisterIntrinsic(go, "destroy", 2, (vm, _) =>
            {
                float delay = FluenceUnity.ExtractFloat(vm.PopStack());
                var wrapper = vm.PopStack().As<Wrapper>();
                if (wrapper?.Instance is GameObject target)
                    Object.Destroy(target, delay);
                return RuntimeValue.Nil;
            }, scope, "gameObject", "delay");

            FluenceUnity.RegisterIntrinsic(go, "destroy_immediate", 1, (vm, _) =>
            {
                var wrapper = vm.PopStack().As<Wrapper>();
                if (wrapper?.Instance is GameObject target)
                    Object.DestroyImmediate(target);
                return RuntimeValue.Nil;
            }, scope, "gameObject");

            FluenceUnity.RegisterIntrinsic(go, "create_primitive", 1, (vm, _) =>
            {
                string typeStr = vm.PopStack().ToString().ToLowerInvariant();
                PrimitiveType type = typeStr switch
                {
                    "sphere" => PrimitiveType.Sphere,
                    "capsule" => PrimitiveType.Capsule,
                    "cylinder" => PrimitiveType.Cylinder,
                    "cube" => PrimitiveType.Cube,
                    "plane" => PrimitiveType.Plane,
                    "quad" => PrimitiveType.Quad,
                    _ => PrimitiveType.Cube
                };
                return new RuntimeValue(Create(GameObject.CreatePrimitive(type)));
            }, scope, "type");
        }

        internal static Wrapper Create(GameObject go) => go != null ? new Wrapper(go, _methods, _getters, _setters) : null!;
    }
}