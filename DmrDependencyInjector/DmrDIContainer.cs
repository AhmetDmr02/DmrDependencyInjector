using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DmrDependencyInjector
{
    public static class DmrDIContainer
    {
        // Maps service Type -> Instance
        private static readonly ConcurrentDictionary<Type, object> _instances = new();
        // Maps Instance -> Set of registered types (for cleanup)
        private static readonly ConcurrentDictionary<object, ConcurrentDictionary<Type,byte>> _instanceToTypes = new();

        private static readonly object _lock = new object();

        internal static bool RegisterWithAllTypes(object instance)
        {
            if (!ValidateRegistration(instance))
                return false;

            var typesToRegister = GetAllRegistrableTypes(instance.GetType());
            return Register(instance, typesToRegister.ToArray());
        }

        internal static bool Register(object instance, params Type[] asTypes)
        {
            if (!ValidateRegistration(instance))
                return false;

            if (asTypes == null || asTypes.Length == 0)
                asTypes = new[] { instance.GetType() };

            foreach (var serviceType in asTypes)
            {
                if (!serviceType.IsAssignableFrom(instance.GetType()))
                {
                    Debug.LogError($"Instance of type {instance.GetType().Name} cannot be registered as {serviceType.Name}");
                    return false;
                }
            }

            lock (_lock)
            {
                var registeredTypes = new ConcurrentDictionary<Type, byte>();
                foreach (var serviceType in asTypes)
                {
                    if (_instances.TryGetValue(serviceType, out var existing) && existing != instance)
                    {
                        Debug.LogWarning($"Service type {serviceType.Name} already registered. Replacing instance.");
                        CleanupOldInstance(existing, serviceType);
                    }
                    _instances[serviceType] = instance;
                    registeredTypes.TryAdd(serviceType, 0);
                }

                _instanceToTypes.AddOrUpdate(instance,
                  registeredTypes,
                  (key, existing) =>
                  {
                      foreach (var kvp in registeredTypes)
                      {
                          existing.TryAdd(kvp.Key, 1);
                      }
                      return existing;
                  });
            }

            return true;
        }

        internal static List<Type> UnregisterInstance(object instance)
        {
            var removed = new List<Type>();

            if (_instanceToTypes.TryRemove(instance, out var types))
            {
                foreach (var kvp in types)
                {
                    var t = kvp.Key;

                    var collection = (System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<Type, object>>)_instances;
                    var pairToRemove = new System.Collections.Generic.KeyValuePair<Type, object>(t, instance);

                    if (collection.Remove(pairToRemove))
                    {
                        removed.Add(t);
                    }
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning($"Instance of type {instance.GetType().Name} was not registered.");
            }

            return removed;
        }

        // Resolves an instance by service type, or null if none found
        internal static object Resolve(Type serviceType)
        {
            _instances.TryGetValue(serviceType, out var inst);
            return inst;
        }

        // Helpers
        private static HashSet<Type> GetAllRegistrableTypes(Type concreteType)
        {
            var types = new HashSet<Type> { concreteType };

            foreach (var i in concreteType.GetInterfaces())
            {
                if (i != typeof(IDisposable))
                    types.Add(i);
            }

            var baseType = concreteType.BaseType;
            while (baseType != null && baseType != typeof(object) && baseType != typeof(MonoBehaviour))
            {
                types.Add(baseType);
                baseType = baseType.BaseType;
            }

            return types;
        }

        private static void CleanupOldInstance(object oldInstance, Type serviceType)
        {
            if (_instanceToTypes.TryGetValue(oldInstance, out var types))
            {
                types.TryRemove(serviceType, out _);
                if (types.IsEmpty)
                    _instanceToTypes.TryRemove(oldInstance, out _);
            }
        }

        private static bool ValidateRegistration(object instance)
        {
            if (instance is null) return false;

            if (instance is UnityEngine.Object unityObj && unityObj == null)
            {
                return false;
            }

            return true;
        }
    }
}
