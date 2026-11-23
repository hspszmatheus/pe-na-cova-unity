using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PlayerNameUIManager : MonoBehaviour
{
    public const string SavedNicknamePrefKey = "SavedPlayerNickname";
    public static string CurrentNickname { get; private set; } = string.Empty;

    private UIDocument uiDocument;
    private TextField nameInputField;
    private Button confirmButton;

    private static string GetNicknamePrefKey()
    {
        string profile = MultiplayerPlayModeProfileManager.GetProfile();
        return $"{SavedNicknamePrefKey}_{profile}";
    }

    public static string GetActivePlayerNickname()
    {
        if (!string.IsNullOrEmpty(CurrentNickname)) return CurrentNickname;

        string prefKey = GetNicknamePrefKey();
        string saved = PlayerPrefs.GetString(prefKey, "");
        if (string.IsNullOrEmpty(saved))
        {
            saved = PlayerPrefs.GetString(SavedNicknamePrefKey, "");
        }

        if (!string.IsNullOrEmpty(saved))
        {
            CurrentNickname = saved;
            return saved;
        }

        try
        {
            if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                string authName = AuthenticationService.Instance.PlayerName;
                if (!string.IsNullOrEmpty(authName))
                {
                    int hashIdx = authName.IndexOf('#');
                    string clean = hashIdx > 0 ? authName.Substring(0, hashIdx) : authName;
                    CurrentNickname = clean;
                    return clean;
                }
            }
        }
        catch {}

        string profile = MultiplayerPlayModeProfileManager.GetProfile();
        if (profile.StartsWith("Build_Player_"))
        {
            string slotNum = profile.Substring("Build_Player_".Length);
            CurrentNickname = $"Player {slotNum}";
            return CurrentNickname;
        }

        return "Player";
    }

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;
        var root = uiDocument.rootVisualElement;

        // Pega as referências da UI
        nameInputField = root.Q<TextField>("input-player-name");
        confirmButton = root.Q<Button>("btn-confirm-name");

        if (nameInputField != null)
        {
            string prefKey = GetNicknamePrefKey();
            string savedName = PlayerPrefs.GetString(prefKey, "");
            if (string.IsNullOrEmpty(savedName))
            {
                savedName = GetActivePlayerNickname();
            }

            if (!string.IsNullOrEmpty(savedName))
            {
                nameInputField.value = savedName;
                CurrentNickname = savedName;
            }

            nameInputField.RegisterValueChangedCallback(OnNameInputValueChanged);
        }

        if (confirmButton != null)
        {
            confirmButton.clicked += OnConfirmButtonClicked;
        }
    }

    private void OnDisable()
    {
        if (confirmButton != null)
        {
            confirmButton.clicked -= OnConfirmButtonClicked;
        }

        if (nameInputField != null)
        {
            nameInputField.UnregisterValueChangedCallback(OnNameInputValueChanged);
        }
    }

    public static void SaveDraftNickname(string rawName)
    {
        string newName = rawName != null ? rawName.Trim() : string.Empty;
        if (string.IsNullOrEmpty(newName))
            return;

        CurrentNickname = newName;
        PlayerPrefs.SetString(GetNicknamePrefKey(), newName);
        PlayerPrefs.Save();
    }

    public async Task ApplyNicknameAsync(string rawName)
    {
        string typedName = rawName != null ? rawName.Trim() : string.Empty;
        if (string.IsNullOrEmpty(typedName))
        {
            string fallback = GetActivePlayerNickname();
            Debug.Log($"Nome vazio. Mantendo o nome atual: {fallback}");
            return;
        }

        SaveDraftNickname(typedName);

        // Aguarda breve janela caso o usuário clique rápido antes da autenticação anônima finalizar
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Application.isPlaying && Time.realtimeSinceStartup < deadline)
        {
            if (UnityServices.State == ServicesInitializationState.Initialized
                && AuthenticationService.Instance != null
                && AuthenticationService.Instance.IsSignedIn)
            {
                break;
            }
            await Task.Delay(100);
        }

        if (UnityServices.State != ServicesInitializationState.Initialized
            || AuthenticationService.Instance == null
            || !AuthenticationService.Instance.IsSignedIn)
        {
            return;
        }

        try
        {
            string authSafeName = SanitizeForAuthPlayerName(typedName);
            if (string.IsNullOrEmpty(authSafeName))
                return;

            // A API nativa para trocar o nome do jogador já autenticado (UGS não permite espaços)
            await AuthenticationService.Instance.UpdatePlayerNameAsync(authSafeName);
            Debug.Log($"Nome atualizado com sucesso para: {AuthenticationService.Instance.PlayerName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Erro ao atualizar nome: {e.Message}");
        }
    }

    private static string SanitizeForAuthPlayerName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        var chars = new char[rawName.Length];
        int len = 0;
        bool prevUnderscore = false;
        foreach (char c in rawName.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevUnderscore && len > 0)
                {
                    chars[len++] = '_';
                    prevUnderscore = true;
                }
            }
            else
            {
                chars[len++] = c;
                prevUnderscore = false;
            }
        }

        string sanitized = new string(chars, 0, len).Trim('_');
        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }

    private void OnNameInputValueChanged(ChangeEvent<string> evt)
    {
        SaveDraftNickname(evt.newValue);
    }

    private async void OnConfirmButtonClicked()
    {
        if (nameInputField == null) return;
        await ApplyNicknameAsync(nameInputField.text);
    }
}
