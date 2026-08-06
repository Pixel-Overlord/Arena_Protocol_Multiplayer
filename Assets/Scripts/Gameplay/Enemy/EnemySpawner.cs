using Fusion;
using UnityEngine;

/// <summary>
/// Spawns the opening batch of enemies once GameStateManager says the match has started.
///
/// Deliberately shaped like PlayerSpawner: a plain MonoBehaviour living in Arena.unity,
/// driven explicitly by another script rather than by Fusion callbacks, with an
/// authority guard on every spawn.
///
/// "Returning to the pool" is just Runner.Despawn - PooledNetworkObjectProvider parks the
/// instance instead of destroying it, so no extra bookkeeping belongs here.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private NetworkPrefabRef enemyPrefab;

    [Tooltip("Where enemies appear. Cycled through if there are fewer points than enemies.")]
    [SerializeField] private Transform[] enemySpawnPoints;

    [Tooltip("How many enemies the first wave puts in the arena.")]
    [SerializeField] private int initialEnemyCount = 5;

    [Tooltip("Random sideways offset applied around each spawn point so enemies sharing a point don't stack inside one another.")]
    [SerializeField] private float spawnScatterRadius = 2f;

    /// <summary>
    /// Phase 1.2 - called once both players have reached the game.
    /// </summary>
    public void SpawnInitialWave(NetworkRunner runner)
    {
        // Only the state authority may spawn networked objects. Same guard as PlayerSpawner.
        if (!runner.IsServer)
        {
            return;
        }

        if (!enemyPrefab.IsValid)
        {
            Debug.LogError("EnemySpawner: no enemy prefab assigned.", this);
            return;
        }

        for (int i = 0; i < initialEnemyCount; i++)
        {
            Transform spawnPoint = GetSpawnPoint(i);
            Vector3 position = ScatterAround(spawnPoint.position);

            // No input authority argument: enemies are host-driven AI and consume no
            // player input, so the default (PlayerRef.None) is correct.
            NetworkObject enemy = runner.Spawn(enemyPrefab, position, spawnPoint.rotation);

            if (enemy == null)
            {
                Debug.LogError($"EnemySpawner: spawn returned null for enemy {i}.", this);
            }
        }
    }

    /// <summary>
    /// Optional warm-up: create and immediately release N instances so the pool is already
    /// populated and the first real wave costs no Instantiate calls. Pure optimisation -
    /// nothing breaks if this is never called.
    /// </summary>
    public void PrewarmPool(NetworkRunner runner, int count)
    {
        if (!runner.IsServer)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // Spawned far below the arena so the single frame they exist is invisible.
            NetworkObject enemy = runner.Spawn(enemyPrefab, Vector3.down * 1000f, Quaternion.identity);

            if (enemy != null)
            {
                runner.Despawn(enemy);
            }
        }
    }

    /// <summary>
    /// Cycles through the assigned points so N enemies spread across M spawn locations.
    /// Mirrors PlayerSpawner.GetSpawnPoint, including its fallback so a missing array
    /// can never null-reference.
    /// </summary>
    private Transform GetSpawnPoint(int index)
    {
        if (enemySpawnPoints == null || enemySpawnPoints.Length == 0)
        {
            Debug.LogWarning("EnemySpawner: no spawn points assigned, using own transform.", this);
            return transform;
        }

        int wrapped = index % enemySpawnPoints.Length;
        return enemySpawnPoints[wrapped] != null ? enemySpawnPoints[wrapped] : transform;
    }

    /// <summary>
    /// Nudges a spawn position by a random amount on the XZ plane. Height is untouched so
    /// enemies still land on the floor rather than inside or above it.
    /// </summary>
    private Vector3 ScatterAround(Vector3 origin)
    {
        if (spawnScatterRadius <= 0f)
        {
            return origin;
        }

        Vector2 offset = Random.insideUnitCircle * spawnScatterRadius;
        return origin + new Vector3(offset.x, 0f, offset.y);
    }
}
