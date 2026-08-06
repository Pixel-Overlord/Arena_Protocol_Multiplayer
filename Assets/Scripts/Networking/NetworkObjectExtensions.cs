using Fusion;

/// <summary>
/// Liveness checks for networked objects that outlive being despawned.
///
/// PooledNetworkObjectProvider parks despawned objects with SetActive(false) instead of
/// destroying them, so a cached reference to one stays perfectly non-null after the player
/// or enemy it belonged to has gone. Reading a [Networked] property off it then throws
/// "Networked properties can only be accessed when Spawned() has been called".
///
/// Unity's fake-null is what most code leans on to notice a departed object, and pooling
/// defeats it. These are the replacement: anywhere a reference is held across frames, or
/// resolved from Runner.GetPlayerObject, test it with IsLive() before reading networked
/// state off it.
/// </summary>
public static class NetworkObjectExtensions
{
    /// <summary>
    /// True when this behaviour still belongs to a spawned object and its [Networked]
    /// properties are safe to read. Safe to call on a null reference.
    /// </summary>
    public static bool IsLive(this NetworkBehaviour behaviour)
    {
        return behaviour != null && behaviour.Object.IsLive();
    }

    /// <summary>
    /// True when this object is still spawned and its networked state is safe to read.
    /// Safe to call on a null reference.
    /// </summary>
    public static bool IsLive(this NetworkObject networkObject)
    {
        return networkObject != null && networkObject.IsValid;
    }
}
