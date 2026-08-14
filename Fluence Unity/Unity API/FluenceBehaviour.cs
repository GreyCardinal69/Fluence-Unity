using Fluence.Unity.RuntimeTypes;
using UnityEngine;

namespace Fluence.Unity
{
    /// <summary>
    /// A generic Unity Component that links a GameObject to a struct inside a Fluence script.
    /// </summary>
    public class FluenceBehaviour : MonoBehaviour
    {
        [Header("Script Setup")]
        [Tooltip("The exact name of the struct inside the Fluence scripts.")]
        public string FluenceStructName;

        private InstanceObject _fluenceInstance;
        private FunctionValue _onCollisionEnterMethod;
        private FunctionValue _onTriggerEnterMethod;
        private FunctionValue _onCollisionStayMethod;
        private FunctionValue _onTriggerStayMethod;
        private FunctionValue _fixedUpdateMethod;

        private FunctionValue _updateMethod;

        internal InstanceObject FluenceInstance => _fluenceInstance;

        private void Start()
        {
            if (FluenceEnvironment.Interpreter == null || FluenceEnvironment.Interpreter.ParseState == null)
            {
                Debug.LogError($"[Fluence] Cannot start '{FluenceStructName}' - Interpreter is not initialized on FluenceEnvironment.");
                return;
            }

            FluenceScope globalScope = FluenceEnvironment.Interpreter.ParseState.GlobalScope;

            int hash = FluenceStructName.GetHashCode();
            if (globalScope.TryGetLocalSymbol(hash, out Symbol symbol) && symbol is StructSymbol structSymbol)
            {
                _fluenceInstance = new InstanceObject(structSymbol);

                structSymbol.Functions.TryGetValue(Mangler.Mangle("update", 0), out _updateMethod!);
                structSymbol.Functions.TryGetValue(Mangler.Mangle("fixed_update", 0), out _fixedUpdateMethod!);

                structSymbol.Functions.TryGetValue(Mangler.Mangle("on_collision_enter", 2), out _onCollisionEnterMethod!);
                structSymbol.Functions.TryGetValue(Mangler.Mangle("on_trigger_enter", 1), out _onTriggerEnterMethod!);
                structSymbol.Functions.TryGetValue(Mangler.Mangle("on_collision_stay", 1), out _onCollisionStayMethod!);
                structSymbol.Functions.TryGetValue(Mangler.Mangle("on_trigger_stay", 1), out _onTriggerStayMethod!);

                _fluenceInstance.SetField("gameObject", new RuntimeValue(GameObjectWrapper.Create(this.gameObject)));
                _fluenceInstance.SetField("transform", new RuntimeValue(TransformWrapper.Create(this.transform)));

                if (structSymbol.Constructors.TryGetValue(Mangler.Mangle("init", 0), out FunctionValue initMethod))
                {
                    Debug.Log($"[Fluence] Calling init() on '{FluenceStructName}'...");
                    FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, initMethod);
                }

                if (_updateMethod != null)
                {
                    FluenceEnvironment.RegisterBehaviour(this);
                }
            }
            else
            {
                Debug.LogError($"[Fluence] Struct '{FluenceStructName}' was not found in the global scope.");
            }
        }

        /// <summary>
        /// Safely invokes a 0-argument method on the Fluence instance from C#.
        /// </summary>
        public void CallFluenceMethod(string methodName)
        {
            if (_fluenceInstance == null) return;

            string mangledName = Mangler.Mangle(methodName, 0);

            if (_fluenceInstance.Class.Functions.TryGetValue(mangledName, out FunctionValue methodBlueprint))
            {
                if (FluenceEnvironment.Interpreter == null)
                {
                    Debug.LogWarning($"[Fluence] No active Interpreter found, aborting execution of a method call of method: {methodName} on struct: {FluenceStructName}.");
                    return;
                }

                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, methodBlueprint);
            }
            else
            {
                Debug.LogWarning($"[Fluence] Method '{methodName}' (mangled: {mangledName}) not found on struct '{FluenceStructName}'.");
            }
        }

        /// <summary>
        /// Safely invokes a 1-argument method on the Fluence instance from C#.
        /// </summary>
        public void CallFluenceMethod(string methodName, double arg)
        {
            if (_fluenceInstance == null) return;

            string mangledName = Mangler.Mangle(methodName, 1);

            if (_fluenceInstance.Class.Functions.TryGetValue(mangledName, out FunctionValue methodBlueprint))
            {
                if (FluenceEnvironment.Interpreter == null)
                {
                    Debug.LogWarning($"[Fluence] No active Interpreter found, aborting execution of a method call of method: {methodName} on struct: {FluenceStructName}.");
                    return;
                }

                RuntimeValue argVal = new RuntimeValue(arg);
                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, methodBlueprint, argVal);
            }
            else
            {
                Debug.LogWarning($"[Fluence] Method '{methodName}' (mangled: {mangledName}) not found on struct '{FluenceStructName}'.");
            }
        }

        private void OnDestroy()
        {
            if (_updateMethod != null)
            {
                FluenceEnvironment.UnregisterBehaviour(this);
            }
        }

        internal void ExecuteUpdate()
        {
            if (_fluenceInstance != null && _updateMethod != null)
            {
                if (FluenceEnvironment.Interpreter == null)
                {
                    Debug.LogWarning($"[Fluence] No active Interpreter found, aborting execution of Update on struct: {FluenceStructName}.");
                    return;
                }

                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, _updateMethod);
            }
        }

        internal void ExecuteFixedUpdate()
        {
            if (_fluenceInstance != null && _fixedUpdateMethod != null)
            {
                if (FluenceEnvironment.Interpreter == null)
                {
                    Debug.LogWarning($"[Fluence] No active Interpreter found, aborting execution of FixedUpdate on struct: {FluenceStructName}.");
                    return;
                }

                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, _fixedUpdateMethod);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_fluenceInstance != null && _onCollisionEnterMethod != null)
            {
                RuntimeValue goArg = new RuntimeValue(GameObjectWrapper.Create(collision.gameObject));

                Vector3 normal = collision.contactCount > 0 ? collision.contacts[0].normal : Vector3.up;
                RuntimeValue normalArg = new RuntimeValue(Vector3Wrapper.Create(normal));

                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, _onCollisionEnterMethod, goArg, normalArg);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_fluenceInstance != null && _onTriggerEnterMethod != null)
            {
                RuntimeValue goArg = new RuntimeValue(GameObjectWrapper.Create(other.gameObject));
                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, _onTriggerEnterMethod, goArg);
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (_fluenceInstance != null && _onCollisionStayMethod != null)
            {
                RuntimeValue goArg = new RuntimeValue(GameObjectWrapper.Create(collision.gameObject));
                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, _onCollisionStayMethod, goArg);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (_fluenceInstance != null && _onTriggerStayMethod != null)
            {
                RuntimeValue goArg = new RuntimeValue(GameObjectWrapper.Create(other.gameObject));
                FluenceEnvironment.Interpreter.VM.ExecuteManualMethodCall(_fluenceInstance, _onTriggerStayMethod, goArg);
            }
        }
    }
}