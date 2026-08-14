using Fluence.Unity.RuntimeTypes;
using UnityEngine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class ColliderWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static ColliderWrapper()
        {
            _getters["isTrigger"] = w => new RuntimeValue(((Collider)w.Instance).isTrigger);
            _getters["gameObject"] = w => new RuntimeValue(GameObjectWrapper.Create(((Collider)w.Instance).gameObject));
            _getters["transform"] = w => new RuntimeValue(TransformWrapper.Create(((Collider)w.Instance).transform));
            _getters["attachedRigidbody"] = w => new RuntimeValue(RigidbodyWrapper.Create(((Collider)w.Instance).attachedRigidbody));
            _getters["enabled"] = w => new RuntimeValue(((Collider)w.Instance).enabled);

            _getters["radius"] = w =>
            {
                if (w.Instance is SphereCollider sc) return new RuntimeValue(sc.radius);
                if (w.Instance is CapsuleCollider cc) return new RuntimeValue(cc.radius);
                return new RuntimeValue(0.0);
            };

            _getters["height"] = w =>
            {
                if (w.Instance is CapsuleCollider cc) return new RuntimeValue(cc.height);
                return new RuntimeValue(0.0);
            };

            _setters["isTrigger"] = (w, val) => ((Collider)w.Instance).isTrigger = val.IsTruthy;
            _setters["enabled"] = (w, val) => ((Collider)w.Instance).enabled = val.IsTruthy;

            _setters["radius"] = (w, val) =>
            {
                float rad = FluenceUnity.ExtractFloat(val);
                if (w.Instance is SphereCollider sc) sc.radius = rad;
                else if (w.Instance is CapsuleCollider cc) cc.radius = rad;
            };

            _setters["height"] = (w, val) =>
            {
                float h = FluenceUnity.ExtractFloat(val);
                if (w.Instance is CapsuleCollider cc) cc.height = h;
            };

            _methods["closest_point__1"] = (vm, self) =>
            {
                Vector3 target = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
                Collider col = (Collider)self.As<Wrapper>().Instance;

                return new RuntimeValue(Vector3Wrapper.Create(col.ClosestPoint(target)));
            };
        }

        internal static Wrapper Create(Collider collider) => collider != null ? new Wrapper(collider, _methods, _getters, _setters) : null!;
    }
}