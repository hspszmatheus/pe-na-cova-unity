using System.Collections.Generic;
using UnityEngine;

public class CityPedestrianNetwork : MonoBehaviour
{
    public static CityPedestrianNetwork Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [Tooltip("Lista de nós da calçada cadastrados")]
    public List<PedestrianNode> nodes = new List<PedestrianNode>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        RefreshNodes();
    }

    public void RefreshNodes()
    {
        nodes.Clear();
        nodes.AddRange(GetComponentsInChildren<PedestrianNode>());
        nodes.RemoveAll(n => n == null);
    }

    public bool HasNodes => nodes != null && nodes.Count > 0;

    public PedestrianNode GetRandomNode()
    {
        if (!HasNodes) return null;
        return nodes[Random.Range(0, nodes.Count)];
    }

    public PedestrianNode GetClosestNode(Vector3 position)
    {
        if (!HasNodes) return null;

        PedestrianNode closest = null;
        float minSqrDist = float.MaxValue;

        foreach (var n in nodes)
        {
            if (n == null) continue;
            float sqrDist = (n.transform.position - position).sqrMagnitude;
            if (sqrDist < minSqrDist)
            {
                minSqrDist = sqrDist;
                closest = n;
            }
        }

        return closest;
    }
}
