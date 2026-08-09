using System.Collections.Generic;
using Fusion;

/// <summary>
/// Represents the state of a player at the moment of disconnect,
/// including health, ability type, ability meter, and
/// remaining cooldown.
/// </summary>
public struct SavedPlayerState
{
    public float Health;

    public PlayerAbility.AbilityType AbilityType;

    public float AbilityMeter;

    public float AbilityCooldownRemainingSeconds;
}

/// <summary>
/// Stores and manages the state of players in a multiplayer session, enabling state persistence across disconnects and
/// rejoins.
/// </summary>
public class PlayerStateStore
{
    /// Stores the saved states of players by their user IDs.
    private readonly Dictionary<string, SavedPlayerState> savedStatesByUserId =
        new Dictionary<string, SavedPlayerState>();

    /// Stores user IDs associated with player references. 
    private readonly Dictionary<PlayerRef, string> userIdByPlayerRef =
        new Dictionary<PlayerRef, string>();

    /// <summary>
    /// Associates the user ID retrieved from the specified network runner with the given player reference.
    /// </summary>
    /// <param name="runner">The network runner instance used to obtain the user ID.</param>
    /// <param name="player">The player reference for which to store the user ID.</param>
    public void RememberUserId(NetworkRunner runner, PlayerRef player)
    {
        string userId = runner.GetPlayerUserId(player);

        if (!string.IsNullOrEmpty(userId))
        {
            userIdByPlayerRef[player] = userId;
        }
    }

    /// <summary>
    /// Saves the specified player's state using their resolved user ID.
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
    /// Attempts to remove and retrieve the saved player state for the specified player.
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

    public void Clear()
    {
        savedStatesByUserId.Clear();
        userIdByPlayerRef.Clear();
    }

    /// <summary>
    /// Resolves the user ID associated with the specified player reference, caching the result for future lookups.
    /// </summary>
    /// <returns>The resolved user ID, or null if not found.</returns>
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
