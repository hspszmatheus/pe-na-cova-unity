using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Properties;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Sessions
{
    public class SessionBrowserViewModel : INotifyBindablePropertyChanged, IDataSourceViewHashProvider, IDisposable
    {
        private static float s_LastQueryTime = -10f;
        private const float k_MinQueryInterval = 1.25f;

        private SessionObserver m_SessionObserver;
        private ServiceObserver<IMultiplayerService> m_ServiceObserver;

        private long m_UpdateVersion;
        private bool m_SelectedAndAvailable;
        private bool m_CanRefresh;
        private int m_SelectedSessionIndex;

        private ISession m_Session;
        private List<SessionInfoViewModel> m_Sessions;

        [CreateProperty]
        public List<SessionInfoViewModel> Sessions
        {
            get => m_Sessions;
            set
            {
                if (m_Sessions == value)
                {
                    return;
                }

                m_Sessions = value;
                ++m_UpdateVersion;
                Notify();
            }
        }

        [CreateProperty]
        public int SelectedSessionIndex
        {
            get => m_SelectedSessionIndex;
            set
            {
                if (value >= 0 && value < Sessions.Count)
                {
                    m_SelectedSessionIndex = value;
                    SelectedAndAvailable = true;
                }
                else
                {
                    SelectedAndAvailable = false;
                }
            }
        }

        public string GetSelectedSessionId()
        {
            if (SelectedSessionIndex >= 0 && SelectedSessionIndex < Sessions.Count)
            {
                return Sessions[SelectedSessionIndex].Id;
            }
            return null;
        }

        [CreateProperty]
        public bool SelectedAndAvailable
        {
            get => m_SelectedAndAvailable;
            set
            {
                var newValue = value;

                if (value && m_Session != null && m_Session.Id == GetSelectedSessionId())
                {
                    newValue = false;
                }

                if (m_SelectedAndAvailable != newValue)
                {
                    m_SelectedAndAvailable = newValue;
                    ++m_UpdateVersion;
                    Notify();
                }
            }
        }


        [CreateProperty]
        public bool CanRefresh
        {
            get => m_CanRefresh;
            set
            {
                if (m_CanRefresh == value)
                {
                    return;
                }

                m_CanRefresh = value;
                ++m_UpdateVersion;
                Notify();
            }
        }


        public SessionBrowserViewModel(string sessionType)
        {
            Sessions = new List<SessionInfoViewModel>();

            m_SessionObserver = new SessionObserver(sessionType);
            m_SessionObserver.SessionAdded += OnSessionAdded;

            if (m_SessionObserver.Session != null)
            {
                OnSessionAdded(m_SessionObserver.Session);
            }

            // Monitora status de autenticação para atualizar a lista automaticamente assim que assinar
            try
            {
                if (AuthenticationService.Instance != null)
                {
                    AuthenticationService.Instance.SignedIn -= OnAuthenticationSignedIn;
                    AuthenticationService.Instance.SignedIn += OnAuthenticationSignedIn;
                }
            }
            catch {}

            // This can be null while in edit mode and needs to be checked before creating the Observer.
            if (UnityServices.Instance != null)
            {
                m_ServiceObserver = new ServiceObserver<IMultiplayerService>();
                if (m_ServiceObserver.Service != null)
                {
                    bool isSignedIn = IsPlayerSignedIn();
                    CanRefresh = isSignedIn;
                    if (isSignedIn)
                    {
                        _ = UpdateSessionListAsync(20);
                    }
                }
                else
                {
                    CanRefresh = false;
                    m_ServiceObserver.Initialized += OnServicesInitialized;
                }
            }
        }

        private static bool IsPlayerSignedIn()
        {
            try
            {
                return UnityServices.State == ServicesInitializationState.Initialized &&
                       AuthenticationService.Instance != null &&
                       AuthenticationService.Instance.IsSignedIn;
            }
            catch
            {
                return false;
            }
        }

        private void OnAuthenticationSignedIn()
        {
            CanRefresh = true;
            _ = UpdateSessionListAsync(20);
        }

        public async Task JoinSessionAsync(JoinSessionOptions options)
        {
            string sessionId = GetSelectedSessionId();
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[SessionBrowser] Nenhuma sala selecionada para entrar.");
                return;
            }

            try
            {
                if (!IsPlayerSignedIn())
                {
                    Debug.LogWarning("[SessionBrowser] Aguardando autenticação antes de entrar na sala...");
                    return;
                }

                if (string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName))
                {
                    string nick = PlayerPrefs.GetString("SavedPlayerNickname", "Player");
                    if (!string.IsNullOrEmpty(nick))
                    {
                        try { await AuthenticationService.Instance.UpdatePlayerNameAsync(nick); } catch {}
                    }
                }

                await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, options);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        void OnServicesInitialized(IMultiplayerService service)
        {
            m_ServiceObserver.Initialized -= OnServicesInitialized;
            bool isSignedIn = IsPlayerSignedIn();
            CanRefresh = isSignedIn;
            if (isSignedIn)
            {
                _ = UpdateSessionListAsync(20);
            }
        }

        internal async Task UpdateSessionListAsync(int numberOfMaxSessions)
        {
            // if there is no connection to MultiplayerService or not signed in, do not try to refresh
            if (!CanRefresh || !IsPlayerSignedIn())
            {
                return;
            }

            // Rate-limit guard: o Lobby API do UGS limita consultas a no máximo 1 por segundo
            float timeSinceLast = Time.unscaledTime - s_LastQueryTime;
            if (timeSinceLast < k_MinQueryInterval)
            {
                return;
            }
            s_LastQueryTime = Time.unscaledTime;

            string previousSelectedId = GetSelectedSessionId();

            try
            {
                CanRefresh = false;
                var queryResult = await MultiplayerService.Instance
                    .QuerySessionsAsync(new QuerySessionsOptions
                    {
                        SortOptions = new List<SortOption>
                        {
                            new (SortOrder.Descending,SortField.Name)
                        }
                    });

                // properly dispose the sessionInfo view models first
                foreach (var session in Sessions)
                {
                    session.Dispose();
                }

                Sessions.Clear();

                // Filtra salas fantasmas (sem host, 0 jogadores restantes, ou dados inválidos)
                var validSessions = new List<ISessionInfo>();
                for (var i = 0; i < queryResult.Sessions.Count; i++)
                {
                    var s = queryResult.Sessions[i];
                    if (s == null) continue;

                    // 1. Sem Host válido
                    if (string.IsNullOrEmpty(s.HostId)) continue;
                    // 2. Salas sem jogadores (0 jogadores no lobby: AvailableSlots >= MaxPlayers)
                    if (s.MaxPlayers > 0 && s.AvailableSlots >= s.MaxPlayers) continue;
                    // 3. Capacidade de jogadores inválida
                    if (s.MaxPlayers <= 0) continue;
                    // 4. Salas trancadas
                    if (s.IsLocked) continue;

                    validSessions.Add(s);
                }

                int count = Math.Min(validSessions.Count, numberOfMaxSessions);
                for (var i = 0; i < count; i++)
                {
                    Sessions.Add(new SessionInfoViewModel(validSessions[i]));
                }

                // Restore previous selection if the session is still active
                int restoredIndex = -1;
                if (!string.IsNullOrEmpty(previousSelectedId))
                {
                    for (int i = 0; i < Sessions.Count; i++)
                    {
                        if (Sessions[i].Id == previousSelectedId)
                        {
                            restoredIndex = i;
                            break;
                        }
                    }
                }
                SelectedSessionIndex = restoredIndex;

                ++m_UpdateVersion;
                Notify(nameof(Sessions));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SessionBrowser] Could not update session list: {ex.Message}");
            }
            finally
            {
                CanRefresh = true;
            }
        }

        private void OnSessionAdded(ISession newSession)
        {
            m_Session = newSession;
            m_Session.RemovedFromSession += OnSessionRemoved;
            m_Session.Deleted += OnSessionRemoved;
            if (m_Session.Id == GetSelectedSessionId())
            {
                SelectedAndAvailable = false;
            }
        }
        private void OnSessionRemoved()
        {
            var lastSessionId = m_Session.Id;
            CleanupSession();
            if (lastSessionId == GetSelectedSessionId())
            {
                SelectedAndAvailable = true;
            }
        }

        private void CleanupSession()
        {
            m_Session.RemovedFromSession -= OnSessionRemoved;
            m_Session.Deleted -= OnSessionRemoved;
            m_Session = null;
        }

        public async Task<ISession> JoinSessionByIdAsync(JoinSessionOptions joinSessionOptions)
        {

            return await MultiplayerService.Instance.JoinSessionByIdAsync(GetSelectedSessionId(), joinSessionOptions);
        }

        public void Dispose()
        {
            try
            {
                if (AuthenticationService.Instance != null)
                {
                    AuthenticationService.Instance.SignedIn -= OnAuthenticationSignedIn;
                }
            }
            catch {}

            if (m_SessionObserver != null)
            {
                m_SessionObserver.SessionAdded -= OnSessionAdded;
                m_SessionObserver.Dispose();
                m_SessionObserver = null;
            }

            if (m_ServiceObserver != null)
            {
                m_ServiceObserver.Dispose();
                m_ServiceObserver = null;
            }

            if (m_Session != null)
            {
                CleanupSession();
            }
        }

        /// <summary>
        /// This method is used by UIToolkit to determine if any data bound to the UI has changed.
        /// Instead of hashing the data, an m_UpdateVersion counter is incremented when changes occur.
        /// </summary>
        public long GetViewHashCode() => m_UpdateVersion;

        /// <summary>
        /// Suggested implementation of INotifyBindablePropertyChanged from UIToolkit.
        /// </summary>
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        private void Notify([CallerMemberName] string property = null)
        {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }
    }
}
