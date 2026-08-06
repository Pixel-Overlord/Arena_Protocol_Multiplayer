using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space HUD for the arena: the local player's health and power bottom-left, the
/// opponent's health top-right, and the shared team score top-centre.
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

    [Header("Opponent (top-right)")]
    [Tooltip("Parent object for the opponent's bar. Hidden while playing solo.")]
    [SerializeField] private GameObject opponentRoot;

    [SerializeField] private Image opponentHealthFill;
    [SerializeField] private TMP_Text opponentHealthText;

    [Header("Score (top-centre)")]
    [SerializeField] private TMP_Text scoreText;

    [Tooltip("Shows the current wave and how many enemies are left in it.")]
    [SerializeField] private TMP_Text waveText;

    [Header("Game over")]
    [Tooltip("Shown in the centre of the screen once every player is dead. Starts inactive.")]
    [SerializeField] private GameObject gameOverRoot;

    [Header("Colours")]
    [SerializeField] private Color shieldColor = new Color(0.2f, 0.6f, 1f);
    [SerializeField] private Color healColor = new Color(0.2f, 1f, 0.4f);

    [Tooltip("Power bar colour while the ability is spent and recovering.")]
    [SerializeField] private Color cooldownColor = new Color(0.45f, 0.45f, 0.45f);

    private NetworkRunner runner;
    private PlayerHealth localHealth;
    private PlayerAbility localAbility;
    private PlayerHealth opponentHealth;

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
        UpdateGameOver();
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
        if (localHealth != null)
        {
            return;
        }

        // PlayerSpawner calls SetPlayerObject after every successful spawn, which is what
        // makes this lookup work. Until then it returns null and the panel stays hidden.
        NetworkObject localObject = runner.GetPlayerObject(runner.LocalPlayer);

        if (localObject == null)
        {
            return;
        }

        localObject.TryGetComponent(out localHealth);
        localObject.TryGetComponent(out localAbility);
    }

    private void ResolveOpponent()
    {
        if (opponentHealth != null)
        {
            return;
        }

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer)
            {
                continue;
            }

            NetworkObject opponentObject = runner.GetPlayerObject(player);

            if (opponentObject != null && opponentObject.TryGetComponent(out opponentHealth))
            {
                return;
            }
        }
    }

    #endregion

    #region Drawing

    private void UpdateLocalPanel()
    {
        bool hasLocalPlayer = localHealth != null;

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
        if (localAbility == null)
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
        bool hasOpponent = opponentHealth != null;

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

    private void UpdateScore()
    {
        // Instance is null until the arena's GameStateManager has spawned.
        GameStateManager gameState = GameStateManager.Instance;

        if (scoreText != null)
        {
            scoreText.text = $"Score : {(gameState != null ? gameState.TeamScore : 0)}";
        }

        if (waveText == null)
        {
            return;
        }

        // WaveNumber is 0 until the match actually starts, so show the waiting state rather
        // than a nonsensical "Wave : 0".
        if (gameState == null || gameState.WaveNumber <= 0)
        {
            waveText.text = "Waiting for players...";
            return;
        }

        waveText.text = $"Wave : {gameState.WaveNumber}    Enemies : {gameState.LiveEnemyCount}";
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
