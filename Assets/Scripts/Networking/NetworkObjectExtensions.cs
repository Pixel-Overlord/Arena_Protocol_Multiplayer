using Fusion;

/// <summary>
/// Provides extension methods for determining the live state of network objects and behaviors.
/// </summary>
public static class NetworkObjectExtensions
{
    /// <summary>
    /// Determines whether the specified network behaviour instance is live.
    /// </summary>
    /// <returns>true if the network behaviour is not null and its associated object is live; otherwise, false.</returns>
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
