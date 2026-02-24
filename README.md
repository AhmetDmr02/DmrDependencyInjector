# DmrDependencyInjector

A lightweight, easy to use dependency injection framework for Unity.

This system is designed for games requiring deterministic service management, minimal reflection overhead, and scene aware lifecycle control.

---

## Core Features

**Reflection Cached Injection:** Field metadata is computed once per type and stored in a `ConcurrentDictionary`. Subsequent injections on the same type execute without additional reflection overhead, reducing GC pressure on the hot path.

**Multi Type Registration:** Services are automatically registered under their concrete class, all implemented interfaces, and base class hierarchy. A single `PlayerService : MonoBehaviour, IPlayerService` instance can be resolved as either `PlayerService` or `IPlayerService` without manual mapping.

**Scene Aware Lifecycle Guards:** Injection is automatically blocked during scene transitions and application shutdown. No race conditions. No null reference exceptions from objects being destroyed mid injection.

**Factory Driven Lazy Instantiation:** Services can be defined as prefabs in a `ScriptableObject` registry. If a dependency is requested but not yet instantiated, the framework spawns the prefab automatically and completes the injection in a single pass.

**Thread Safe Event System:** The `OnServiceUnregistered` event uses lock-free `Interlocked.CompareExchange` for add/remove operations, ensuring safe multi-threaded subscription without blocking the main thread.

**Partial Injection Feedback:** The `InjectionResult` struct returns which fields succeeded and which failed. You receive a detailed list of missing dependencies instead of silent failures or generic error messages.

---

## Architecture: The "Service Locator" Approach

Just as a multiplayer framework requires a `NetworkManager` to sync objects across clients, the `DmrDependencyInjector` requires **deterministic service registration** to resolve dependencies from a global container.

### 1. Registration Before Injection

Every service must register itself before any dependent object attempts injection.

> ⚠️ **Registration must occur in `Awake()` before any `Inject()` calls in the same or dependent objects.**

Dynamic late binding (e.g., instantiating a service after a client has already tried to inject it) will cause injection failure unless you re-inject manually. The container does not automatically detect newly registered services and retroactively satisfy pending dependencies.

### 2. The Field Injection Model

To keep the API minimal and Unity-native, this framework uses **attribute driven field injection** instead of constructor injection.

- **Attribute:** Mark fields with `[DmrInject]`.
- **Resolution:** The injector scans all fields (public, private, inherited) at runtime.
- **Assignment:** Dependencies are resolved from the container and assigned via reflection.

---

## Usage

### Implementing a Service

Any `MonoBehaviour` can be a service. Register in `Awake()`, unregister in `OnDestroy()`.

```csharp
using DmrDependencyInjector;
using UnityEngine;

public class WeaponService : MonoBehaviour, IWeaponService
{
    public int damage = 10;

    void Awake()
    {
        // Registers this instance as WeaponService AND IWeaponService
        this.Register();
    }

    void OnDestroy()
    {
        // Cleans up all type mappings for this instance
        this.Unregister();
    }

    public void Fire() => Debug.Log($"Fired! Damage: {damage}");
}
```

> ⚠️ **Always call `Unregister()` in `OnDestroy()`.**
>
> Failing to unregister leaves a destroyed object reference in the container. Subsequent injection attempts will resolve a "zombie" instance, causing null reference exceptions when you try to invoke methods on it.

---

### Injecting Dependencies

Mark fields with `[DmrInject]` and call `this.Inject()` in `Awake()`.

```csharp
using DmrDependencyInjector;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [DmrInject] private IWeaponService _weapon;
    [DmrInject] private IAudioService _audio;

    void Awake()
    {
        this.Inject();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _weapon.Fire();
            _audio.PlaySound("gunshot");
        }
    }
}
```

> ⚠️ **Injection order matters.**
>
> If `PlayerController.Awake()` runs before `WeaponService.Awake()`, the injection will fail because `WeaponService` has not yet registered itself. Unity's script execution order is non deterministic by default.
>
> Use **Script Execution Order** settings (`Edit > Project Settings > Script Execution Order`) to enforce that services register before clients inject. Alternatively, register services in a bootstrapper scene that loads before gameplay scenes.

---

### Factory Driven Instantiation

For services that should spawn on-demand (e.g., a pooled enemy AI service or a dynamically loaded HUD manager), use the `DmrFactoryService`.

#### Step 1: Create the ScriptableObject

```
Right-click in Project window > Create > DmrDependencyInjector > DmrFactoryService
```

Name it `DmrFactoryService` and place it in a `Resources` folder. The framework auto loads it at startup.

#### Step 2: Assign Prefabs

Drag service prefabs into the `Service Object Prefabs` array in the inspector.

#### Step 3: Inject

When a client requests a service type that exists in the factory registry but is not yet instantiated, the framework spawns the prefab automatically.

```csharp
public class GameManager : MonoBehaviour
{
    // This service is defined as a prefab, not a scene object
    [DmrInject] private PoolManager _poolManager;

    void Awake()
    {
        this.Inject();
        // If PoolManager was not instantiated yet, the factory spawns it now
        // Injection completes in a single pass
    }
}
```

> ⚠️ **Factory instantiation happens during injection.**
>
> The factory calls `Instantiate()` synchronously, which triggers `Awake()` on the spawned prefab. If that prefab's `Awake()` calls `this.Register()`, the service becomes available immediately. If the prefab's `Awake()` itself depends on other services, you may encounter ordering issues. For complex dependency graphs, consider using a two phase initialization pattern (register in `Awake()`, inject in `Start()`).

---

### Listening for Service Unregistration

