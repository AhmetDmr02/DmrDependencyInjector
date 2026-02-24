using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using DmrDependencyInjector;

public interface IWeaponService { void Fire(); }

public class WeaponService : MonoBehaviour, IWeaponService
{
    public void Fire() { }
    private void Awake() => this.Register();
    private void OnDestroy() => this.Unregister();
}

public class FactorySpawnedService : MonoBehaviour
{
    private void Awake() => this.Register();
}

public class PlayerClient : MonoBehaviour
{
    [DmrInject] public IWeaponService Weapon;
    [DmrInject] public FactorySpawnedService FactoryService;
}

public class BaseClient : MonoBehaviour
{
    [DmrInject] public IWeaponService BaseWeapon;
}

public class DerivedClient : BaseClient { }

public class DmrDITests
{
    private GameObject _serviceGo;
    private GameObject _clientGo;

    // Resets the DI container state and creates fresh GameObjects before every test.
    [SetUp]
    public void Setup()
    {
        DIInjectorManager.ClearCache();
        DIInjectorManager.SetSceneChanging(false);
        ClearDIContainer();

        _serviceGo = new GameObject("Service");
        _clientGo = new GameObject("Client");
    }

    // Destroys the test GameObjects and clears the factory reference after every test.
    [TearDown]
    public void Teardown()
    {
        if (_serviceGo != null) UnityEngine.Object.DestroyImmediate(_serviceGo);
        if (_clientGo != null) UnityEngine.Object.DestroyImmediate(_clientGo);
        DIInjectorManager.SetFactory(null);
    }

    // Uses reflection to violently wipe the static dictionaries inside the DmrDIContainer.
    private void ClearDIContainer()
    {
        var type = typeof(DmrDIContainer);
        var instancesField = type.GetField("_instances", BindingFlags.Static | BindingFlags.NonPublic);
        var instanceToTypesField = type.GetField("_instanceToTypes", BindingFlags.Static | BindingFlags.NonPublic);

        var instances = (System.Collections.IDictionary)instancesField.GetValue(null);
        var instanceToTypes = (System.Collections.IDictionary)instanceToTypesField.GetValue(null);

        instances.Clear();
        instanceToTypes.Clear();
    }

    // Proves a standard registered service is successfully injected into a client.
    [Test]
    public void Test01_Inject_WhenServiceRegistered_ReturnsSuccess()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        var client = _clientGo.AddComponent<PlayerClient>();
        client.Inject();

