using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace DmrDependencyInjector
{
    [CreateAssetMenu(menuName = "DmrDependencyInjector/DmrFactoryService")]
    public class DmrFactoryService : ScriptableObject 
    {
        [SerializeField] private GameObject[] _serviceObjectPrefabs;

        [SerializeField] private bool _closeWarning = false;

        //To fetch needed prefab
        private ConcurrentDictionary<Type, GameObject> _serviceObjects = new ConcurrentDictionary<Type, GameObject>();
        public ConcurrentDictionary<Type, GameObject> ServiceObjects => _serviceObjects;

        //To register all the types it resides if needed
        private ConcurrentDictionary<GameObject, Type[]> _serviceObjectTypes = new ConcurrentDictionary<GameObject, Type[]>();

        public void OnInitialize()
        {
            if (_serviceObjectPrefabs == null || _serviceObjectPrefabs.Length == 0)
                return;

            foreach (GameObject prefab in _serviceObjectPrefabs)
            {
                if (prefab == null) continue;

                var components = prefab.GetComponents<MonoBehaviour>();
                var validTypes = new List<Type>();

                foreach (var comp in components)
                {
                    if (comp == null) continue;

                    var type = comp.GetType();

                    if (_serviceObjects.ContainsKey(type))
                    {
                        Debug.LogWarning($"Service type {type.Name} is already registered. Skipping {prefab.name}.");
                        continue;
                    }

                    _serviceObjects.TryAdd(type, prefab);
                    validTypes.Add(type);
                }

                _serviceObjectTypes.TryAdd(prefab, validTypes.ToArray());
            }
        }

        public GameObject GetServiceObject(Type type)
        {
            return _serviceObjects.TryGetValue(type, out var prefab) ? prefab : null;
        }

        /// <summary>
        /// Create a service GameObject from the prefab registered for `type`.
        /// Unity calls Awake on instantiated objects synchronously during Instantiate
        /// That means if the prefab's Awake registers the service with the DI container, the service
        /// will be available immediately after Instantiate returns.
        /// Start is invoked later (on the first enabled frame). If you need to delay Start, deactivate
        /// the instance and activate it manually after any required setup.
        /// If the prefab's Awake depends on other services, you may encounter ordering issues. For
        /// complex scenarios consider adding a lazy or explicit registration flow
        /// </summary>
        public bool CreateServiceObject(Type type)
        {
            if (_serviceObjects.TryGetValue(type, out var prefab))
            {
                Instantiate(prefab);
                return true;
            }
          
            return false;
        }
    }
}
