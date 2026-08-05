using Fusion;
using UnityEngine;

public class PlayerSpawner : SimulationBehaviour, IPlayerJoined, IPlayerLeft
{
    // Why not GameObject?
    // Because NetworkPrefabRef is a special type that allows us to reference a prefab that can be spawned over the network.
    [SerializeField] private NetworkPrefabRef playerPrefab;

    [SerializeField] private Transform[] spawnPoints;

    public void PlayerJoined(PlayerRef player)
    {
        Debug.Log("Player Joined");
        Runner.Spawn(playerPrefab, Vector3.zero, Quaternion.identity, player);
    }

    public void PlayerLeft(PlayerRef player)
    {
        Runner.Despawn(Runner.GetPlayerObject(player));
    }
}
