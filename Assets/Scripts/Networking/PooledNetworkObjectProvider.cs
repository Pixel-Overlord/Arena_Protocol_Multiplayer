using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a pool of network objects to optimize instantiation and destruction.
/// </summary>
/// <remarks>Allows reusing instances of network objects, reducing the overhead of frequent instantiation and
/// destruction.</remarks>
public class PooledNetworkObjectProvider : NetworkObjectProviderDefault
{
    [Tooltip("Most instances of one prefab kept parked. Anything beyond this is really destroyed so the pool can't grow without bound.")]
    [SerializeField] private int maxPooledPerPrefab = 32;

    /// Maps a prefab to its parked instances for reuse.
    [Tooltip("Contains a specific number of prefabs instantiated. We will pop out 'Stack of <EnemyNetworkObject>' with key of EnemyNetworkObject.")]
    private Dictionary<NetworkObject, Stack<NetworkObject>> parked =
        new Dictionary<NetworkObject, Stack<NetworkObject>>();

    // Maps an instance to its prefab for proper tracking when despawning.
    private Dictionary<NetworkObject, NetworkObject> prefabOfInstance =
        new Dictionary<NetworkObject, NetworkObject>();

    /// <summary>
    /// Instantiates a networked prefab or reuses a parked instance if available.
    /// </summary>
    /// <param name="runner">The network runner managing the networked objects.</param>
    /// <param name="prefab">The prefab to instantiate or reuse.</param>
    /// <returns>The instantiated or reused NetworkObject.</returns>
    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        // Try to reuse a parked instance of the prefab if available
        // Note : Initally since parked is empty, this will not be true. But when DestroyPrefabInstance is called,
        // the prefab will be pushed into the parked dictionary and this will be true next time.
        if (parked.TryGetValue(prefab, out Stack<NetworkObject> availablePrefabInstane))
        {
            // Reuse a parked instance if available 
            while (availablePrefabInstane.Count > 0)
            {
                NetworkObject enemyNetworkObject = availablePrefabInstane.Pop();

                if (enemyNetworkObject == null)
                {
                    continue;
                }

                enemyNetworkObject.gameObject.SetActive(true);

                // Record the prefab of the reused instance for proper tracking
                return enemyNetworkObject;
            }
        }

        NetworkObject newInstanceOfEnemy = base.InstantiatePrefab(runner, prefab);

        if (newInstanceOfEnemy != null)
        {
            prefabOfInstance[newInstanceOfEnemy] = prefab;
        }

        return newInstanceOfEnemy;
    }

    /// <summary>
    /// Destroys or pools a prefab instance, optimizing memory usage and preventing leaks by reusing objects when
    /// possible.
    /// </summary>
    /// <remarks>If the pool for a prefab reaches its maximum capacity, the instance is destroyed to avoid
    /// unbounded memory growth.</remarks>
    /// <param name="runner">The network runner managing the networked object lifecycle.</param>
    /// <param name="prefabId">The identifier of the prefab to destroy or pool.</param>
    /// <param name="instance">The prefab instance to destroy or return to the pool.</param>
    protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject NetworkObjectInstance)
    {
        // If the instance is null or not tracked, fall back to the base destruction to handle it.
        // and to avoid memory leaks.
        if (NetworkObjectInstance == null || !prefabOfInstance.TryGetValue(NetworkObjectInstance, out NetworkObject prefab))
        {
            base.DestroyPrefabInstance(runner, prefabId, NetworkObjectInstance);
            return;
        }

        if (!parked.TryGetValue(prefab, out Stack<NetworkObject> availablePrefabInstance))
        {
            // Create a new stack for this prefab if it doesn't exist yet
            availablePrefabInstance = new Stack<NetworkObject>();
            parked[prefab] = availablePrefabInstance;
        }

        // If the pool for this prefab has reached its maximum capacity,
        // destroy the instance instead of pooling it.
        if (availablePrefabInstance.Count >= maxPooledPerPrefab)
        {
            prefabOfInstance.Remove(NetworkObjectInstance);
            base.DestroyPrefabInstance(runner, prefabId, NetworkObjectInstance);
            return;
        }

        NetworkObjectInstance.gameObject.SetActive(false);

        // Add the instance into the stack of similar Network Objects.
        availablePrefabInstance.Push(NetworkObjectInstance);
    }

    /// <summary>
    /// Cleans up and destroys all parked network objects and clears internal collections when the provider is destroyed.
    /// It is called by Unity when the GameObject this script is attached to is destroyed,
    /// ensuring that all pooled objects are properly cleaned up to prevent memory leaks.
    /// </summary>
    private void OnDestroy()
    {
        foreach (KeyValuePair<NetworkObject, Stack<NetworkObject>> pool in parked)
        {
            foreach (NetworkObject parkedInstance in pool.Value)
            {
                // Destroy the parked instance if it still exists to prevent memory leaks.
                {
                    Destroy(parkedInstance.gameObject);
                }
            }
        }

        parked.Clear();
        prefabOfInstance.Clear();
    }
}
