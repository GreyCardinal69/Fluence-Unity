using Fluence.Unity.RuntimeTypes;

namespace Fluence.Unity.API
{
    /// <summary>
    /// A centralized, public factory for converting standard Unity Engine objects 
    /// into Fluence-compatible RuntimeValue wrappers.
    /// </summary>
    public static class FluenceWrapperFactory
    {
        /// <summary>
        /// Safely wraps a Unity GameObject into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(GameObject gameObject)
        {
            if (gameObject == null) return RuntimeValue.Nil;
            return new RuntimeValue(GameObjectWrapper.Create(gameObject));
        }

        /// <summary>
        /// Safely wraps a Unity Transform into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(Transform transform)
        {
            if (transform == null) return RuntimeValue.Nil;
            return new RuntimeValue(TransformWrapper.Create(transform));
        }

        /// <summary>
        /// Safely wraps a Unity Rigidbody into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(Rigidbody rigidbody)
        {
            if (rigidbody == null) return RuntimeValue.Nil;
            return new RuntimeValue(RigidbodyWrapper.Create(rigidbody));
        }

        /// <summary>
        /// Safely wraps a Unity Collider into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(Collider collider)
        {
            if (collider == null) return RuntimeValue.Nil;
            return new RuntimeValue(ColliderWrapper.Create(collider));
        }

        /// <summary>
        /// Safely wraps a Unity Camera into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(Camera camera)
        {
            if (camera == null) return RuntimeValue.Nil;
            return new RuntimeValue(CameraWrapper.Create(camera));
        }

        /// <summary>
        /// Safely wraps a Unity Vector3 struct into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(Vector3 vector)
        {
            return new RuntimeValue(Vector3Wrapper.Create(vector));
        }

        /// <summary>
        /// Safely wraps a Unity Quaternion struct into a Fluence RuntimeValue.
        /// </summary>
        public static RuntimeValue Wrap(Quaternion quaternion)
        {
            return new RuntimeValue(QuaternionWrapper.Create(quaternion));
        }

        /// <summary>
        /// A generic fallback wrapper for any custom C# class or Unity Component 
        /// that does not have a dedicated Fluence wrapper dictionary.
        /// Warning: The resulting object will be accessible in Fluence, but will 
        /// have no exposed properties or methods unless manually bound.
        /// </summary>
        public static RuntimeValue WrapGeneric(object instance)
        {
            if (instance == null) return RuntimeValue.Nil;

            var emptyMethods = new System.Collections.Generic.Dictionary<string, VirtualMachine.FluenceVirtualMachine.IntrinsicRuntimeMethod>();
            var bareWrapper = new Wrapper(instance, emptyMethods);

            return new RuntimeValue(bareWrapper);
        }
    }
}