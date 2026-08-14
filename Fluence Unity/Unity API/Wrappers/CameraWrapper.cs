using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using UnityEngine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class CameraWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _instanceMethods = new Dictionary<string, IntrinsicRuntimeMethod>();
        private static readonly Dictionary<string, System.Func<Wrapper, RuntimeValue>> _getters = new Dictionary<string, System.Func<Wrapper, RuntimeValue>>();
        private static readonly Dictionary<string, System.Action<Wrapper, RuntimeValue>> _setters = new Dictionary<string, System.Action<Wrapper, RuntimeValue>>();

        static CameraWrapper()
        {
            _instanceMethods["screen_to_world_point__1"] = ScreenToWorldPoint;
            _instanceMethods["world_to_screen_point__1"] = WorldToScreenPoint;
            _instanceMethods["screen_to_viewport_point__1"] = ScreenToViewportPoint;
            _instanceMethods["viewport_to_screen_point__1"] = ViewportToScreenPoint;
            _instanceMethods["viewport_to_world_point__1"] = ViewportToWorldPoint;
            _instanceMethods["world_to_viewport_point__1"] = WorldToViewportPoint;
            _instanceMethods["get_transform__0"] = GetTransform;

            _getters["transform"] = w => new RuntimeValue(TransformWrapper.Create(((Camera)w.Instance).transform));

            _getters["field_of_view"] = w => new RuntimeValue(((Camera)w.Instance).fieldOfView);
            _setters["field_of_view"] = (w, v) => ((Camera)w.Instance).fieldOfView = (float)v.DoubleValue;

            _getters["orthographic"] = w => new RuntimeValue(((Camera)w.Instance).orthographic);
            _setters["orthographic"] = (w, v) => ((Camera)w.Instance).orthographic = v.IsTruthy;

            _getters["orthographic_size"] = w => new RuntimeValue(((Camera)w.Instance).orthographicSize);
            _setters["orthographic_size"] = (w, v) => ((Camera)w.Instance).orthographicSize = (float)v.DoubleValue;

            _getters["aspect"] = w => new RuntimeValue(((Camera)w.Instance).aspect);
            _setters["aspect"] = (w, v) => ((Camera)w.Instance).aspect = (float)v.DoubleValue;

            _getters["near_clip_plane"] = w => new RuntimeValue(((Camera)w.Instance).nearClipPlane);
            _setters["near_clip_plane"] = (w, v) => ((Camera)w.Instance).nearClipPlane = (float)v.DoubleValue;

            _getters["far_clip_plane"] = w => new RuntimeValue(((Camera)w.Instance).farClipPlane);
            _setters["far_clip_plane"] = (w, v) => ((Camera)w.Instance).farClipPlane = (float)v.DoubleValue;

            _getters["fieldOfView"] = w => new RuntimeValue(((Camera)w.Instance).fieldOfView);
            _setters["fieldOfView"] = (w, v) => ((Camera)w.Instance).fieldOfView = (float)v.DoubleValue;
        }

        internal static void RegisterStatics(StructSymbol camStatic, FluenceScope scope)
        {
            camStatic.StaticIntrinsics.Add("get_main__0", new FunctionSymbol("get_main__0", 0, (vm, count) => Camera.main != null ? new RuntimeValue(Create(Camera.main)) : RuntimeValue.Nil, scope, new List<string>()));
            camStatic.StaticIntrinsics.Add("get_current__0", new FunctionSymbol("get_current__0", 0, (vm, count) => Camera.current != null ? new RuntimeValue(Create(Camera.current)) : RuntimeValue.Nil, scope, new List<string>()));
        }

        public static Wrapper Create(Camera cam)
        {
            return new Wrapper(cam, _instanceMethods, _getters, _setters);
        }

        public static FunctionSymbol[] CreateConstructors(FluenceScope scope)
        {
            return new FunctionSymbol[]
            {
                new FunctionSymbol("Camera_get_main__0", 0, (vm, count) =>
                {
                    if (Camera.main != null) return new RuntimeValue(Create(Camera.main));
                    return RuntimeValue.Nil;
                }, scope, new List<string>()),

                new FunctionSymbol("Camera_get_current__0", 0, (vm, count) =>
                {
                    if (Camera.current != null) return new RuntimeValue(Create(Camera.current));
                    return RuntimeValue.Nil;
                }, scope, new List<string>())
            };
        }

        private static RuntimeValue GetTransform(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(TransformWrapper.Create(cam.transform));
        }

        private static RuntimeValue ScreenToWorldPoint(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(Vector3Wrapper.Create(cam.ScreenToWorldPoint(pos)));
        }

        private static RuntimeValue WorldToScreenPoint(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(Vector3Wrapper.Create(cam.WorldToScreenPoint(pos)));
        }

        private static RuntimeValue ScreenToViewportPoint(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(Vector3Wrapper.Create(cam.ScreenToViewportPoint(pos)));
        }

        private static RuntimeValue ViewportToScreenPoint(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(Vector3Wrapper.Create(cam.ViewportToScreenPoint(pos)));
        }

        private static RuntimeValue ViewportToWorldPoint(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(Vector3Wrapper.Create(cam.ViewportToWorldPoint(pos)));
        }

        private static RuntimeValue WorldToViewportPoint(FluenceVirtualMachine vm, RuntimeValue self)
        {
            Vector3 pos = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
            Camera cam = (Camera)((Wrapper)self.ObjectReference).Instance;
            return new RuntimeValue(Vector3Wrapper.Create(cam.WorldToViewportPoint(pos)));
        }
    }
}