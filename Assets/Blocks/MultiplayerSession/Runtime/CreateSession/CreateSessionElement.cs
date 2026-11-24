using System.Collections.Generic;
using Blocks.Common;
using Blocks.Sessions.Common;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Sessions
{
    [UxmlElement]
    public partial class CreateSessionElement : VisualElement
    {
        const string k_EnterSessionNamePlaceholder = "Nome da Sala";
        const string k_CreateButtonText = "CRIAR SALA";

        [CreateProperty, UxmlAttribute]
        public SessionSettings SessionSettings
        {
            get => m_SessionSettings;
            set
            {
                if (m_SessionSettings == value)
                    return;

                m_SessionSettings = value;
                if (panel != null)
                    UpdateBindings();
            }
        }
        SessionSettings m_SessionSettings;

        CreateSessionViewModel m_ViewModel;

        readonly List<DataBinding> m_Bindings = new();

        public CreateSessionElement()
        {
            AddToClassList(BlocksTheme.ContainerHorizontal);

            var enabledBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(m_ViewModel.CanRegisterSession)),
                bindingMode = BindingMode.ToTarget
            };
            SetBinding(new BindingId(nameof(enabledSelf)), enabledBinding);
            m_Bindings.Add(enabledBinding);

            var sessionNameTextField = new TextField
            {
                textEdition =
                {
                    placeholder = k_EnterSessionNamePlaceholder,
                    hidePlaceholderOnFocus = true
                }
            };
            sessionNameTextField.AddToClassList(BlocksTheme.TextField);
            sessionNameTextField.AddToClassList(BlocksTheme.SpaceRight);
            var sessionNameBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(m_ViewModel.SessionName)),
                bindingMode = BindingMode.ToSource
            };
            sessionNameTextField.SetBinding("value", sessionNameBinding);
            Add(sessionNameTextField);
            m_Bindings.Add(sessionNameBinding);

            var createSessionButton = new Button
            {
                text = k_CreateButtonText
            };
            createSessionButton.AddToClassList(BlocksTheme.Button);
            var createSessionBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(m_ViewModel.HasSessionName)),
                bindingMode = BindingMode.ToTarget
            };
            createSessionButton.SetBinding(new BindingId(nameof(enabledSelf)), createSessionBinding);
            createSessionButton.clicked += CreateSession;
            Add(createSessionButton);
            m_Bindings.Add(createSessionBinding);

            RegisterCallback<AttachToPanelEvent>(_ => UpdateBindings());
            RegisterCallback<DetachFromPanelEvent>(_ => CleanupBindings());
        }

        async void CreateSession()
        {
            if (!SessionSettings)
            {
                Debug.LogError("SessionSettings is null, it needs to be assigned in the uxml.");
                return;
            }
            if (!m_ViewModel.AreMultiplayerServicesInitialized())
            {
                Debug.LogError("Multiplayer Services are not initialized. You can initialize them with default settings by adding a ServicesInitialization and PlayerAuthentication components in your scene.");
                return;
            }

            try
            {
                if (Unity.Services.Authentication.AuthenticationService.Instance == null ||
                    !Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.LogWarning("[CreateSession] Aguardando autenticação antes de criar a sala...");
                    return;
                }

                var options = SessionSettings.ToSessionOptions();
                options.MaxPlayers = Mathf.Clamp(options.MaxPlayers, 1, 8);
                if (string.IsNullOrEmpty(Unity.Services.Authentication.AuthenticationService.Instance.PlayerName))
                {
                    string nick = PlayerPrefs.GetString("SavedPlayerNickname", "Player");
                    if (!string.IsNullOrEmpty(nick))
                    {
                        try { await Unity.Services.Authentication.AuthenticationService.Instance.UpdatePlayerNameAsync(nick); } catch {}
                    }
                }

                await m_ViewModel.CreateSessionAsync(options);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        void UpdateBindings()
        {
            CleanupBindings();

            m_ViewModel = new CreateSessionViewModel(SessionSettings?.sessionType);
            foreach (var binding in m_Bindings)
            {
                binding.dataSource = m_ViewModel;
            }
        }

        void CleanupBindings()
        {
            m_ViewModel?.Dispose();
            m_ViewModel = null;

            foreach (var binding in m_Bindings)
            {
                binding.dataSource = null;
            }
        }
    }
}
