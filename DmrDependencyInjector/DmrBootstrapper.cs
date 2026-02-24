using UnityEngine;
using UnityEngine.SceneManagement;

namespace DmrDependencyInjector
{
    internal static class DmrBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            DIInjectorManager.SetSceneChanging(false);

            var factory = Resources.Load<DmrFactoryService>("DmrFactoryService");
            if (factory != null)
            {
                factory.OnInitialize();
                DIInjectorManager.SetFactory(factory);
            }
            else
            {
                Debug.Log("[DmrDI] DmrFactoryService not found in Resources. Factory instantiation disabled.");
            }
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            DIInjectorManager.SetSceneChanging(true);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DIInjectorManager.SetSceneChanging(false);
        }
    }
}