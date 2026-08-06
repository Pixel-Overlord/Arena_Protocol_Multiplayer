using Fusion;
using UnityEngine;

/// <summary>
/// Gates the start of the match. Nothing in the project detected "everyone has arrived"
/// before this - players simply spawned as they connected. The first enemy wave waits on
/// State flipping to InProgress.
///
/// Must live on a scene NetworkObject in Arena.unity. A plain MonoBehaviour or a bare
/// SimulationBehaviour will never receive Fusion callbacks - that is the exact trap called
/// out in PlayerSpawner's header comment.
/// </summary>
public class GameStateManager : NetworkBehaviour
{
    public enum MatchState
    {
        WaitingForPlayers,
        InProgress,
        Ended
    }

    /// <summary>
    /// The one GameStateManager in the arena. Mirrors how FusionBootstrap.Instance is used,
    /// and saves every enemy from running FindObjectOfType just to report a kill.
    /// </summary>
    public static GameStateManager Instance { get; private set; }

    /// <summary>
    /// True once every player has died. Read by Enemy, EnemyWeapon, Projectile, Weapon and
    /// PlayerMovement to freeze the whole arena - "nothing should move" once GAME OVER shows.
    ///
    /// Deliberately tolerant of a missing or despawned manager: before the match object
    /// exists nothing is frozen, which is the safe default.
    /// </summary>
    public static bool IsMatchOver
    {
        get
        {
            if (Instance == null || Instance.Object == null || !Instance.Object.IsValid)
            {
                return false;
            }

            return Instance.State == MatchState.Ended;
        }
    }

    [Tooltip("Replicated so clients can show a 'waiting for opponent' message later.")]
    [Networked] public MatchState State { get; set; }

    [Tooltip("Shared team score - both players contribute to the same number. Replicated so each peer's HUD shows the same total. Later phases will also add to this from powerups.")]
    [Networked] public int TeamScore { get; set; }

    [Tooltip("How many players must be in the session before the first wave spawns. Set to 1 while testing solo in the editor.")]
    [SerializeField] private int requiredPlayers = 2;

    [SerializeField] private EnemySpawner enemySpawner;

    public override void Spawned()
    {
        Instance = this;

        // Enums default to their zero value, which is already WaitingForPlayers. Setting it
        // explicitly matters because a pooled/reloaded scene object could carry stale state,
        // and because it documents the starting point.
        if (Object.HasStateAuthority)
        {
            State = MatchState.WaitingForPlayers;
            TeamScore = 0;
        }
    }

    /// <summary>
    /// Clears the static reference so a second session can't reach into the dead one.
    /// </summary>
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Watches for the whole team being wiped out. Runs only on the host; every other peer
    /// learns the match ended through the replicated State property.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || State != MatchState.InProgress)
        {
            return;
        }

        int playerObjectCount = 0;
        int aliveCount = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);

            if (playerObject == null || !playerObject.TryGetComponent(out PlayerHealth health))
            {
                continue;
            }

            playerObjectCount++;

            if (!health.isDead)
            {
                aliveCount++;
            }
        }

        // Nothing spawned yet. Without this the match would end on the very first tick after
        // State flips to InProgress, because the player objects are not resolvable yet.
        if (playerObjectCount == 0)
        {
            return;
        }

        if (aliveCount > 0)
        {
            return;
        }

        State = MatchState.Ended;
    }

    /// <summary>
    /// Adds to the shared team score. Called by Enemy on death, and later by powerups.
    /// </summary>
    public void AddScore(int points)
    {
        // Only the host may write networked state. Clients receive the new total through
        // replication, so there is nothing for them to do here.
        if (!Object.HasStateAuthority || points <= 0)
        {
            return;
        }

        TeamScore += points;
    }

    /// <summary>
    /// Called by PlayerSpawner every time a player successfully spawns.
    /// </summary>
    public void NotifyPlayerSpawned(NetworkRunner runner)
    {
        // Only the host decides when the match starts. Clients are told via the
        // replicated State property.
        if (!runner.IsServer)
        {
            return;
        }

        // Already running. Without this guard a player rejoining mid-match would fire a
        // second opening wave on top of the enemies already in the arena.
        if (State != MatchState.WaitingForPlayers)
        {
            return;
        }

        // ActivePlayers is an IEnumerable<PlayerRef>. Counted with a plain foreach rather
        // than Linq's Count() to avoid the per-call allocation - this runs on the host.
        int playerCount = 0;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            playerCount++;
        }

        if (playerCount < requiredPlayers)
        {
            return;
        }

        State = MatchState.InProgress;

        if (enemySpawner == null)
        {
            Debug.LogError("GameStateManager: no EnemySpawner assigned, match started with no enemies.", this);
            return;
        }

        enemySpawner.SpawnInitialWave(runner);
    }
}
