using Fluence.Unity.RuntimeTypes;
using static Fluence.Unity.VirtualMachine.FluenceVirtualMachine;

namespace Fluence.Unity
{
    internal static class FluenceBehaviourWrapper
    {
        private static readonly Dictionary<string, IntrinsicRuntimeMethod> _methods = new();
        private static readonly Dictionary<string, Func<Wrapper, RuntimeValue>> _getters = new();
        private static readonly Dictionary<string, Action<Wrapper, RuntimeValue>> _setters = new();

        static FluenceBehaviourWrapper()
        {
            _getters["instance"] = w => new RuntimeValue(((FluenceBehaviour)w.Instance).FluenceInstance);
        }

        public static Wrapper Create(FluenceBehaviour fb)
        {
            return fb != null ? new Wrapper(fb, _methods, _getters, _setters) : null!;
        }
    }
}