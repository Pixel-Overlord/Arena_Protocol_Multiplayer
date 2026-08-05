using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class FusionBootstrap : MonoBehaviour, IPlayerJoined
{
    public static FusionBootstrap Instance { get; private set; }

    private NetworkRunner runner;

    /// <summary>
    /// To make the NetworkRunner available to other scenes, we can use the Singleton pattern.
    /// This allows us to access the NetworkRunner from any other script in our project.
    /// </summary>
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Create a new NetworkRunner instance and attach it to this GameObject
        runner = gameObject.AddComponent<NetworkRunner>();
    }

    public void PlayerJoined(PlayerRef player)
    {
        throw new System.NotImplementedException();
    }    
}
