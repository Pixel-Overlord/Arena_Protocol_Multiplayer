using System.Collections.Generic;
using Fusion;

/// <summary>
/// What one player carries across a disconnect.
///
/// Plain fields rather than [Networked] properties on purpose: the whole point is that
/// this data outlives the NetworkObject it came from, which Fusion destroys the instant
/// the player leaves.
/// </summary>
public struct SavedPlayerState
{
    /// <summary>
    /// Health at the moment of disconnect, or 0 for a player who was already dead.
    /// Zero is treated as "no health to restore" - a rejoining corpse comes back at full
    /// health instead, because this game has no respawn to bring them back any other way.
    /// </summary>
    public float Health;

    /// <summary>
    /// Which ability they had. Restored alongside the cooldown below, because a cooldown
    /// only makes sense attached to the ability that spent it - coming back as Heal with a
    /// Shield's cooldown running would be nonsense.
    /// </summary>
    public PlayerAbility.AbilityType AbilityType;

    /// <summary>How much active duration was left mid-use, or 0 if the ability was idle.</summary>
    public float AbilityMeter;

    /// <summary>
    /// Cooldown left in seconds rather than as a TickTimer. A TickTimer is anchored to a
    /// tick number in the running session and keeps counting down while the player is away,
    /// so it would read as long expired by the time they came back.
    /// </summary>
    public float AbilityCooldownRemainingSeconds;
}

/// <summary>
/// Host-side memory of players who dropped out, so they can be restored when they rejoin.
///
/// Keyed by Photon UserId rather than PlayerRef, because a PlayerRef is only valid for one
/// connection - a rejoining player always arrives with a new one. The UserId comes from
/// FusionBootstrap.GetOrCreateLocalUserId and is stable across app restarts, which is what
/// makes "close the game, relaunch, rejoin" work and not just "alt-tab away".
///
/// A plain C# class, not a MonoBehaviour: it is owned by FusionBootstrap and has no reason
/// to be a scene object.
/// </summary>
public class PlayerStateStore
{
    private readonly Dictionary<string, SavedPlayerState> savedStatesByUserId =
        new Dictionary<string, SavedPlayerState>();

    // PlayerRef -> UserId, captured while the player is still connected. This cache is the
    // reason the leave path works at all: Runner.GetPlayerUserId stops resolving once the
    // player has gone, and leaving is exactly when the id is needed.
    private readonly Dictionary<PlayerRef, string> userIdByPlayerRef =
        new Dictionary<PlayerRef, string>();

    /// <summary>
    /// Records a connected player's UserId. Call from OnPlayerJoined (host only) so the
    /// mapping is already cached by the time they disconnect.
    /// </summary>
    public void RememberUserId(NetworkRunner runner, PlayerRef player)
    {
        string userId = runner.GetPlayerUserId(player);

        if (!string.IsNullOrEmpty(userId))
        {
            userIdByPlayerRef[player] = userId;
        }
    }

    /// <summary>
    /// Stores a leaving player's state so a later rejoin can pick it up. Does nothing if
    /// the player has no resolvable UserId, in which case they simply come back as new.
    /// </summary>
    public void Save(NetworkRunner runner, PlayerRef player, SavedPlayerState state)
    {
        string userId = ResolveUserId(runner, player);

        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        savedStatesByUserId[userId] = state;

        // That PlayerRef will never be seen again - the same person rejoins under a new one.
        userIdByPlayerRef.Remove(player);
    }

    /// <summary>
    /// Hands back the state saved for this player, if any, and removes it from the store.
    ///
    /// Consumed rather than copied so a stale snapshot can never be applied twice - once it
    /// has been restored onto a live player object, that object is the source of truth.
    /// </summary>
    public bool TryTake(NetworkRunner runner, PlayerRef player, out SavedPlayerState state)
    {
        string userId = ResolveUserId(runner, player);

        if (string.IsNullOrEmpty(userId) || !savedStatesByUserId.TryGetValue(userId, out state))
        {
            state = default;
            return false;
        }

        savedStatesByUserId.Remove(userId);
        return true;
    }

    /// <summary>
    /// Wipes everything. Called when the session ends, because a snapshot only means
    /// anything inside the match it was taken in.
    /// </summary>
    public void Clear()
    {
        savedStatesByUserId.Clear();
        userIdByPlayerRef.Clear();
    }

    /// <summary>
    /// Prefers the cached id and falls back to asking the runner, which covers a player who
    /// joined before anything thought to cache them.
    /// </summary>
    private string ResolveUserId(NetworkRunner runner, PlayerRef player)
    {
        if (userIdByPlayerRef.TryGetValue(player, out string cachedUserId))
        {
            return cachedUserId;
        }

        string userId = runner.GetPlayerUserId(player);

        if (!string.IsNullOrEmpty(userId))
        {
            userIdByPlayerRef[player] = userId;
        }

        return userId;
    }
}
