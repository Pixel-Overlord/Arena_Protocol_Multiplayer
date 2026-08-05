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

    private void Awake()
    {
        SetStatus(string.Empty);
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
