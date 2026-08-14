using Fluence.Unity.RuntimeTypes;
using UnityEngine;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class RigidbodyWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static RigidbodyWrapper()
        {
#if UNITY_6000_0_OR_NEWER
            _getters["velocity"] = w => new RuntimeValue(Vector3Wrapper.Create(((Rigidbody)w.Instance).linearVelocity));
            _setters["velocity"] = (w, val) => ((Rigidbody)w.Instance).linearVelocity = (Vector3)val.As<Wrapper>().Instance;
#else
            _getters["velocity"] = w => new RuntimeValue(Vector3Wrapper.Create(((Rigidbody)w.Instance).velocity));
            _setters["velocity"] = (w, val) => ((Rigidbody)w.Instance).velocity = (Vector3)val.As<Wrapper>().Instance;
#endif
            _getters["angularVelocity"] = w => new RuntimeValue(Vector3Wrapper.Create(((Rigidbody)w.Instance).angularVelocity));
            _getters["mass"] = w => new RuntimeValue((double)((Rigidbody)w.Instance).mass);
            _getters["useGravity"] = w => new RuntimeValue(((Rigidbody)w.Instance).useGravity);
            _getters["isKinematic"] = w => new RuntimeValue(((Rigidbody)w.Instance).isKinematic);
            _getters["gameObject"] = w => new RuntimeValue(GameObjectWrapper.Create(((Rigidbody)w.Instance).gameObject));
            _getters["transform"] = w => new RuntimeValue(TransformWrapper.Create(((Rigidbody)w.Instance).transform));

            _setters["angularVelocity"] = (w, val) => ((Rigidbody)w.Instance).angularVelocity = (Vector3)val.As<Wrapper>().Instance;
            _setters["mass"] = (w, val) => ((Rigidbody)w.Instance).mass = (float)val.DoubleValue;
            _setters["useGravity"] = (w, val) => ((Rigidbody)w.Instance).useGravity = val.IsTruthy;
            _setters["isKinematic"] = (w, val) => ((Rigidbody)w.Instance).isKinematic = val.IsTruthy;

            _methods["add_force__1"] = (vm, self) =>
            {
                Vector3 force = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Rigidbody)self.As<Wrapper>().Instance).AddForce(force, ForceMode.Force);
                return RuntimeValue.Nil;
            };

            _methods["add_force__2"] = (vm, self) =>
            {
                int mode = (int)vm.PopStack().DoubleValue;
                Vector3 force = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Rigidbody)self.As<Wrapper>().Instance).AddForce(force, (ForceMode)mode);
                return RuntimeValue.Nil;
            };

            _methods["add_torque__1"] = (vm, self) =>
            {
                Vector3 torque = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Rigidbody)self.As<Wrapper>().Instance).AddTorque(torque, ForceMode.Force);
                return RuntimeValue.Nil;
            };

            _methods["get_point_velocity__1"] = (vm, self) =>
            {
                Vector3 point = FluenceUnity.ExtractVector3(vm.PopStack(), vm);
                Rigidbody rb = (Rigidbody)self.As<Wrapper>().Instance;

#if UNITY_6000_0_OR_NEWER
                return new RuntimeValue(Vector3Wrapper.Create(rb.GetPointVelocity(point)));
#else
                return new RuntimeValue(Vector3Wrapper.Create(rb.GetPointVelocity(point)));
#endif
            };

            _methods["add_torque__2"] = (vm, self) =>
            {
                int mode = (int)vm.PopStack().DoubleValue;
                Vector3 torque = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Rigidbody)self.As<Wrapper>().Instance).AddTorque(torque, (ForceMode)mode);
                return RuntimeValue.Nil;
            };

            _methods["get_velocity__0"] = (vm, self) =>
            {
#if UNITY_6000_0_OR_NEWER
                Vector3 vel = ((Rigidbody)self.As<Wrapper>().Instance).linearVelocity;
#else
                Vector3 vel = ((Rigidbody)self.As<Wrapper>().Instance).velocity;
#endif
                return new RuntimeValue(Vector3Wrapper.Create(vel));
            };

            _methods["set_velocity__1"] = (vm, self) =>
            {
                Vector3 vel = (Vector3)vm.PopStack().As<Wrapper>().Instance;
#if UNITY_6000_0_OR_NEWER
                ((Rigidbody)self.As<Wrapper>().Instance).linearVelocity = vel;
#else
                ((Rigidbody)self.As<Wrapper>().Instance).velocity = vel;
#endif
                return RuntimeValue.Nil;
            };

            _methods["set_use_gravity__1"] = (vm, self) =>
            {
                bool useGravity = vm.PopStack().IsTruthy;
                ((Rigidbody)self.As<Wrapper>().Instance).useGravity = useGravity;
                return RuntimeValue.Nil;
            };

            _methods["set_freeze_rotation__1"] = (vm, self) =>
            {
                bool freeze = vm.PopStack().IsTruthy;
                ((Rigidbody)self.As<Wrapper>().Instance).freezeRotation = freeze;
                return RuntimeValue.Nil;
            };

            _methods["set_collision_detection_mode__1"] = (vm, self) =>
            {
                int mode = (int)vm.PopStack().DoubleValue;
                ((Rigidbody)self.As<Wrapper>().Instance).collisionDetectionMode = (CollisionDetectionMode)mode;
                return RuntimeValue.Nil;
            };

            _methods["get_rotation__0"] = (vm, self) =>
            {
                Quaternion rot = ((Rigidbody)self.As<Wrapper>().Instance).rotation;
                return new RuntimeValue(QuaternionWrapper.Create(rot));
            };

            _methods["move_rotation__1"] = (vm, self) =>
            {
                Quaternion rot = (Quaternion)vm.PopStack().As<Wrapper>().Instance;
                ((Rigidbody)self.As<Wrapper>().Instance).MoveRotation(rot);
                return RuntimeValue.Nil;
            };

            _methods["move_position__1"] = (vm, self) =>
            {
                Vector3 pos = (Vector3)vm.PopStack().As<Wrapper>().Instance;
                ((Rigidbody)self.As<Wrapper>().Instance).MovePosition(pos);
                return RuntimeValue.Nil;
            };
        }

        internal static Wrapper Create(Rigidbody rb) => rb != null ? new Wrapper(rb, _methods, _getters, _setters) : null!;
    }
}