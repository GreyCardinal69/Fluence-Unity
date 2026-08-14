namespace Fluence.Unity
{
    /// <summary>
    /// A generic, game-agnostic locator for the active Fluence Interpreter and behaviors.
    /// </summary>
    public static class FluenceEnvironment
    {
        /// <summary>
        /// The active interpreter provided by the host game.
        /// </summary>
        public static FluenceInterpreter? Interpreter { get; set; }

        private static readonly List<FluenceBehaviour> _activeBehaviours = new List<FluenceBehaviour>();

        /// <summary>
        /// Registers a behaviour to receive Update ticks.
        /// </summary>
        public static void RegisterBehaviour(FluenceBehaviour behaviour)
        {
            if (!_activeBehaviours.Contains(behaviour))
            {
                _activeBehaviours.Add(behaviour);
            }
        }

        public static void UnregisterBehaviour(FluenceBehaviour behaviour)
        {
            _activeBehaviours.Remove(behaviour);
        }

        /// <summary>
        /// Ticks all registered FluenceBehaviors. The host game must call this from an Update loop.
        /// </summary>
        public static void TickBehavioursUpdate()
        {
            if (Interpreter == null || Interpreter.State == FluenceVMState.Error || Interpreter.State == FluenceVMState.NotStarted)
            {
                UnityEngine.Debug.Log($"[Fluence] Ticks blocked. State: {(Interpreter != null ? Interpreter.State.ToString() : "Null")}"); return;
            }

            for (int i = 0; i < _activeBehaviours.Count; i++)
            {
                _activeBehaviours[i].ExecuteUpdate();
            }
        }

        /// <summary>
        /// Ticks all registered FluenceBehaviours on the physics thread. The host game must call this from a FixedUpdate loop.
        /// </summary>
        public static void TickBehavioursFixedUpdate()
        {
            if (Interpreter == null || Interpreter.State == FluenceVMState.Error || Interpreter.State == FluenceVMState.NotStarted)
            {
                UnityEngine.Debug.Log($"[Fluence] Ticks blocked. State: {(Interpreter != null ? Interpreter.State.ToString() : "Null")}"); return;
            }

            for (int i = 0; i < _activeBehaviours.Count; i++)
            {
                _activeBehaviours[i].ExecuteFixedUpdate();
            }
        }
    }
}