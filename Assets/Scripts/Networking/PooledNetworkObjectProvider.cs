using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recycles NetworkObject instances instead of Instantiate/Destroy on every spawn.
///
/// Because this plugs into Fusion's own spawn path, Runner.Spawn and Runner.Despawn keep
/// working unchanged and every peer pools independently - there is no [Networked] "is active"
/// flag to hand-manage, and clients stay in sync for free.
///
/// Assigned to the runner in FusionBootstrap via StartGameArgs.ObjectProvider. That line
/// fetches a NetworkObjectProviderDefault, and this IS one, so no change is needed there -
/// just swap the component on the runner GameObject in Menu.unity.
/// </summary>
public class PooledNetworkObjectProvider : NetworkObjectProviderDefault
{
    [Tooltip("Most instances of one prefab kept parked. Anything beyond this is really destroyed so the pool can't grow without bound.")]
    [SerializeField] private int maxPooledPerPrefab = 32;

    [Tooltip("Logs every reuse and park. Useful while verifying the pool, noisy afterwards.")]
    [SerializeField] private bool logPoolActivity = false;

    // Parked, deactivated instances, keyed by the prefab they came from.
    private readonly Dictionary<NetworkObject, Stack<NetworkObject>> parked =
        new Dictionary<NetworkObject, Stack<NetworkObject>>();

    // Remembers which prefab each live instance came from.
    //
    // This exists because Fusion's two hooks are asymmetric: InstantiatePrefab receives the
    // prefab, while DestroyPrefabInstance only receives a NetworkPrefabId. Recording the
    // mapping on creation lets both sides share one dictionary key without having to reload
    // the prefab asset just to look it up.
    private readonly Dictionary<NetworkObject, NetworkObject> prefabOfInstance =
        new Dictionary<NetworkObject, NetworkObject>();

    /// <summary>
    /// Called by Fusion when it needs a new instance of a prefab.
    /// Reuse a parked one if we have it; otherwise fall through to a real Instantiate.
    /// </summary>
    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        if (parked.TryGetValue(prefab, out Stack<NetworkObject> available))
        {
            // Loop rather than a single Pop: a parked instance can be destroyed out from
            // under us by a scene unload, and Unity's fake-null makes that pop non-null-safe.
            while (available.Count > 0)
            {
                NetworkObject recycled = available.Pop();

                if (recycled == null)
                {
                    continue;
                }

                recycled.gameObject.SetActive(true);

                if (logPoolActivity)
                {
                    Debug.Log($"[Pool] Reused {prefab.name}. {available.Count} left parked.");
                }

                // Deliberately not resetting gameplay state here. Fusion calls Spawned() on
                // the recycled object just like a fresh one, so resets belong there
                // (see Enemy.Spawned). Awake will NOT run again.
                return recycled;
            }
        }

        NetworkObject created = base.InstantiatePrefab(runner, prefab);

        if (created != null)
        {
            prefabOfInstance[created] = prefab;
        }

        return created;
    }

    /// <summary>
    /// Called by Fusion when an object is despawned. Park it instead of destroying it.
    /// </summary>
    protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        // Something we never handed out (or already released). Let the base class deal with it.
        if (instance == null || !prefabOfInstance.TryGetValue(instance, out NetworkObject prefab))
        {
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }

        if (!parked.TryGetValue(prefab, out Stack<NetworkObject> available))
        {
            available = new Stack<NetworkObject>();
            parked[prefab] = available;
        }

        // Pool is full. Genuinely destroy this one so a long session can't grow forever.
        if (available.Count >= maxPooledPerPrefab)
        {
            prefabOfInstance.Remove(instance);
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }

        instance.gameObject.SetActive(false);
        available.Push(instance);

        if (logPoolActivity)
        {
            Debug.Log($"[Pool] Parked {prefab.name}. {available.Count} now parked.");
        }
    }

    /// <summary>
    /// Releases everything this session pooled.
    ///
    /// Parked objects mostly die with the arena scene, but this component sits on a
    /// DontDestroyOnLoad object, so anything parked after that unload outlives it as an
    /// orphaned hidden GameObject. Destroying them here means a finished session leaves
    /// nothing behind for the next one, and the dictionaries cannot hold destroyed
    /// references into it either.
    /// </summary>
    private void OnDestroy()
    {
        foreach (KeyValuePair<NetworkObject, Stack<NetworkObject>> pool in parked)
        {
            foreach (NetworkObject parkedInstance in pool.Value)
            {
                // Null-checked because a scene unload may already have taken some of these,
                // and Unity's fake-null makes that invisible to a plain reference check.
                if (parkedInstance != null)
                {
                    Destroy(parkedInstance.gameObject);
                }
            }
        }

        parked.Clear();
        prefabOfInstance.Clear();
    }
}
