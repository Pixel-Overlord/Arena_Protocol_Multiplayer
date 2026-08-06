using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Screen-space HUD for the arena: the local player's health, power and score bottom-left,
/// the opponent's health and score top-right. Each score is that player's own.
///
/// A plain MonoBehaviour on the PlayerUI object, not a NetworkBehaviour. The HUD only ever
/// reads replicated state and never writes it, so it needs no authority and no NetworkObject.
///
/// Updated in Update rather than FixedUpdateNetwork: this is presentation, so it should
/// refresh at the display's frame rate rather than the fixed network tick.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [Header("Local player (bottom-left)")]
    [Tooltip("Parent object for the local player's bars. Hidden until their player exists.")]
    [SerializeField] private GameObject localRoot;

    [Tooltip("Image with Image Type set to Filled, Fill Method Horizontal.")]
    [SerializeField] private Image localHealthFill;

    [SerializeField] private TMP_Text localHealthText;

    [Tooltip("Same Filled setup as the health bar. Shows the ability meter, or the cooldown refilling while it recovers.")]
    [SerializeField] private Image localPowerFill;

    [Tooltip("Reads 'Shield' or 'Heal' depending on which ability this player was assigned.")]
    [SerializeField] private TMP_Text localPowerLabel;

    // FormerlySerializedAs keeps the label that used to show the shared team total wired up:
    // without it the rename would silently drop the existing inspector reference and the
    // score would just stop appearing.
    [Tooltip("The local player's own score, next to their health bar.")]
    [FormerlySerializedAs("scoreText")]
    [SerializeField] private TMP_Text localScoreText;

    [Header("Opponent (top-right)")]
    [Tooltip("Parent object for the opponent's bar. Hidden while playing solo.")]
    [SerializeField] private GameObject opponentRoot;

    [SerializeField] private Image opponentHealthFill;
    [SerializeField] private TMP_Text opponentHealthText;

    [Tooltip("The opponent's score. Lives inside opponentRoot so it hides with them.")]
    [SerializeField] private TMP_Text opponentScoreText;

    [Header("Game over")]
    [Tooltip("Shown in the centre of the screen once every player is dead. Starts inactive.")]
    [SerializeField] private GameObject gameOverRoot;

    [Header("Connection")]
    [Tooltip("Shows this peer's own round trip time to the host. Each player sees their own number, not the other player's.")]
    [SerializeField] private TMP_Text pingText;

    [Tooltip("How often the ping readout refreshes. Refreshing every frame makes the number flicker unreadably and builds a new string each time.")]
    [SerializeField] private float pingRefreshInterval = 0.25f;

    [Header("Colours")]
    [SerializeField] private Color shieldColor = new Color(0.2f, 0.6f, 1f);
    [SerializeField] private Color healColor = new Color(0.2f, 1f, 0.4f);

    [Tooltip("Power bar colour while the ability is spent and recovering.")]
    [SerializeField] private Color cooldownColor = new Color(0.45f, 0.45f, 0.45f);

    private NetworkRunner runner;
    private PlayerHealth localHealth;
    private PlayerAbility localAbility;
    private PlayerScore localScore;
    private PlayerHealth opponentHealth;
    private PlayerScore opponentScore;

    // Unscaled so the readout keeps ticking regardless of what Time.timeScale is doing.
    private float nextPingRefreshTime;

    private void Update()
    {
        // Everything is resolved lazily and re-resolved when it goes null. The HUD exists in
        // the arena scene from the moment it loads, which is before any player has spawned,
        // and the opponent can join or leave at any point after that.
        if (!TryResolveRunner())
        {
            return;
        }

        ResolveLocalPlayer();
        ResolveOpponent();

        UpdateLocalPanel();
        UpdateOpponentPanel();
        UpdateScore();
        UpdatePing();
        UpdateGameOver();
    }

    /// <summary>
    /// Shows this peer's own round trip time to the host.
    ///
    /// Deliberately the local player's ping only. A peer can measure its own connection but
    /// not another client's - only the host can see everyone's - so showing "your ping on
    /// your screen" is the one reading that is both meaningful and symmetric.
    /// </summary>
    private void UpdatePing()
    {
        if (pingText == null || Time.unscaledTime < nextPingRefreshTime)
        {
            return;
        }

        nextPingRefreshTime = Time.unscaledTime + pingRefreshInterval;

        // The runner GameObject is DontDestroyOnLoad and exists from the menu scene onward,
        // so TryResolveRunner finding it says nothing about whether a session is actually
        // up. GetPlayerRtt reaches into connection state that does not exist before then and
        // throws a NullReferenceException from inside Fusion if it is missing.
        if (!runner.IsRunning)
        {
            pingText.text = "Ping : --";
            return;
        }

        // The host IS the game server, so asking for its round trip to itself returns zero -
        // true, but useless. The latency that actually costs the host something is the hop
        // out to the Photon cloud that relays traffic to the client, so show that instead.
        //
        // Worth knowing when reading the two screens side by side: they measure different
        // legs. The client's number is its trip to the host, which already contains this one.
        if (runner.IsServer)
        {
            pingText.text = $"Ping : {ToMilliseconds(runner.GetRttToPhotonCloud().average)} ms";
            return;
        }

        // A client mid-handshake has no round trip to report yet: LocalPlayer stays invalid
        // until the host has accepted it, and asking for the RTT of an invalid player is
        // what produces the null dereference.
        if (!runner.IsConnectedToServer || !runner.LocalPlayer.IsRealPlayer)
        {
            pingText.text = "Ping : --";
            return;
        }

        pingText.text = $"Ping : {ToMilliseconds(runner.GetPlayerRtt(runner.LocalPlayer))} ms";
    }

    /// <summary>
    /// Fusion reports every round trip time in seconds. A ping readout is far easier to
    /// judge in whole milliseconds, so both call sites above convert through here.
    /// </summary>
    private static int ToMilliseconds(double seconds)
    {
        return (int)System.Math.Round(seconds * 1000.0);
    }

    /// <summary>
    /// Reveals the GAME OVER banner once the match has ended. Driven off the replicated
    /// MatchState, so both peers show it at the same moment without any extra messaging.
    /// </summary>
    private void UpdateGameOver()
    {
        if (gameOverRoot != null)
        {
            gameOverRoot.SetActive(GameStateManager.IsMatchOver);
        }
    }

    #region Resolving references

    private bool TryResolveRunner()
    {
        if (runner != null)
        {
            return true;
        }

        // The runner is created in the menu scene and marked DontDestroyOnLoad, so it is
        // already alive by the time the arena loads - it just isn't in this scene.
        runner = FindObjectOfType<NetworkRunner>();

        return runner != null;
    }

    private void ResolveLocalPlayer()
    {
        // IsLive rather than a null check throughout this class: a departed player's object
        // is parked by the pool, not destroyed, so the cached reference never goes null.
        if (localHealth.IsLive())
        {
            return;
        }

        // Cleared rather than left to be overwritten: if the lookup below finds nothing,
        // these must end up null so the panel hides instead of drawing a despawned player.
        localHealth = null;
        localAbility = null;
        localScore = null;

        // PlayerSpawner calls SetPlayerObject after every successful spawn, which is what
        // makes this lookup work. Until then it returns null and the panel stays hidden.
        NetworkObject localObject = runner.GetPlayerObject(runner.LocalPlayer);

        if (!localObject.IsLive())
        {
            return;
        }

        localObject.TryGetComponent(out localHealth);
        localObject.TryGetComponent(out localAbility);
        localObject.TryGetComponent(out localScore);
    }

    private void ResolveOpponent()
    {
        if (opponentHealth.IsLive())
        {
            return;
        }

        opponentHealth = null;
        opponentScore = null;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer)
            {
                continue;
            }

            NetworkObject opponentObject = runner.GetPlayerObject(player);

            if (!opponentObject.IsLive())
            {
                continue;
            }

            if (opponentObject.TryGetComponent(out opponentHealth))
            {
                // Picked up together with the health so the two can never drift apart - the
                // liveness check above re-resolves both at once when the opponent leaves.
                opponentObject.TryGetComponent(out opponentScore);
                return;
            }
        }
    }

    #endregion

    #region Drawing

    private void UpdateLocalPanel()
    {
        bool hasLocalPlayer = localHealth.IsLive();

        if (localRoot != null)
        {
            localRoot.SetActive(hasLocalPlayer);
        }

        if (!hasLocalPlayer)
        {
            return;
        }

        SetBar(localHealthFill, localHealthText, localHealth);

        UpdatePowerBar();
    }

    private void UpdatePowerBar()
    {
        if (!localAbility.IsLive())
        {
            return;
        }

        bool isHeal = localAbility.Type == PlayerAbility.AbilityType.Heal;

        if (localPowerLabel != null)
        {
            localPowerLabel.text = isHeal ? "Heal" : "Shield";
        }

        if (localPowerFill == null)
        {
            return;
        }

        if (localAbility.IsOnCooldown)
        {
            // Show the recovery running down instead of an empty bar, so the player can see
            // when the ability comes back. 1 - remaining makes it fill up towards ready.
            localPowerFill.fillAmount = 1f - localAbility.CooldownNormalized;
            localPowerFill.color = cooldownColor;
            return;
        }

        localPowerFill.fillAmount = localAbility.MeterNormalized;
        localPowerFill.color = isHeal ? healColor : shieldColor;
    }

    private void UpdateOpponentPanel()
    {
        bool hasOpponent = opponentHealth.IsLive();

        if (opponentRoot != null)
        {
            opponentRoot.SetActive(hasOpponent);
        }

        if (!hasOpponent)
        {
            return;
        }

        SetBar(opponentHealthFill, opponentHealthText, opponentHealth);
    }

    /// <summary>
    /// Draws both players' scores. Each score is replicated on its own player object, so no
    /// peer has to ask the host for the other's number.
    /// </summary>
    private void UpdateScore()
    {
        if (localScoreText != null)
        {
            localScoreText.text = $"Score : {(localScore.IsLive() ? localScore.Score : 0)}";
        }

        if (opponentScoreText != null)
        {
            opponentScoreText.text = $"Score : {(opponentScore.IsLive() ? opponentScore.Score : 0)}";
        }
    }

    /// <summary>
    /// Fills a bar and its label from a PlayerHealth. Shared by both panels so the local and
    /// opponent bars can never drift apart in how they round or clamp.
    /// </summary>
    private void SetBar(Image fill, TMP_Text label, PlayerHealth health)
    {
        // MaxHealth is serialized per prefab, so guard against a zero that would divide badly.
        float maxHealth = health.MaxHealth > 0f ? health.MaxHealth : 1f;
        float normalized = Mathf.Clamp01(health.currentHealth / maxHealth);

        if (fill != null)
        {
            fill.fillAmount = normalized;
        }

        if (label != null)
        {
            // CeilToInt so a player on a sliver of health reads "1" rather than a
            // demoralising "0" while still alive.
            label.text = $"{Mathf.CeilToInt(health.currentHealth)} / {Mathf.RoundToInt(maxHealth)}";
        }
    }

    #endregion
}
