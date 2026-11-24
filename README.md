# 🎮 Pé na Cova

> Jogo multiplayer assimétrico em terceira pessoa desenvolvido em **Unity** com foco em furtividade, dedução social e conclusão de objetivos em ambiente urbano.

---

## 👥 Integrantes da Equipe

| Nome Completo | RA |
| :--- | :--- |
| **Matheus Hespanhol de Souza** | `2017301` |
| **Vinícius Boroto dos Anjos** | `2017230` |
| **Luan Martins Toniolo** | `2017588` |
| **Kauã Vinicius de Souza** | `2018097` |
| **Maria Eduarda Moura Gandra** | `2017560` |

---

## 📌 Nota sobre o Controle de Versão

> [!NOTE]
> Todo o desenvolvimento ativo e colaborativo do projeto foi realizado utilizando o **sistema de controle de versão nativo da Unity (Unity Version Control / Plastic SCM)**.
> 
> O projeto foi exportado e estruturado neste repositório do **GitHub** especialmente para **facilitar o acesso, avaliação e execução do código por parte do professor**, mantendo o repositório leve e otimizado com o `.gitignore` oficial da Unity (excluindo as pastas de cache como `Library/`, `Temp/`, etc., que são recriadas automaticamente pelo editor).

---

## 🕹️ Sobre o Jogo

**Pé na Cova** é uma experiência multiplayer assimétrica onde os jogadores são divididos entre **Sobreviventes (Survivors)** e **Caçador (Hunter)** em um mapa urbano vivo e dinâmico.

### 🎯 Dinâmica da Partida

* **Sobreviventes (Survivors):**
  * Devem navegar pela cidade para localizar e concluir tarefas interativas (*tasks*) espalhadas pelo mapa.
  * Precisam se misturar e imitar o comportamento de **pedestres (NPCs)** que circulam pela rede urbana para evitar serem descobertos e eliminados.
  * Possuem mecânicas defensivas de emergência, como atordoamento (chute) para escapar quando encurralados.

* **Caçador (Hunter):**
  * Tem como objetivo identificar quais pedestres são jogadores reais disfarçados e eliminá-los antes que todas as tarefas sejam concluídas.
  * Possui habilidades únicas e temáticas, incluindo:
    * Invocação e lançamento de **projéteis de fogo (Fireball)**.
    * Habilidade de **revelação em área** para expor sobreviventes escondidos.
    * **Presença intimidadora / efeito de medo por proximidade** que alerta e desestabiliza sobreviventes próximos.

### 🏙️ Principais Funcionalidades e Sistemas

1. **Rede de Tráfego de Pedestres (City Pedestrian Network):**
   * Sistema de nós de navegação e rotas onde NPCs caminham autonomamente pela cidade, permitindo uma camuflagem social orgânica dos jogadores.
2. **Sistema Completo de Tarefas (TaskManager & TaskObjects):**
   * Objetivos espalhados pelo mapa com indicadores visuais no HUD e na bússola orientando os sobreviventes.
3. **Lobby & Matchmaking Multiplayer:**
   * Navegação e busca de salas de jogo através de interface interativa (*Join by Browsing*), status de confirmação (*Ready*) e controle dinâmico de conexões.
4. **Áudio e Feedback Visual:**
   * Trilha sonora dinâmica de gameplay, efeitos sonoros espaciais 3D (passos, conjurações, gritos e reações) e efeitos de partículas estilizados (URP VFX).

---

## 🛠️ Tecnologias e Ferramentas

* **Game Engine:** Unity 6 (`6000.5.6f1`)
* **Render Pipeline:** Universal Render Pipeline (URP)
* **Linguagem:** C# (.NET)
* **Multiplayer / Networking:** Unity Netcode for GameObjects
* **Versionamento de Desenvolvimento:** Plastic SCM / Unity Version Control

---

## 🚀 Como Executar o Projeto

1. **Pré-requisitos:**
   * Instale o **Unity Hub**.
   * Instale a versão **Unity 6 (`6000.5.6f1`)** ou superior compatível via Unity Hub.

2. **Clonando o Repositório:**
   ```bash
   git clone https://github.com/hspszmatheus/pe-na-cova-unity.git
   ```

3. **Abrindo na Unity:**
   * Abra o **Unity Hub** e clique em **Add** > **Add project from disk**.
   * Selecione a pasta clonada do projeto.
   * Na primeira inicialização, o Unity irá reconstruir automaticamente a pasta `Library/` e compilar os pacotes necessários.

4. **Cenas Principais:**
   * `Assets/Scenes/JoinByBrowsing.unity` — **Cena Inicial / Menu & Lobby** (Inicie o jogo por aqui para testar o fluxo completo de salas e conexões).
   * `Assets/Scenes/GameScene.unity` — **Cena Principal de Gameplay**.
