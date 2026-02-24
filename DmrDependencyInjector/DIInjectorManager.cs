using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace DmrDependencyInjector
{
    public static class DIInjectorManager
    {
        static readonly ConcurrentDictionary<Type, List<FieldInfo>> _cache = new();

        private static DmrFactoryService _factoryService;

        private static Action<Type> _onServiceUnregistered;

        //Thread safe event
        public static event Action<Type> OnServiceUnregistered
        {
            add
            {
                Action<Type> prev, next;
                do
                {
                    prev = _onServiceUnregistered;
                    next = (Action<Type>)Delegate.Combine(prev, value);
                }
                while (Interlocked.CompareExchange(ref _onServiceUnregistered, next, prev) != prev);
            }
            remove
            {
                Action<Type> prev, next;
                do
                {
                    prev = _onServiceUnregistered;
                    next = (Action<Type>)Delegate.Remove(prev, value) as Action<Type>;
                }
                while (Interlocked.CompareExchange(ref _onServiceUnregistered, next, prev) != prev);
            }
        }

        private static bool _appClosing;

        private static bool _sceneChanging;
        public static void SetSceneChanging(bool isChanging) => _sceneChanging = isChanging;

        public static void SetFactory(DmrFactoryService factory) => _factoryService = factory;
        static DIInjectorManager()
        {
            //We dont need to release this event since its killed with the app
            Application.quitting += () => _appClosing = true;
        }

        public static void InjectClassDependencies(object target, out InjectionResult result)
        {
            if (_appClosing || _sceneChanging)
            {
                result = InjectionResult.Failed;
                return;
            }

            List<string> failedFields = null;

            try
            {
                var fields = GetInjectableFields(target.GetType());

                foreach (var field in fields)
                {
                    if (_sceneChanging || _appClosing)
                    {
                        result = InjectionResult.Failed;
                        return;
                    }

                    var service = DmrDIContainer.Resolve(field.FieldType);

                    if (service is UnityEngine.Object unityObj && unityObj == null)
                    {
                        service = null;
                    }

                    if (service == null)
                    {
                        if (_factoryService != null && _factoryService.ServiceObjects.TryGetValue(field.FieldType, out var prefab) && prefab != null)
                        {
                            _factoryService.CreateServiceObject(field.FieldType);

                            service = DmrDIContainer.Resolve(field.FieldType);

                            if (service != null)
                            {
                                field.SetValue(target, service);

                                continue;
                            }
                        }

                        if(failedFields == null) failedFields = new List<string>();

                        failedFields.Add($"{field.DeclaringType?.Name}.{field.Name} ({field.FieldType.Name})");

                        continue;
                    }
                    field.SetValue(target, service);
                }

                result = (failedFields == null || failedFields.Count == 0) ? InjectionResult.Success : InjectionResult.PartialFailure(failedFields.ToList());
            }
            catch (Exception ex)
            {
                Debug.LogError($"Injection failed for {target.GetType().Name}: {ex.Message}");
                result = InjectionResult.Failed;
            }
        }

        public static bool CanInjectDependencies(object target)
        {
            if (_appClosing || _sceneChanging)
            {
                return false;
            }

            var fields = GetInjectableFields(target.GetType());

            bool success = false;
            foreach (var field in fields)
            {
                var service = DmrDIContainer.Resolve(field.FieldType);
                if (service == null)
                {
                    if (_factoryService != null && _factoryService.ServiceObjects.TryGetValue(field.FieldType, out var prefab) && prefab != null)
                    {
                        _factoryService.CreateServiceObject(field.FieldType);

                        service = DmrDIContainer.Resolve(field.FieldType);

                        if (service != null)
                        {
                            continue;
                        }
                    }

                    success = false;
                    break;
                }
                success = true;
            }

            return success;
        }

        public static List<FieldInfo> GetInjectableFields(Type type)
        {
            return _cache.GetOrAdd(type, t =>
            {
                var fields = new List<FieldInfo>();

                while (t != null && t != typeof(object))
                {
                    fields.AddRange(
                       t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                        .Where(f => f.IsDefined(typeof(DmrInjectAttribute), true))
                    );
                    t = t.BaseType;
                }

                return fields;
            });
        }

        // Auto-register instance with all its types (class + interfaces + base classes)
        public static bool Register(object instance)
        {
           return DmrDIContainer.RegisterWithAllTypes(instance);
        }

        public static void Unregister(object instance)
        {
            var unregisteredTypes = DmrDIContainer.UnregisterInstance(instance);
            if(unregisteredTypes == null || unregisteredTypes.Count == 0) return;

            var handlerSnapshot = _onServiceUnregistered;
            if(handlerSnapshot == null) return;

            foreach (var type in unregisteredTypes)
            {
                foreach (var handler in handlerSnapshot.GetInvocationList())
                {
                    try
                    {
                        ((Action<Type>)handler)(type);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"OnServiceUnregistered handler threw for {type}: {ex}");
                    }
                }
            }
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }
    }

    public class InjectionResult
    {
        public bool IsSuccess { get; private set; }
        public List<string> FailedFields { get; private set; }

        private InjectionResult(bool success, List<string> failedFields = null)
        {
            IsSuccess = success;
            FailedFields = failedFields ?? new List<string>();
        }

        public static InjectionResult Success => new(true);
        public static InjectionResult Failed => new(false);
        public static InjectionResult PartialFailure(List<string> failedFields) => new(false, failedFields);

        public override string ToString()
        {
            if (IsSuccess) return "Success";
            if (FailedFields.Count == 0) return "Failed";
            return $"Failed to inject: {string.Join(", ", FailedFields)}";
        }
    }
}
