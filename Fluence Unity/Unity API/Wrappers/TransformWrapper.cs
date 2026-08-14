using Fluence.Unity.RuntimeTypes;
using UnityEngine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class TransformWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static TransformWrapper()
        {
            _getters["position"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).position));
            _getters["localPosition"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).localPosition));
            _getters["localRotation"] = w => new RuntimeValue(QuaternionWrapper.Create(((Transform)w.Instance).localRotation));
            _getters["eulerAngles"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).eulerAngles));
            _getters["localScale"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).localScale));
            _getters["rotation"] = w => new RuntimeValue(QuaternionWrapper.Create(((Transform)w.Instance).rotation));
            _getters["forward"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).forward));
            _getters["up"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).up));
            _getters["right"] = w => new RuntimeValue(Vector3Wrapper.Create(((Transform)w.Instance).right));
            _getters["gameObject"] = w => new RuntimeValue(GameObjectWrapper.Create(((Transform)w.Instance).gameObject));

            _setters["position"] = (w, val) => ((Transform)w.Instance).position = (Vector3)val.As<Wrapper>().Instance;
            _setters["eulerAngles"] = (w, val) => ((Transform)w.Instance).eulerAngles = (Vector3)val.As<Wrapper>().Instance;
            _setters["localScale"] = (w, val) => ((Transform)w.Instance).localScale = (Vector3)val.As<Wrapper>().Instance;
            _setters["rotation"] = (w, v) => ((Transform)w.Instance).rotation = (Quaternion)((Wrapper)v.ObjectReference).Instance;
            _setters["localPosition"] = (w, val) => ((Transform)w.Instance).localPosition = (Vector3)val.As<Wrapper>().Instance;
            _setters["localRotation"] = (w, val) => ((Transform)w.Instance).localRotation = (Quaternion)val.As<Wrapper>().Instance;

            _methods["translate__1"] = (vm, self) =>
            {
                Vector3 offset = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Transform)self.As<Wrapper>().Instance).Translate(offset);
                return RuntimeValue.Nil;
            };

            _methods["rotate__1"] = (vm, self) =>
            {
                Vector3 angles = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Transform)self.As<Wrapper>().Instance).Rotate(angles);
                return RuntimeValue.Nil;
            };

            _methods["rotate__2"] = (vm, self) =>
            {
                int relativeTo = (int)vm.PopStack().DoubleValue;
                Vector3 euler = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
                Transform t = (Transform)((Wrapper)self.ObjectReference).Instance;
                t.Rotate(euler, (Space)relativeTo);
                return self;
            };

            _methods["translate__3"] = (vm, self) =>
            {
                float z = (float)vm.PopStack().DoubleValue;
                float y = (float)vm.PopStack().DoubleValue;
                float x = (float)vm.PopStack().DoubleValue;

                Transform t = (Transform)((Wrapper)self.ObjectReference).Instance;
                t.Translate(x, y, z, Space.World);
                return self;
            };

            _methods["look_at__1"] = (vm, self) =>
            {
                RuntimeValue targetWrapper = vm.PopStack();
                Transform target = (Transform)((Wrapper)targetWrapper.ObjectReference).Instance;

                Transform t = (Transform)((Wrapper)self.ObjectReference).Instance;
                t.LookAt(target);
                return self;
            };

            _methods["set_parent__1"] = (vm, self) =>
            {
                RuntimeValue parentVal = vm.PopStack();
                Transform t = (Transform)((Wrapper)self.ObjectReference).Instance;

                if (parentVal.Type == RuntimeValueType.Nil)
                {
                    t.SetParent(null, true);
                }
                else
                {
                    Transform parentTransform = (Transform)((Wrapper)parentVal.ObjectReference).Instance;
                    t.SetParent(parentTransform, true);
                }

                return RuntimeValue.Nil;
            };

            _methods["get_parent__0"] = (vm, self) =>
            {
                Transform t = (Transform)((Wrapper)self.ObjectReference).Instance;
                if (t.parent != null) return new RuntimeValue(TransformWrapper.Create(t.parent));
                return RuntimeValue.Nil;
            };

            _methods["set_no_parent__0"] = (vm, self) =>
            {
                ((Transform)self.As<Wrapper>().Instance).SetParent(null);
                return RuntimeValue.Nil;
            };
        }

        internal static Wrapper Create(Transform transform) => transform != null ? new Wrapper(transform, _methods, _getters, _setters) : null!;
    }
}