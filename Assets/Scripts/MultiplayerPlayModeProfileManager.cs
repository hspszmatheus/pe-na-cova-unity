using System;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Authentication.Components;
using Unity.Multiplayer.PlayMode;

[DefaultExecutionOrder(-1000)]
public class MultiplayerPlayModeProfileManager : MonoBehaviour
{
    private static string s_CachedProfile;
    private static Mutex s_ProcessSlotMutex;
    private static int s_AcquiredSlot = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_CachedProfile = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitBeforeSceneLoad()
    {
        // Garante que janelas em segundo plano continuem executando a rede sem congelar
        Application.runInBackground = true;

        s_CachedProfile = ResolveProfile();
        bool isMain = Application.isEditor && CurrentPlayer.IsMainEditor;
        Debug.Log($"[MultiplayerPlayModeProfileManager] Initialized BeforeSceneLoad. Active Profile: '{s_CachedProfile}' (IsMainEditor: {isMain})");

        // Pré-inicializa o UnityServices com o perfil isolado antes mesmo da cena carregar
        InitializeServicesWithProfile(s_CachedProfile);

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitAfterSceneLoad()
    {
        ApplyProfileToActiveScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyProfileToActiveScene();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        string profile = GetProfile();
        InitializeServicesWithProfile(profile);
        ConfigureLocalComponent();
    }

    private void OnEnable()
    {
        string profile = GetProfile();
        InitializeServicesWithProfile(profile);
        ConfigureLocalComponent();
    }

    public static async void InitializeServicesWithProfile(string targetProfile)
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                options.SetProfile(targetProfile);
                await UnityServices.InitializeAsync(options);
                Debug.Log($"[MultiplayerPlayModeProfileManager] UnityServices successfully initialized with profile: '{targetProfile}'");
            }
            else if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance != null)
            {
                if (AuthenticationService.Instance.Profile != targetProfile)
                {
                    if (AuthenticationService.Instance.IsSignedIn)
                    {
                        AuthenticationService.Instance.SignOut();
                    }
                    AuthenticationService.Instance.SwitchProfile(targetProfile);
                    Debug.Log($"[MultiplayerPlayModeProfileManager] Switched AuthenticationService profile to '{targetProfile}'");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerPlayModeProfileManager] InitializeServicesWithProfile: {ex.Message}");
        }
    }

    private void OnApplicationQuit()
    {
        ReleaseSlotMutex();
    }

    private void OnDestroy()
    {
        ReleaseSlotMutex();
    }

    private static void ReleaseSlotMutex()
    {
        if (s_ProcessSlotMutex != null)
        {
            try
            {
                s_ProcessSlotMutex.ReleaseMutex();
                s_ProcessSlotMutex.Dispose();
            }
            catch {}
            s_ProcessSlotMutex = null;
        }
    }

    private void ConfigureLocalComponent()
    {
        var auth = GetComponent<PlayerAuthentication>();
        if (auth != null)
        {
            ApplyProfile(auth);
        }
    }

    public static void ApplyProfileToActiveScene()
    {
        string profile = GetProfile();
        var allAuth = UnityEngine.Object.FindObjectsByType<PlayerAuthentication>(FindObjectsInactive.Include);
        foreach (var auth in allAuth)
        {
            ApplyProfile(auth);
        }

        // Also check if UnityServices and AuthenticationService are already available
        CheckAndSwitchActiveAuthService(profile);
    }

    public static void ApplyProfile(PlayerAuthentication auth)
    {
        if (auth == null) return;

        string targetProfile = GetProfile();
        if (!auth.SetCustomProfile || auth.Profile != targetProfile)
        {
            auth.SetCustomProfile = true;
            auth.Profile = targetProfile;
            Debug.Log($"[MultiplayerPlayModeProfileManager] Set PlayerAuthentication on '{auth.gameObject.name}' -> Profile: '{targetProfile}'");
        }
    }

    private static async void CheckAndSwitchActiveAuthService(string targetProfile)
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance != null)
            {
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    if (AuthenticationService.Instance.Profile != targetProfile)
                    {
                        Debug.LogWarning($"[MultiplayerPlayModeProfileManager] Profile mismatch detected (current: '{AuthenticationService.Instance.Profile}', target: '{targetProfile}'). Switching...");
                        AuthenticationService.Instance.SignOut();
                        AuthenticationService.Instance.SwitchProfile(targetProfile);
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                        Debug.Log($"[MultiplayerPlayModeProfileManager] Re-signed in with correct profile: '{targetProfile}', PlayerId: {AuthenticationService.Instance.PlayerId}");
                    }
                }
                else
                {
                    if (AuthenticationService.Instance.Profile != targetProfile)
                    {
                        AuthenticationService.Instance.SwitchProfile(targetProfile);
                        Debug.Log($"[MultiplayerPlayModeProfileManager] Switched AuthenticationService profile to '{targetProfile}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerPlayModeProfileManager] CheckAndSwitchActiveAuthService: {ex.Message}");
        }
    }

    public static string GetProfile()
    {
        if (string.IsNullOrEmpty(s_CachedProfile))
        {
            s_CachedProfile = ResolveProfile();
        }
        return s_CachedProfile;
    }

    public static string ResolveProfile()
    {
        // 1. Se estiver rodando dentro do Unity Editor (Multiplayer Play Mode)
        if (Application.isEditor)
        {
            if (CurrentPlayer.IsMainEditor)
            {
                return "MainEditor";
            }

            // Virtual Player clone: checa argumentos de linha de comando (-profile <Name> ou -name <Name>)
            var editorArgs = Environment.GetCommandLineArgs();
            for (int i = 0; i < editorArgs.Length - 1; i++)
            {
                if (editorArgs[i].Equals("-profile", StringComparison.OrdinalIgnoreCase) ||
                    editorArgs[i].Equals("-name", StringComparison.OrdinalIgnoreCase))
                {
                    string name = editorArgs[i + 1]?.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        return SanitizeProfile(name);
                    }
                }
            }

            // Virtual Player clone project path: -projectPath <Path>
            for (int i = 0; i < editorArgs.Length - 1; i++)
            {
                if (editorArgs[i].Equals("-projectPath", StringComparison.OrdinalIgnoreCase))
                {
                    string path = editorArgs[i + 1];
                    if (!string.IsNullOrEmpty(path) && path.IndexOf("VP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string dirName = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
                        return SanitizeProfile("VP_" + dirName);
                    }
                }
            }

            // Check dataPath for clone
            string dataPath = Application.dataPath;
            if (dataPath.IndexOf("Library/VP/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                dataPath.IndexOf("Library\\VP\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int vpIndex = dataPath.IndexOf("VP", StringComparison.OrdinalIgnoreCase);
                string sub = dataPath.Substring(vpIndex + 3);
                int slashIndex = sub.IndexOfAny(new char[] { '/', '\\' });
                if (slashIndex > 0)
                {
                    string cloneName = sub.Substring(0, slashIndex);
                    return SanitizeProfile("VP_" + cloneName);
                }
            }

            // Check MPPM tags if any
            try
            {
                var tags = CurrentPlayer.Tags;
                if (tags != null && tags.Count > 0 && !string.IsNullOrEmpty(tags[0]))
                {
                    return SanitizeProfile("Tag_" + tags[0]);
                }
            }
            catch {}

            return "VP_VirtualPlayer";
        }

        // 2. Argumentos explícitos de linha de comando para Standalone Build (-profile <Name>)
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("-profile", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-name", StringComparison.OrdinalIgnoreCase))
            {
                string name = args[i + 1]?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    return SanitizeProfile(name);
                }
            }
        }

        // 3. Se estiver rodando em Standalone Build (.exe):
        // Reserva dinamicamente um slot de processo (1 a 16) usando Mutex local do sistema.
        // Assim, cada instância aberta na mesma máquina ganha um perfil único ('Build_Player_1', 'Build_Player_2', etc.)
        // e um PlayerId totalmente independente na nuvem do Unity Gaming Services!
        int slot = GetOrAcquireProcessSlot();
        return SanitizeProfile($"Build_Player_{slot}");
    }

    private static int GetOrAcquireProcessSlot()
    {
        if (s_AcquiredSlot > 0) return s_AcquiredSlot;

        for (int i = 1; i <= 16; i++)
        {
            string mutexName = $"Global_EOTWH_Instance_Slot_{i}";
            try
            {
                var mutex = new Mutex(true, mutexName, out bool createdNew);
                if (createdNew)
                {
                    s_ProcessSlotMutex = mutex;
                    s_AcquiredSlot = i;
                    return i;
                }
                mutex.Dispose();
            }
            catch
            {
                // Fallback para mutex local caso Global seja restrito
                try
                {
                    string localName = $"EOTWH_Instance_Slot_{i}";
                    var mutex = new Mutex(true, localName, out bool createdNew);
                    if (createdNew)
                    {
                        s_ProcessSlotMutex = mutex;
                        s_AcquiredSlot = i;
                        return i;
                    }
                    mutex.Dispose();
                }
                catch
                {
                    // Tenta o próximo slot
                }
            }
        }

        // Fallback final: usa o Process ID do sistema operacional
        try
        {
            s_AcquiredSlot = System.Diagnostics.Process.GetCurrentProcess().Id % 1000 + 1;
        }
        catch
        {
            s_AcquiredSlot = UnityEngine.Random.Range(1, 100);
        }
        return s_AcquiredSlot;
    }

    private static string SanitizeProfile(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "DefaultPlayer";
        var sb = new StringBuilder();
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
            else
                sb.Append('_');
        }
        string result = sb.ToString();
        if (string.IsNullOrEmpty(result)) return "DefaultPlayer";
        if (result.Length > 30) result = result.Substring(0, 30);
        return result;
    }
}
