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

    [Tooltip("Shared team score - both players feed the same number, and it only comes from collecting energy orbs. Replicated so each peer's HUD shows the same total.")]
    [Networked] public int TeamScore { get; set; }

    [Tooltip("Which wave is running. 0 before the first one spawns.")]
    [Networked] public int WaveNumber { get; set; }

    [Tooltip("Enemies still alive in the current wave. Replicated so the HUD can show it without asking the host.")]
    [Networked] public int LiveEnemyCount { get; set; }

    [Tooltip("Counts down the breather between one wave being cleared and the next spawning.")]
    [Networked] private TickTimer waveBreakTimer { get; set; }

    [Tooltip("True while waiting out the gap between waves. Needed because a TickTimer that was never started also reports 'expired', which would otherwise spawn the next wave instantly.")]
    [Networked] private NetworkBool waveBreakPending { get; set; }

    [Tooltip("How many players must be in the session before the first wave spawns. Set to 1 while testing solo in the editor.")]
    [SerializeField] private int requiredPlayers = 2;

    [Tooltip("Breather between clearing a wave and the next one arriving.")]
    [SerializeField] private float secondsBetweenWaves = 3f;

    [SerializeField] private EnemySpawner enemySpawner;

    // Host-only rather than [Networked]: only the state authority runs the end-of-match
    // check, and this project does not do host migration, so no peer needs to read it.
    private bool anyPlayerHasSpawned;

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
            WaveNumber = 0;
            LiveEnemyCount = 0;
            waveBreakPending = false;
            anyPlayerHasSpawned = false;
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

            // IsLive rather than a null check: a despawned player object is parked by the
            // pool rather than destroyed, so it can still be handed back here, and reading
            // isDead off it would throw and take the whole simulation with it.
            if (!playerObject.IsLive() || !playerObject.TryGetComponent(out PlayerHealth health))
            {
                continue;
            }

            playerObjectCount++;

            if (!health.isDead)
            {
                aliveCount++;
            }
        }

        if (playerObjectCount > 0)
        {
            anyPlayerHasSpawned = true;
        }

        // Nothing spawned yet. Without this the match would end on the very first tick after
        // State flips to InProgress, because the player objects are not resolvable yet.
        //
        // The latch is what separates that from "everyone has left": once a player has been
        // seen, playerObjectCount dropping back to zero is a real end condition and falls
        // through to the aliveCount check below.
        if (!anyPlayerHasSpawned)
        {
            return;
        }

        if (aliveCount == 0)
        {
            State = MatchState.Ended;
            return;
        }

        TickWaveLoop();
    }

    /// <summary>
    /// Spawns the next wave once the arena has been cleared and the breather has elapsed.
    ///
    /// Only reached while State is InProgress, which is what stops a fresh wave arriving
    /// after GAME OVER.
    /// </summary>
    private void TickWaveLoop()
    {
        // A wave is still being fought.
        if (LiveEnemyCount > 0)
        {
            return;
        }

        // Nothing is waiting to spawn. This is the guard that matters: without it, a
        // TickTimer that was never started reads as expired and wave 2 would appear the
        // instant wave 1 spawned.
        if (!waveBreakPending)
        {
            return;
        }

        if (!waveBreakTimer.Expired(Runner))
        {
            return;
        }

        StartNextWave();
    }

    /// <summary>
    /// Advances the wave counter and asks the spawner to fill the arena.
    /// </summary>
    private void StartNextWave()
    {
        if (enemySpawner == null)
        {
            Debug.LogError("GameStateManager: no EnemySpawner assigned, no enemies will spawn.", this);
            return;
        }

        waveBreakPending = false;
        WaveNumber++;

        // Trusting the spawner's return value rather than the requested size: if a spawn
        // failed, counting it would leave LiveEnemyCount permanently above zero and the
        // wave loop would stall waiting for a kill that can never happen.
        LiveEnemyCount = enemySpawner.SpawnWave(Runner, WaveNumber);

        if (LiveEnemyCount <= 0)
        {
            Debug.LogError($"GameStateManager: wave {WaveNumber} spawned no enemies, wave loop has stalled.", this);
        }
    }

    /// <summary>
    /// Called by Enemy when it dies. Starts the breather once the wave is wiped out.
    /// </summary>
    public void NotifyEnemyKilled()
    {
        if (!Object.HasStateAuthority || State != MatchState.InProgress)
        {
            return;
        }

        // Never below zero - a double-report would otherwise push the count negative and
        // the "wave cleared" test would still pass, spawning waves early.
        LiveEnemyCount = Mathf.Max(0, LiveEnemyCount - 1);

        if (LiveEnemyCount > 0)
        {
            return;
        }

        waveBreakPending = true;
        waveBreakTimer = TickTimer.CreateFromSeconds(Runner, secondsBetweenWaves);
    }

    /// <summary>
    /// Adds to the shared team score. Called by EnergyOrb when either player collects one -
    /// orbs are the only thing that scores, so killing enemies deliberately awards nothing.
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

        StartNextWave();
    }
}
