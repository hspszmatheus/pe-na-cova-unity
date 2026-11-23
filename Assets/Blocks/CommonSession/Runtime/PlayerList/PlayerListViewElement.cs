using Blocks.Common;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Blocks.Sessions.Common
{
    [UxmlElement]
    public partial class PlayerListViewElement : ListView
    {
        private const string k_PlayerListIsEmptyLabel = "Aguardando outros jogadores entrarem...";

        private string m_SessionType;
        private DataBinding m_DataBinding;
        private PlayerListViewModel m_ViewModel;

        [CreateProperty, UxmlAttribute]
        public string SessionType
        {
            get => m_SessionType;
            set
            {
                if (m_SessionType == value)
                {
                    return;
                }

                m_SessionType = value;
                if (panel != null)
                {
                    UpdateBindingSources();
                }
            }
        }

        public PlayerListViewElement()
        {
            selectionType = SelectionType.None;
            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            // required for making each element inheriting an indexed array item for the data source path
            // if not set, you need to specify the datasource path manually in the bindItem callback for each item
            bindingSourceSelectionMode = BindingSourceSelectionMode.AutoAssign;

            AddToClassList(BlocksTheme.ListViewNoHover);
            AddToClassList(BlocksTheme.ScrollView);

            makeNoneElement = MakeNoneElement;

            makeItem = MakeDefaultItem;

            bindItem = (element, index) =>
            {
                if (itemsSource != null && index >= 0 && index < itemsSource.Count)
                {
                    var vm = itemsSource[index] as PlayerViewModel;
                    var roleBadge = element.Q<Label>("CartoonRoleBadge");
                    if (roleBadge != null && vm != null)
                    {
                        if (vm.IsHost)
                        {
                            roleBadge.text = "★ LÍDER";
                            roleBadge.style.display = DisplayStyle.Flex;
                            roleBadge.RemoveFromClassList("cartoon-player-role-badge--survivor");
                            roleBadge.AddToClassList("cartoon-player-role-badge--host");
                        }
                        else
                        {
                            // Sobreviventes que entram na sala não possuem tag; apenas o líder exibe badge
                            roleBadge.style.display = DisplayStyle.None;
                            roleBadge.text = string.Empty;
                        }
                    }
                }
            };

            BindItemSource();

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanelEvent);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanelEvent);
        }


        private void OnDetachFromPanelEvent(DetachFromPanelEvent panelEvent)
        {
            CleanupBindings();
        }

        private void OnAttachToPanelEvent(AttachToPanelEvent panelEvent)
        {
            UpdateBindingSources();
        }

        private void UpdateBindingSources()
        {
            CleanupBindings();

            m_ViewModel = new PlayerListViewModel(m_SessionType);
            m_DataBinding.dataSource = m_ViewModel;
        }

        private void CleanupBindings()
        {
            m_ViewModel?.Dispose();
            m_ViewModel = null;
            m_DataBinding.dataSource = null;
        }

        private static VisualElement MakeNoneElement()
        {
            var box = new VisualElement();
            box.AddToClassList("cartoon-player-empty-box");
            var label = new Label(k_PlayerListIsEmptyLabel);
            label.AddToClassList("cartoon-player-empty-label");
            box.Add(label);
            return box;
        }

        private void BindItemSource()
        {
            m_DataBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(PlayerListViewModel.Players)),
                bindingMode = BindingMode.ToTarget
            };
            SetBinding(new BindingId(nameof(itemsSource)), m_DataBinding);
        }

        private static VisualElement MakeDefaultItem()
        {
            var row = new VisualElement { name = "CartoonPlayerRow" };
            row.AddToClassList("cartoon-player-card");

            // Círculo com avatar do sobrevivente
            var avatarCircle = new VisualElement { name = "CartoonAvatarCircle" };
            avatarCircle.AddToClassList("cartoon-player-avatar-circle");

            var avatarIcon = new VisualElement { name = "CartoonAvatarIcon" };
            avatarIcon.AddToClassList("cartoon-player-avatar-icon");
            avatarCircle.Add(avatarIcon);
            row.Add(avatarCircle);

            // Container central do nome do jogador
            var infoBox = new VisualElement { name = "CartoonPlayerInfoBox" };
            infoBox.AddToClassList("cartoon-player-info-box");

            var playerNameLabel = new Label { name = nameof(PlayerNameLabel) };
            playerNameLabel.AddToClassList("cartoon-player-name-text");

            var dataBinding = new DataBinding
            {
                dataSourcePath = PropertyPath.FromName(nameof(PlayerViewModel.Name)),
                bindingMode = BindingMode.ToTarget
            };
            playerNameLabel.SetBinding(new BindingId(nameof(Label.text)), dataBinding);
            infoBox.Add(playerNameLabel);
            row.Add(infoBox);

            // Badge de função (LÍDER exibido exclusivamente para o Host; Sobreviventes não exibem tag)
            var roleBadge = new Label("★ LÍDER") { name = "CartoonRoleBadge" };
            roleBadge.AddToClassList("cartoon-player-role-badge");
            roleBadge.AddToClassList("cartoon-player-role-badge--host");
            roleBadge.style.display = DisplayStyle.None;
            row.Add(roleBadge);

            return row;
        }
    }
}