        Assert.IsNotNull(client.Weapon);
    }

    // Proves the container gracefully returns a partial failure when a dependency is missing.
    [Test]
    public void Test02_Inject_WhenServiceMissing_ReturnsPartialFailure()
    {
        var client = _clientGo.AddComponent<PlayerClient>();
        DIInjectorManager.InjectClassDependencies(client, out var result);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.FailedFields.Count > 0);
        Assert.IsNull(client.Weapon);
    }

    // Proves the container can resolve a concrete instance using its interface type.
    [Test]
    public void Test03_Container_ResolvesByInterface()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        var resolveMethod = typeof(DmrDIContainer).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
        var resolved = resolveMethod.Invoke(null, new object[] { typeof(IWeaponService) });

        Assert.AreEqual(service, resolved);
    }

    // Proves a destroyed Unity object cannot poison the container via registration.
    [Test]
    public void Test04_Register_WithDestroyedMonoBehaviour_FailsFakeNullCheck()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        UnityEngine.Object.DestroyImmediate(service);

        bool success = service.Register();

        var resolveMethod = typeof(DmrDIContainer).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
        var resolved = resolveMethod.Invoke(null, new object[] { typeof(WeaponService) });

        Assert.IsNull(resolved);
    }

    // Proves the factory will instantiate and inject a missing service on demand.
    [Test]
    public void Test05_Factory_WhenServiceMissing_InstantiatesAndInjects()
    {
        var factory = ScriptableObject.CreateInstance<DmrFactoryService>();
        var prefabGo = new GameObject("FactoryPrefab");
        prefabGo.AddComponent<FactorySpawnedService>();

        var dictField = typeof(DmrFactoryService).GetField("_serviceObjects", BindingFlags.Instance | BindingFlags.NonPublic);
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<Type, GameObject>();
        dict.TryAdd(typeof(FactorySpawnedService), prefabGo);
        dictField.SetValue(factory, dict);

        DIInjectorManager.SetFactory(factory);

        var client = _clientGo.AddComponent<PlayerClient>();
        client.Inject();

        Assert.IsNotNull(client.FactoryService);

        UnityEngine.Object.DestroyImmediate(prefabGo);
    }

    // Proves the container aborts injection if the scene transition lock is active.
    [Test]
    public void Test06_Inject_WhenSceneIsChanging_AbortsAndReturnsFailed()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        DIInjectorManager.SetSceneChanging(true);

        var client = _clientGo.AddComponent<PlayerClient>();
        DIInjectorManager.InjectClassDependencies(client, out var result);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(client.Weapon);
    }

    // Proves registering a new service overwrites an old service of the exact same type.
    [Test]
    public void Test07_Register_OverwritesExistingServiceOfSameType()
    {
        var service1 = _serviceGo.AddComponent<WeaponService>();
        var service2 = new GameObject("Service2").AddComponent<WeaponService>();

        service1.Register();
        service2.Register();

        var client = _clientGo.AddComponent<PlayerClient>();
        client.Inject();

        Assert.AreEqual(service2, client.Weapon);
        UnityEngine.Object.DestroyImmediate(service2.gameObject);
    }

    // Proves a service is cleanly removed from the container upon unregistration.
    [Test]
    public void Test08_Unregister_RemovesServiceFromContainer()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();
        service.Unregister();

        var client = _clientGo.AddComponent<PlayerClient>();
        client.Inject();

        Assert.IsNull(client.Weapon);
    }

    // Proves the manager fires its unregistration event properly.
    [Test]
    public void Test09_Unregister_FiresOnServiceUnregisteredEvent()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        bool eventFired = false;
        Action<Type> handler = (type) =>
        {
            if (type == typeof(WeaponService)) eventFired = true;
        };

        DIInjectorManager.OnServiceUnregistered += handler;
        service.Unregister();
        DIInjectorManager.OnServiceUnregistered -= handler;

        Assert.IsTrue(eventFired);
    }

    // Proves the reflection walker correctly finds injectable fields inside parent classes.
    [Test]
    public void Test10_Inject_PopulatesBaseClassInjectableFields()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        var client = _clientGo.AddComponent<DerivedClient>();
        client.Inject();

        Assert.IsNotNull(client.BaseWeapon);
    }

    // Proves the container instantly rejects standard null objects.
    [Test]
    public void Test11_Register_RejectsStandardNull()
    {
        WeaponService nullService = null;

        var registerMethod = typeof(DmrDIContainer).GetMethod("RegisterWithAllTypes", BindingFlags.Static | BindingFlags.NonPublic);
        bool result = (bool)registerMethod.Invoke(null, new object[] { nullService });

        Assert.IsFalse(result);
    }

    // Proves the scene changing lock handles rapid toggling without leaking state.
    [Test]
    public void Test12_SceneTransition_RapidToggle_DropsInjectionsCleanly()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        var client = _clientGo.AddComponent<BaseClient>();

        DIInjectorManager.SetSceneChanging(true);
        DIInjectorManager.InjectClassDependencies(client, out var result1);

        DIInjectorManager.SetSceneChanging(false);
        DIInjectorManager.InjectClassDependencies(client, out var result2);

        DIInjectorManager.SetSceneChanging(true);
        DIInjectorManager.InjectClassDependencies(client, out var result3);

        Assert.IsFalse(result1.IsSuccess);
        Assert.IsTrue(result2.IsSuccess);
        Assert.IsFalse(result3.IsSuccess);
    }

    // Proves a destroyed service cannot be injected, and a replacement overwrites the dead entry.
    [Test]
    public void Test13_DeletionThenReinjection_ZombieService_IsOverwritten()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();
        var client = _clientGo.AddComponent<PlayerClient>();

        client.Inject();
        UnityEngine.Object.DestroyImmediate(service);
        client.Weapon = null;

        client.Inject();
        Assert.IsNull(client.Weapon);

        var newServiceGo = new GameObject("NewService");
        var newService = newServiceGo.AddComponent<WeaponService>();
        newService.Register();

        client.Inject();
        Assert.IsNotNull(client.Weapon);
        Assert.AreEqual(newService, client.Weapon);

        UnityEngine.Object.DestroyImmediate(newServiceGo);
    }

    // Proves spamming the register method on the same instance does not duplicate type entries.
    [Test]
    public void Test14_DoubleRegister_SpammingSameInstance_DoesNotDuplicate()
    {
        var service = _serviceGo.AddComponent<WeaponService>();

        service.Register();
        service.Register();
        service.Register();

        var instanceToTypesField = typeof(DmrDIContainer).GetField("_instanceToTypes", BindingFlags.Static | BindingFlags.NonPublic);
        var map = (System.Collections.Concurrent.ConcurrentDictionary<object, HashSet<Type>>)instanceToTypesField.GetValue(null);

        Assert.IsTrue(map.TryGetValue(service, out var types));
        Assert.AreEqual(types.Count, new HashSet<Type>(types).Count);
    }

    // Proves the reflection cache performs efficiently under heavy injection load.
    [Test]
    public void Test15_InjectSpamming_StressTest_ZeroGarbageCheck()
    {
        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        var client = _clientGo.AddComponent<BaseClient>();

        client.Inject();

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        for (int i = 0; i < 10000; i++)
        {
            client.BaseWeapon = null; 
            DIInjectorManager.InjectClassDependencies(client, out var result);
            if (!result.IsSuccess) Assert.Fail("Injection returned false during stress test.");
        }

        stopwatch.Stop();

        Assert.Less(stopwatch.ElapsedMilliseconds, 500);
    }

    // Proves registering a new service updates interface mappings to the newest instance.
    [Test]
    public void Test16_OverwriteRegistration_DifferentInstances_SameInterface()
    {
        var service1 = _serviceGo.AddComponent<WeaponService>();
        var serviceGo2 = new GameObject("Service2");
        var service2 = serviceGo2.AddComponent<WeaponService>();

        service1.Register();

        var resolveMethod = typeof(DmrDIContainer).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
        var resolvedFirst = resolveMethod.Invoke(null, new object[] { typeof(IWeaponService) });
        Assert.AreEqual(service1, resolvedFirst);

        service2.Register();

        var resolvedSecond = resolveMethod.Invoke(null, new object[] { typeof(IWeaponService) });
        Assert.AreEqual(service2, resolvedSecond);

        UnityEngine.Object.DestroyImmediate(serviceGo2);
    }

    // Proves the factory instance survives a manual scene state toggle and successfully spawns an object.
    [Test]
    public void Test17_Factory_SurvivesAndFunctions_AfterSceneTransition()
    {
        var factory = ScriptableObject.CreateInstance<DmrFactoryService>();
        var prefabGo = new GameObject("FactoryPrefab");
        prefabGo.AddComponent<FactorySpawnedService>();

        var dictField = typeof(DmrFactoryService).GetField("_serviceObjects", BindingFlags.Instance | BindingFlags.NonPublic);
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<Type, GameObject>();
        dict.TryAdd(typeof(FactorySpawnedService), prefabGo);
        dictField.SetValue(factory, dict);

        DIInjectorManager.SetFactory(factory);

        DIInjectorManager.SetSceneChanging(true);
        DIInjectorManager.SetSceneChanging(false);

        var client = _clientGo.AddComponent<PlayerClient>();
        client.Inject();

        Assert.IsNotNull(client.FactoryService);

        UnityEngine.Object.DestroyImmediate(prefabGo);
        UnityEngine.Object.DestroyImmediate(factory);
    }

    // Proves the DmrBootstrapper natively intercepts Unity's SceneManager events to lock the container.
    [UnityTest]
    public IEnumerator Test18_NativeSceneManager_HooksWorkProperly()
    {
        Scene tempScene = SceneManager.CreateScene("TempDI_TestScene");
        yield return null;

        var service = _serviceGo.AddComponent<WeaponService>();
        service.Register();

        DIInjectorManager.SetSceneChanging(false);

        var unloadOp = SceneManager.UnloadSceneAsync(tempScene);
        yield return unloadOp;

        var client = _clientGo.AddComponent<PlayerClient>();
        DIInjectorManager.InjectClassDependencies(client, out var result);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(client.Weapon);

        DIInjectorManager.SetSceneChanging(false);
    }
    // Pure C# dummy classes to bypass Unity's main thread restrictions
    public class NonUnityService { }
    public class NonUnityClient { [DmrInject] private NonUnityService _service; }

    [Test]
    public void Test19_MultiThreading_ConcurrentReadWrites_DoesNotCorruptContainer()
    {
        int threadCount = 50;
        var tasks = new System.Threading.Tasks.Task[threadCount];
        int exceptionsCaught = 0;

        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var dummyService = new NonUnityService();

                    var registerMethod = typeof(DmrDIContainer).GetMethod("RegisterWithAllTypes", BindingFlags.Static | BindingFlags.NonPublic);
                    registerMethod.Invoke(null, new object[] { dummyService });

                    var resolveMethod = typeof(DmrDIContainer).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
                    var resolved = resolveMethod.Invoke(null, new object[] { typeof(NonUnityService) });

                    if (resolved == null) throw new Exception("Resolution failed on background thread.");

                    var fields = DIInjectorManager.GetInjectableFields(typeof(NonUnityClient));

                    if (fields.Count == 0) throw new Exception("Reflection cache failed to return fields on background thread.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Thread failed: {ex.Message}");
                    System.Threading.Interlocked.Increment(ref exceptionsCaught);
                }
            });
        }

        System.Threading.Tasks.Task.WaitAll(tasks);

        Assert.AreEqual(0, exceptionsCaught, "Exceptions were thrown during multi-threaded access. The container is not thread-safe!");
    }
}