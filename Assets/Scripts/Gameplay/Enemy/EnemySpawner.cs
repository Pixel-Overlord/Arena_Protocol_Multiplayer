using Fusion;
using UnityEngine;

/// <summary>
/// Spawns a wave of enemies.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private NetworkPrefabRef enemyPrefab;

    [Tooltip("Where enemies appear. Cycled through if there are fewer points than enemies.")]
    [SerializeField] private Transform[] enemySpawnPoints;

    [Header("Wave sizing")]
    [Tooltip("Initial number of Enemies.")]
    [SerializeField] private int baseEnemyCount = 5;

    [Tooltip("Extra enemies added per wave.")]
    [SerializeField] private int extraEnemiesPerWave = 2;

    [Tooltip("Max number of Enemies per wave.")]
    [SerializeField] private int maxEnemiesPerWave = 15;

    /// <summary>
    /// Calculates the number of enemies in a wave based on the wave number, scaling from a base count and clamping to
    /// the allowed range.
    /// </summary>
    /// <param name="waveNumber">The wave number used to determine the enemy count.</param>
    /// <returns>The number of enemies for the specified wave</returns>
    public int GetWaveSize(int waveNumber)
    {
        int size = baseEnemyCount + Mathf.Max(0, waveNumber - 1) * extraEnemiesPerWave;

        return Mathf.Clamp(size, 1, Mathf.Max(1, maxEnemiesPerWave));
    }

    /// <summary>
    /// Spawns one wave.
    /// </summary>
    /// <returns>
    /// How many enemies actually in the arena.
    /// </returns>
    public int SpawnWave(NetworkRunner runner, int waveNumber)
    {
        // Only the state authority may spawn networked objects. Same guard as PlayerSpawner.
        if (!runner.IsServer)
        {
            return 0;
        }

        if (!enemyPrefab.IsValid)
        {
            Debug.LogError("EnemySpawner: no enemy prefab assigned.", this);
            return 0;
        }

        int waveSize = GetWaveSize(waveNumber);
        int spawnedCount = 0;

        for (int i = 0; i < waveSize; i++)
        {
            Transform spawnPoint = GetSpawnPoint(i);
            
            NetworkObject enemy = runner.Spawn(enemyPrefab, spawnPoint.position, spawnPoint.rotation);

            if (enemy == null)
            {
                Debug.LogError($"EnemySpawner: spawn returned null for enemy {i} of wave {waveNumber}.", this);
                continue;
            }

            spawnedCount++;
        }

        return spawnedCount;
    }

    /// <summary>
    /// Prepares the enemy object pool by spawning and immediately despawning the specified number of enemy instances on
    /// the server.
    /// </summary>
    /// <param name="runner">The network runner managing the networked game state.</param>
    /// <param name="count">The number of enemy objects to prewarm in the pool.</param>
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
    /// Retrieves the spawn point transform corresponding to the specified index.
    /// </summary>
    /// <param name="index">The index used to select a spawn point.</param>
    /// <returns>The transform of the selected spawn point, or the spawner's own transform if no spawn points are assigned.</returns>
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
}
