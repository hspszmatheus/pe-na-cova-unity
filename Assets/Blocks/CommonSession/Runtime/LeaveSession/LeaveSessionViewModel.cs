using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.Properties;
using Unity.Services.Multiplayer;
using UnityEngine.UIElements;

namespace Blocks.Sessions.Common
{
    class LeaveSessionViewModel : IDisposable, IDataSourceViewHashProvider, INotifyBindablePropertyChanged
    {
        SessionObserver m_SessionObserver;
        ISession m_Session;

        [CreateProperty]
        public bool CanLeaveSession
        {
            get => m_CanLeaveSession;
            private set
            {
                if (m_CanLeaveSession == value)
                {
                    return;
                }

                m_CanLeaveSession = value;
                Notify();
            }
        }

        bool m_CanLeaveSession;

        public LeaveSessionViewModel(string sessionType)
        {
            m_SessionObserver = new SessionObserver(sessionType);
            m_SessionObserver.SessionAdded += OnSessionAdded;
            if (m_SessionObserver.Session != null)
            {
                OnSessionAdded(m_SessionObserver.Session);
            }
        }

        void OnSessionAdded(ISession newSession)
        {
            m_Session = newSession;
            m_Session.RemovedFromSession += OnSessionRemoved;
            m_Session.Deleted += OnSessionRemoved;
            CanLeaveSession = true;
        }

        void OnSessionRemoved()
        {
            CanLeaveSession = false;
            CleanupSession();
        }

        public async Task LeaveSession(string sessionType)
        {
            try
            {
                var session = MultiplayerService.Instance?.Sessions[sessionType];
                if (session != null)
                {
                    if (session.IsHost)
                    {
                        await session.AsHost().DeleteAsync();
                    }
                    else
                    {
                        await session.LeaveAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LeaveSessionViewModel] Erro ao sair/deletar sessão: {ex.Message}");
            }
        }

        void CleanupSession()
        {
            m_Session.RemovedFromSession -= OnSessionRemoved;
            m_Session.Deleted -= OnSessionRemoved;
            m_Session = null;
        }

        public void Dispose()
        {
            if (m_SessionObserver != null)
            {
                m_SessionObserver.Dispose();
                m_SessionObserver = null;
            }

            if (m_Session != null)
            {
                CleanupSession();
            }
        }

        /// <summary>
        /// This method is used by UIToolkit to determine if any data bound to the UI has changed.
        /// Instead of hashing the data, an m_CanLeaveSession boolean is toggled when changes occur.
        /// </summary>
        public long GetViewHashCode() => m_CanLeaveSession ? 1 : 0;

        /// <summary>
        /// Suggested implementation of INotifyBindablePropertyChanged from UIToolkit.
        /// </summary>
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        void Notify([CallerMemberName] string property = null)
        {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }
    }
}