If you need to clean up references when a service is destroyed (e.g., unsubscribing from events), use the `OnServiceUnregistered` event.

```csharp
void OnEnable()
{
    DIInjectorManager.OnServiceUnregistered += OnServiceDestroyed;
}

void OnDisable()
{
    DIInjectorManager.OnServiceUnregistered -= OnServiceDestroyed;
}

void OnServiceDestroyed(Type serviceType)
{
    if (serviceType == typeof(IAudioService))
    {
        Debug.Log("Audio service was destroyed. Cleaning up audio references.");
        _cachedAudioService = null;
    }
}
```

---

## Technical Specs & Constraints

### Thread Safety

> ⚠️ **The DmrDependencyInjector is partially thread-safe.**

**Container operations (`Register`, `Unregister`, `Resolve`) use `ConcurrentDictionary` and are thread-safe.**

**Injection (`Inject()`) is NOT thread-safe.** Unity's reflection APIs (`FieldInfo.SetValue`) and GameObject lifecycle methods must run on the main thread. Do not call `Inject()` from background threads.

You can safely call Inject() from background threads on pure C# objects as long as:

-Each thread injects a different object instance (no shared targets)
-The services being resolved are also thread-safe
-No factory instantiation is triggered (which requires Unity's main thread)

**The `OnServiceUnregistered` event uses lock-free `Interlocked.CompareExchange` and is fully thread-safe.**

### The Injection Lifecycle

Every `Inject()` call follows this sequence:

1. **Scene Guard Check:** If `_sceneChanging` or `_appClosing` is true, injection fails immediately.
2. **Reflection Cache Lookup:** Retrieve cached `FieldInfo` list for the target type.
3. **Resolve Dependencies:** For each `[DmrInject]` field, query the container.
4. **Factory Fallback:** If resolution fails and a factory prefab exists, spawn it and retry.
5. **Field Assignment:** Use `FieldInfo.SetValue()` to assign resolved instances.
6. **Result Construction:** Return `InjectionResult` with success status and failed field list.

### Partial Injection Behavior

If some dependencies are missing, injection does **not** throw an exception. Instead:

- Successfully resolved fields are assigned.
- Failed fields remain `null`.
- `InjectionResult.IsSuccess` is `false`.
- `InjectionResult.FailedFields` contains a list of unresolved field names.

This allows graceful degradation. You can check the result and disable optional features if their dependencies are unavailable.

```csharp
this.Inject(out var result);
if (!result.IsSuccess)
{
    Debug.LogWarning($"Injection incomplete. Missing: {string.Join(", ", result.FailedFields)}");
}
```

### Duplicate Registrations

Registering two instances under the same service type (e.g., two `WeaponService` objects) will **replace the first registration**. The container logs a warning and overwrites the old instance. The previous instance will not receive `Unregister()` cleanup unless you call it manually.

To avoid this, ensure service types are unique. If you need multiple instances of the same type (e.g., multiple weapon types), use **distinct interfaces** (`IPrimaryWeapon`, `ISecondaryWeapon`) instead of a single `IWeaponService`.

---

## Performance Notes

**Reflection is cached.** The first injection on a given type incurs reflection cost. Subsequent injections reuse the cached field list.

**No allocations on repeat injections.** After the initial cache population, injection allocates only the `InjectionResult` struct and the `List<string>` for failed fields (if any). If all dependencies resolve successfully, the failed fields list is never allocated.

**Thread-safe events use spinlocks.** The `Interlocked.CompareExchange` loop in `OnServiceUnregistered` is lock-free but may spin under high contention. In practice, service registration/unregistration is infrequent, so this is not a bottleneck.

---

## Known Limitations

**No constructor injection.** MonoBehaviours cannot use parameterized constructors in Unity. This is a Unity limitation, not a framework limitation.

**No lazy injection.** Dependencies must be registered before injection. The framework does not support deferred resolution or "inject when available" semantics.

**No scoped lifetimes.** All services are effectively singletons. If you need transient or scoped services, implement factory methods manually.

**Factory service is global.** Only one `DmrFactoryService` can exist. It must be named `DmrFactoryService` and placed in a `Resources` folder. This is enforced by the bootstrapper.

**Scene transitions block all injection.** During `SceneManager.sceneUnloaded` and `SceneManager.sceneLoaded`, injection is globally disabled. This prevents race conditions but means you cannot inject dependencies during scene load. Register services in `Awake()` after the scene has fully loaded.

---

## FAQ

**Q: Can I inject into non-MonoBehaviour classes?**

A: Yes. Any C# class can call `DIInjectorManager.InjectClassDependencies(this, out var result)`. However, you must manually call `Inject()` it is not automatic.

**Q: What happens if I call `Inject()` twice on the same object?**

A: All `[DmrInject]` fields are re-resolved and re-assigned. If a service was destroyed between the first and second call, the field will be set to `null` (or the new instance if a replacement was registered).

**Q: Can I inject static fields?**

A: No. The injector only scans instance fields. Static fields are ignored.

**Q: Can I inject properties?**

A: No. Only fields are supported. Properties are not injectable.

**Q: What if a service prefab's `Awake()` depends on another service?**

A: The factory spawns prefabs synchronously via `Instantiate()`, which immediately calls `Awake()`. If that `Awake()` injects a dependency that is not yet registered, the injection will fail. Use script execution order or two phase initialization (register in `Awake()`, inject in `Start()`) to resolve this.

**Q: Can I use this with `DontDestroyOnLoad` objects?**

A: Yes. `DontDestroyOnLoad` objects persist across scenes. If they remain registered, they will continue to be resolvable.

---
