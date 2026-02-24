using UnityEngine;

namespace DmrDependencyInjector
{
    public static class DmrExtensions
    {
        /// <summary>
        /// Injects dependencies into fields marked with [DmrInject].
        /// Call this in Awake() or Start().
        /// </summary>
        public static void Inject(this MonoBehaviour mb)
        {
            DIInjectorManager.InjectClassDependencies(mb, out _);
        }

        /// <summary>
        /// Registers this instance into the DI container.
        /// </summary>
        public static bool Register(this MonoBehaviour mb)
        {
           return DIInjectorManager.Register(mb);
        }
        /// <summary>
        /// Unregisters this instance. MUST be called in OnDestroy() if registered.
        /// </summary>
        public static void Unregister(this MonoBehaviour mb)
        {
            DIInjectorManager.Unregister(mb);
        }
    }
}