using System.Collections.Generic;
using UnityEngine;

public enum PedestrianNodeType
{
    Sidewalk,        // Calçadas e passeios urbanos
    StoreFront,      // Vitrines de lojas e fachadas comerciais
    GasStation,      // Posto de combustível, bombas e conveniência
    ParkGarden,      // Praças, áreas verdes, parques e jardins
    ParkingLot,      // Estacionamentos e pátios de veículos
    StreetCrossing,  // Faixas de pedestres e travessias de rua
    Alleyway,        // Becos e passagens estreitas entre edifícios
    ScenicPoint,     // Outdoors, placas de trânsito, postes e mirantes
    TaskSpot         // Proximidade de tarefas e pontos interativos
}

public class PedestrianNode : MonoBehaviour
{
    [Header("Conexões de Calçada")]
    [Tooltip("Nós vizinhos conectados para onde o NPC pode caminhar")]
    public List<PedestrianNode> neighbors = new List<PedestrianNode>();

    [Header("Tipo de Local e Variedade")]
    [Tooltip("Variedade do ponto urbano respeitando os objetos do cenário")]
    public PedestrianNodeType nodeType = PedestrianNodeType.Sidewalk;

    [Tooltip("Descrição legível do local")]
    public string locationName = "Calçada Urbana";

    [Header("Comportamento de Pausa")]
    [Tooltip("Se marcado, NPCs podem pausar aqui para observar o cenário")]
    public bool isPausePoint = true;

    [Tooltip("Chance (0 a 1) do NPC fazer uma pausa ao passar por este nó")]
    [Range(0f, 1f)]
    public float pauseChance = 0.35f;

    [Tooltip("Tempo mínimo de permanência ao pausar")]
    public float minPauseDuration = 2.0f;

    [Tooltip("Tempo máximo de permanência ao pausar")]
    public float maxPauseDuration = 5.0f;

    [Header("Orientação no Cenário")]
    [Tooltip("Direção preferida para olhar ao pausar (ex: encarar vitrine, outdoor, posto). Se zero, usa orientação natural")]
    public Vector3 preferredLookDirection = Vector3.zero;

    public Vector3 Position => transform.position;

    public PedestrianNode GetRandomNeighbor(PedestrianNode previousNode = null)
    {
        if (neighbors == null || neighbors.Count == 0) return null;

        // Se houver mais de um vizinho, tenta não voltar imediatamente para o nó anterior
        List<PedestrianNode> candidates = new List<PedestrianNode>();
        foreach (var n in neighbors)
        {
            if (n != null && (neighbors.Count == 1 || n != previousNode))
            {
                candidates.Add(n);
            }
        }

        if (candidates.Count == 0) candidates.AddRange(neighbors);

        // Se houver candidatos com tipos variados (diferentes do atual), dá chance extra de variedade
        if (candidates.Count > 1)
        {
            List<PedestrianNode> variedCandidates = new List<PedestrianNode>();
            foreach (var c in candidates)
            {
                if (c != null && c.nodeType != this.nodeType)
                {
                    variedCandidates.Add(c);
                }
            }

            // 50% de chance de priorizar transição para outro tipo de ambiente (ex: sair da calçada para praça ou posto)
            if (variedCandidates.Count > 0 && Random.value < 0.50f)
            {
                return variedCandidates[Random.Range(0, variedCandidates.Count)];
            }
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Color nodeColor = GetNodeColor(nodeType);
        Gizmos.color = nodeColor;
        Gizmos.DrawSphere(transform.position, isPausePoint ? 0.40f : 0.25f);

        if (preferredLookDirection.sqrMagnitude > 0.01f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.3f, preferredLookDirection.normalized * 1.5f);
        }

        if (neighbors != null)
        {
            Gizmos.color = new Color(nodeColor.r, nodeColor.g, nodeColor.b, 0.45f);
            foreach (var n in neighbors)
            {
                if (n != null)
                {
                    Gizmos.DrawLine(transform.position, n.transform.position);
                }
            }
        }
    }

    private static Color GetNodeColor(PedestrianNodeType type)
    {
        switch (type)
        {
            case PedestrianNodeType.Sidewalk: return new Color(0.1f, 0.7f, 1f, 0.85f); // Ciano
            case PedestrianNodeType.StoreFront: return new Color(1f, 0.6f, 0.1f, 0.9f); // Laranja
            case PedestrianNodeType.GasStation: return new Color(1f, 0.25f, 0.15f, 0.9f); // Vermelho-alaranjado
            case PedestrianNodeType.ParkGarden: return new Color(0.2f, 0.95f, 0.3f, 0.9f); // Verde Esmeralda
            case PedestrianNodeType.ParkingLot: return new Color(0.4f, 0.6f, 0.9f, 0.85f); // Azul aço
            case PedestrianNodeType.StreetCrossing: return new Color(1f, 0.95f, 0.2f, 0.95f); // Amarelo
            case PedestrianNodeType.Alleyway: return new Color(0.7f, 0.3f, 1f, 0.85f); // Roxo
            case PedestrianNodeType.ScenicPoint: return new Color(1f, 0.85f, 0.1f, 0.95f); // Ouro
            case PedestrianNodeType.TaskSpot: return new Color(0.9f, 0.1f, 1f, 0.95f); // Magenta
            default: return Color.white;
        }
    }
#endif
}
