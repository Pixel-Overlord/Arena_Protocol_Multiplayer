using Fusion;
using UnityEngine;

/// <summary>
/// One player's personal kill score.
///
/// Lives on the player object rather than on GameStateManager because scores are now per
/// player: putting it here means it replicates to both peers for free, and the HUD reads
/// anyone's total through Runner.GetPlayerObject exactly the way it reads their health.
/// </summary>
public class PlayerScore : NetworkBehaviour
{
    [Tooltip("Points this player has earned this match. Replicated so both peers can see each other's total.")]
    [Networked] public int Score { get; set; }

    /// <summary>
    /// Starts at zero. PlayerSpawner overwrites this immediately afterwards for a rejoining
    /// player, which is how a score survives a disconnect.
    /// </summary>
    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            Score = 0;
        }
    }

    /// <summary>
    /// Adds points. Called on the host by Enemy when this player lands a killing shot;
    /// every other peer sees the new total through replication.
    /// </summary>
    public void AddScore(int points)
    {
        if (!Object.HasStateAuthority || points <= 0)
        {
            return;
        }

        Score += points;
    }

    /// <summary>
    /// Copies this player's score into a snapshot that will outlive the object. Host-side
    /// only, called just before the player is despawned on disconnect.
    /// </summary>
    public void CaptureStateInto(ref SavedPlayerState state)
    {
        state.Score = Score;
    }

    /// <summary>
    /// Restores a score saved when this player disconnected. Must run after Spawned(),
    /// which zeroes it - hence PlayerSpawner applying it once runner.Spawn has returned.
    /// </summary>
    public void RestoreState(SavedPlayerState state)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        Score = Mathf.Max(0, state.Score);
    }
}
