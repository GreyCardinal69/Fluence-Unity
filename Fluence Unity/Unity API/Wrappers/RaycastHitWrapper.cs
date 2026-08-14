using Fluence.Unity.RuntimeTypes;
using UnityEngine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class RaycastHitWrapper
    {
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();

        static RaycastHitWrapper()
        {
            _getters["collider"] = w => { Collider c = ((RaycastHit)w.Instance).collider; return c != null ? new RuntimeValue(ColliderWrapper.Create(c)) : RuntimeValue.Nil; };
            _getters["transform"] = w => { Transform t = ((RaycastHit)w.Instance).transform; return t != null ? new RuntimeValue(TransformWrapper.Create(t)) : RuntimeValue.Nil; };
            _getters["rigidbody"] = w => { Rigidbody rb = ((RaycastHit)w.Instance).rigidbody; return rb != null ? new RuntimeValue(RigidbodyWrapper.Create(rb)) : RuntimeValue.Nil; };
            _getters["point"] = w => new RuntimeValue(Vector3Wrapper.Create(((RaycastHit)w.Instance).point));
            _getters["normal"] = w => new RuntimeValue(Vector3Wrapper.Create(((RaycastHit)w.Instance).normal));
            _getters["distance"] = w => new RuntimeValue((double)((RaycastHit)w.Instance).distance);
        }

        internal static Wrapper Create(RaycastHit hit) => new Wrapper(hit, new Dictionary<string, IntrinsicRuntimeMethod>(), _getters, new Dictionary<string, Action<Wrapper, RuntimeValue>>());
    }

    internal static class BoundsWrapper
    {
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static BoundsWrapper()
        {
            _getters["center"] = w => new RuntimeValue(Vector3Wrapper.Create(((Bounds)w.Instance).center));
            _getters["size"] = w => new RuntimeValue(Vector3Wrapper.Create(((Bounds)w.Instance).size));
            _getters["extents"] = w => new RuntimeValue(Vector3Wrapper.Create(((Bounds)w.Instance).extents));
            _getters["min"] = w => new RuntimeValue(Vector3Wrapper.Create(((Bounds)w.Instance).min));
            _getters["max"] = w => new RuntimeValue(Vector3Wrapper.Create(((Bounds)w.Instance).max));

            _setters["center"] = (w, val) => { Bounds b = (Bounds)w.Instance; b.center = FluenceUnity.ExtractVector3(val, null); w.Instance = b; };
            _setters["size"] = (w, val) => { Bounds b = (Bounds)w.Instance; b.size = FluenceUnity.ExtractVector3(val, null); w.Instance = b; };
            _setters["extents"] = (w, val) => { Bounds b = (Bounds)w.Instance; b.extents = FluenceUnity.ExtractVector3(val, null); w.Instance = b; };
        }

        internal static Wrapper Create(Bounds bounds) => new Wrapper(bounds, new Dictionary<string, IntrinsicRuntimeMethod>(), _getters, _setters);
    }

    internal static class RayWrapper
    {
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static RayWrapper()
        {
            _getters["origin"] = w => new RuntimeValue(Vector3Wrapper.Create(((Ray)w.Instance).origin));
            _getters["direction"] = w => new RuntimeValue(Vector3Wrapper.Create(((Ray)w.Instance).direction));

            _setters["origin"] = (w, val) => { Ray r = (Ray)w.Instance; r.origin = FluenceUnity.ExtractVector3(val, null); w.Instance = r; };
            _setters["direction"] = (w, val) => { Ray r = (Ray)w.Instance; r.direction = FluenceUnity.ExtractVector3(val, null); w.Instance = r; };
        }

        internal static Wrapper Create(Ray ray) => new Wrapper(ray, new Dictionary<string, IntrinsicRuntimeMethod>(), _getters, _setters);
    }
}