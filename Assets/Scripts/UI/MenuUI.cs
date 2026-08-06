using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MenuUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_Text statusText;

    private const string DefaultRoomName = "ArenaRoom";

    /// <summary>
    /// Puts the menu into a usable state and shows why the last session ended, if it ended
    /// for a reason worth mentioning.
    ///
    /// Deliberately Start and not Awake: the bootstrap that survived the last session hands
    /// over to a fresh one during Awake, and only by Start is FusionBootstrap.Instance
    /// guaranteed to be the new one holding the message.
    /// </summary>
    private void Start()
    {
        SetInteractable(true);

        string message = FusionBootstrap.Instance != null
            ? FusionBootstrap.Instance.ConsumeStatusMessage()
            : null;

        SetStatus(message ?? string.Empty);
    }

    public void OnHostClicked()
    {
        string roomName = ResolveRoomName();

        SetInteractable(false);
        SetStatus($"Creating room '{roomName}'...");

        FusionBootstrap.Instance.StartHost(roomName);
    }

    public void OnJoinClicked()
    {
        string roomName = ResolveRoomName();

        SetInteractable(false);
        SetStatus($"Joining room '{roomName}'...");

        FusionBootstrap.Instance.StartClient(roomName);
    }

    public void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Falls back to a shared default so an empty field still lets both peers
    /// meet in the same session.
    /// </summary>
    private string ResolveRoomName()
    {
        return string.IsNullOrWhiteSpace(roomNameInput.text)
            ? DefaultRoomName
            : roomNameInput.text.Trim();
    }

    private void SetInteractable(bool value)
    {
        hostButton.interactable = value;
        joinButton.interactable = value;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
