using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class HunterFireballProjectile : MonoBehaviour
{
    [Header("Configuração de Voo")]
    [Tooltip("Velocidade de propagação da bola de fogo")]
    [SerializeField] private float speed = 28f;
    [Tooltip("Tempo máximo de vida antes de autodestruir")]
    [SerializeField] private float lifetime = 5f;
    [Tooltip("Raio da esfera de colisão")]
    [SerializeField] private float hitRadius = 0.25f;
    [Tooltip("Camadas que bloqueiam ou colidem com a bola de fogo")]
    [SerializeField] private LayerMask collisionMask = ~0;

    [Header("Efeitos")]
    [Tooltip("Prefab do impacto explosivo ao atingir um Player real (efeito roxo)")]
    [SerializeField] private GameObject explosionPrefab;
    [Tooltip("Prefab do impacto de fogo ao atingir NPC ou obstáculo (efeito de fogo)")]
    [SerializeField] private GameObject fireExplosionPrefab;
    [SerializeField] private float explosionLifetime = 2.0f;

    private Vector3 moveDirection;
    private PlayerController caster;
    private ulong casterNetworkId = ulong.MaxValue;
    private PlayerController targetedPlayer = null;
    private NPCController targetedNpc = null;
    private bool isInitialized = false;
    private bool hasExploded = false;
    private float spawnTime;
    private Vector3 lastPosition;

    public void Initialize(Vector3 direction, PlayerController ownerCaster, GameObject playerExplosionTemplate = null, GameObject fireExplosionTemplate = null, PlayerController aimedPlayer = null, NPCController aimedNpc = null)
    {
        moveDirection = direction.normalized;
        caster = ownerCaster;
        casterNetworkId = (caster != null && caster.IsSpawned) ? caster.NetworkObjectId : ulong.MaxValue;
        targetedPlayer = aimedPlayer;
        targetedNpc = aimedNpc;

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(moveDirection);
        }

        if (playerExplosionTemplate != null)
        {
            explosionPrefab = playerExplosionTemplate;
        }

        if (fireExplosionTemplate != null)
        {
            fireExplosionPrefab = fireExplosionTemplate;
        }

        spawnTime = Time.time;
        lastPosition = transform.position;
        isInitialized = true;
    }

    public void Initialize(Vector3 direction, PlayerController ownerCaster, GameObject explosionTemplate, PlayerController aimedPlayer, NPCController aimedNpc)
    {
        Initialize(direction, ownerCaster, explosionTemplate, null, aimedPlayer, aimedNpc);
    }

    void Update()
    {
        if (!isInitialized || hasExploded) return;

        // Autodestruição por tempo
        if (Time.time - spawnTime >= lifetime)
        {
            Explode(transform.position, Vector3.up, isRealPlayer: false);
            return;
        }

        Vector3 nextPosition = transform.position + moveDirection * (speed * Time.deltaTime);
        Vector3 displacement = nextPosition - lastPosition;
        float distance = displacement.magnitude;
        Vector3 rayDir = distance > 0.0001f ? displacement / distance : moveDirection;

        PlayerController bestPlayer = null;
        NPCController bestNpc = null;
        Vector3 bestHitPoint = Vector3.zero;
        float earliestProgress = float.MaxValue;
        float smallestDist = float.MaxValue;

        // 1. Verificação contínua (CCD) contra Players Sobreviventes
        // Se o Hunter mirou especificamente em um NPC, não atinge nenhum player
        if (targetedNpc == null && PlayerController.ActivePlayers != null)
        {
            var players = PlayerController.ActivePlayers;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player == caster) continue;
                if (casterNetworkId != ulong.MaxValue && player.NetworkObjectId == casterNetworkId) continue;
                if (player.IsHunter || player.IsDead || player.IsGhost) continue;

                // Se o Hunter mirou em um Player específico, só aquele alvo pode ser atingido (quem está do lado é poupado)
                if (targetedPlayer != null && player != targetedPlayer) continue;

                Vector3 pBottom = player.transform.position + Vector3.up * 0.25f;
                Vector3 pTop = player.transform.position + Vector3.up * 1.65f;

                float dist = DistanceBetweenSegments(lastPosition, nextPosition, pBottom, pTop, out Vector3 onPath, out Vector3 onTarget);
                // Raio de colisão preciso (0.18m) para não pegar pessoas ao lado da trajetória mirada
                float detectionRadius = hitRadius + 0.18f;

                if (dist <= detectionRadius)
                {
                    float progress = Vector3.Dot(onPath - lastPosition, rayDir);
                    if (progress < earliestProgress - 0.05f || (Mathf.Abs(progress - earliestProgress) <= 0.05f && dist < smallestDist))
                    {
                        earliestProgress = progress;
                        smallestDist = dist;
                        bestPlayer = player;
                        bestNpc = null;
                        bestHitPoint = onTarget;
                    }
                }
            }
        }

        // 2. Verificação contínua (CCD) contra NPCs
        // Se o Hunter mirou especificamente em um Player, não atinge nenhum NPC
        if (targetedPlayer == null && NPCController.ActiveNpcs != null)
        {
            var npcs = NPCController.ActiveNpcs;
            for (int i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                if (npc == null || npc.IsDead) continue;

                // Se o Hunter mirou em um NPC específico, só aquele NPC pode ser atingido
                if (targetedNpc != null && npc != targetedNpc) continue;

                Vector3 nBottom = npc.transform.position + Vector3.up * 0.25f;
                Vector3 nTop = npc.transform.position + Vector3.up * 1.65f;

                float dist = DistanceBetweenSegments(lastPosition, nextPosition, nBottom, nTop, out Vector3 onPath, out Vector3 onTarget);
                float detectionRadius = hitRadius + 0.18f;

                if (dist <= detectionRadius)
                {
                    float progress = Vector3.Dot(onPath - lastPosition, rayDir);
                    if (progress < earliestProgress - 0.05f || (Mathf.Abs(progress - earliestProgress) <= 0.05f && dist < smallestDist))
                    {
                        earliestProgress = progress;
                        smallestDist = dist;
                        bestPlayer = null;
                        bestNpc = npc;
                        bestHitPoint = onTarget;
                    }
                }
            }
        }

        // 3. Verificação contra o cenário / obstáculos físicos
        bool envBlocked = false;
        RaycastHit envHit = default;
        if (Physics.SphereCast(lastPosition, hitRadius, rayDir, out envHit, distance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            var p = envHit.collider.GetComponentInParent<PlayerController>();
            if (p != null && (p == caster || p.IsHunter || (casterNetworkId != ulong.MaxValue && p.NetworkObjectId == casterNetworkId)))
            {
                // Ignora colisão com o próprio caster
            }
            // Ignora colisões rasteiras no chão nos primeiros instantes após o lançamento
            else if (Time.time - spawnTime < 0.12f && Vector3.Dot(envHit.normal, Vector3.up) > 0.5f)
            {
                // Ignora chão inicial
            }
            else
            {
                envBlocked = true;
            }
        }

        // Se uma parede/obstáculo físico colidiu antes de qualquer personagem:
        if (envBlocked && envHit.distance < earliestProgress)
        {
            HandleHit(envHit.collider, envHit.point, envHit.normal);
            return;
        }

        // Se atingiu um jogador sobrevivente (alvo único):
        if (bestPlayer != null)
        {
            HandlePlayerHit(bestPlayer, bestHitPoint, -moveDirection);
            return;
        }

        // Se atingiu um NPC (alvo único):
        if (bestNpc != null)
        {
            HandleNpcHit(bestNpc, bestHitPoint, -moveDirection);
            return;
        }

        // Se colidiu com o cenário:
        if (envBlocked)
        {
            HandleHit(envHit.collider, envHit.point, envHit.normal);
            return;
        }

        lastPosition = transform.position;
        transform.position = nextPosition;
    }

    private void HandlePlayerHit(PlayerController targetPlayer, Vector3 point, Vector3 normal)
    {
        if (hasExploded) return;
        hasExploded = true;

        try
        {
            if (targetPlayer != null && !targetPlayer.IsHunter && !targetPlayer.IsDead && !targetPlayer.IsGhost)
            {
                Debug.Log($"[HunterFireballProjectile] Bola de Fogo atingiu e eliminou Player {targetPlayer.NetworkObjectId}!");
                if (caster != null)
                {
                    caster.ReportFireballKillPlayer(targetPlayer);
                }
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    targetPlayer.KillByHunter(caster, "fireball");
                }
                else
                {
                    var localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject()?.GetComponent<PlayerController>();
                    if (localPlayer != null)
                    {
                        localPlayer.ReportFireballKillPlayer(targetPlayer);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[HunterFireballProjectile] Erro ao aplicar dano da bola de fogo no Player: {ex}");
        }

        Explode(point, normal, isRealPlayer: true);
    }

    private void HandleNpcHit(NPCController targetNpc, Vector3 point, Vector3 normal)
    {
        if (hasExploded) return;
        hasExploded = true;

        try
        {
            if (targetNpc != null && !targetNpc.IsDead)
            {
                Debug.Log($"[HunterFireballProjectile] Bola de Fogo atingiu e eliminou NPC {targetNpc.NetworkObjectId}!");
                if (caster != null)
                {
                    caster.ReportFireballKillNpc(targetNpc);
                }
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    targetNpc.KillByHunter(caster);
                }
                else
                {
                    var localPlayer = NetworkManager.Singleton?.SpawnManager?.GetLocalPlayerObject()?.GetComponent<PlayerController>();
                    if (localPlayer != null)
                    {
                        localPlayer.ReportFireballKillNpc(targetNpc);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[HunterFireballProjectile] Erro ao aplicar dano da bola de fogo no NPC: {ex}");
        }

        Explode(point, normal, isRealPlayer: false);
    }

    private void HandleHit(Collider col, Vector3 point, Vector3 normal)
    {
        if (hasExploded) return;
        hasExploded = true;

        bool hitRealPlayer = false;

        try
        {
            var targetPlayer = col.GetComponentInParent<PlayerController>();
            if (targetPlayer != null && !targetPlayer.IsHunter && !targetPlayer.IsDead && !targetPlayer.IsGhost)
            {
                hitRealPlayer = true;
                Debug.Log($"[HunterFireballProjectile] Bola de Fogo explodiu no Player {targetPlayer.NetworkObjectId}!");
                if (caster != null)
                {
                    caster.ReportFireballKillPlayer(targetPlayer);
                }
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    targetPlayer.KillByHunter(caster, "fireball");
                }
            }
            else
            {
                var targetNpc = col.GetComponentInParent<NPCController>();
                if (targetNpc != null && !targetNpc.IsDead)
                {
                    Debug.Log($"[HunterFireballProjectile] Bola de Fogo explodiu no NPC {targetNpc.NetworkObjectId}!");
                    if (caster != null)
                    {
                        caster.ReportFireballKillNpc(targetNpc);
                    }
                    else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                    {
                        targetNpc.KillByHunter(caster);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[HunterFireballProjectile] Erro ao aplicar dano da bola de fogo: {ex}");
        }

        Explode(point, normal, isRealPlayer: hitRealPlayer);
    }

    public void Explode(Vector3 point, Vector3 normal, bool isRealPlayer = false)
    {
        GameAudioManager.Instance?.PlayFireballExplosion(point);

        // Alvo único: projétil atinge apenas o alvo de impacto direto
        // Player real -> efeito roxo; NPC ou ambiente -> variante de fogo
        GameObject prefabToSpawn = isRealPlayer ? explosionPrefab : fireExplosionPrefab;

        if (prefabToSpawn == null)
        {
            if (isRealPlayer)
            {
                prefabToSpawn = explosionPrefab;
#if UNITY_EDITOR
                if (prefabToSpawn == null)
                {
                    prefabToSpawn = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Eric VFX Studio/Free Game VFX/Prefab/FX_Purple_Hit_02.prefab");
                }
#endif
            }
            else
            {
                prefabToSpawn = fireExplosionPrefab;
#if UNITY_EDITOR
                if (prefabToSpawn == null)
                {
                    prefabToSpawn = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Fire_Hit_02.prefab");
                }
#endif
                if (prefabToSpawn == null)
                {
                    prefabToSpawn = Resources.Load<GameObject>("FX_Fire_Hit_02");
                }
                if (prefabToSpawn == null)
                {
                    prefabToSpawn = explosionPrefab; // fallback se fireExplosionPrefab não for encontrado
                }
            }
        }

        if (prefabToSpawn != null)
        {
            Quaternion rot = normal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            GameObject exp = Instantiate(prefabToSpawn, point, rot);
            if (Application.isPlaying)
            {
                Destroy(exp, explosionLifetime);
            }
            else
            {
                DestroyImmediate(exp);
            }
        }

        if (Application.isPlaying)
        {
            Destroy(gameObject);
        }
        else
        {
            DestroyImmediate(gameObject);
        }
    }

    /// <summary>
    /// Calcula a distância mínima entre dois segmentos no espaço 3D (CCD exato).
    /// Segmento 1: p1 -> p2 (trajetória da bola de fogo neste frame)
    /// Segmento 2: p3 -> p4 (cilindro vertical do corpo do alvo)
    /// </summary>
    public static float DistanceBetweenSegments(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 closestOnSegment1, out Vector3 closestOnSegment2)
    {
        Vector3 u = p2 - p1;
        Vector3 v = p4 - p3;
        Vector3 w = p1 - p3;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);
        float D = a * c - b * b;

        float sN, sD = D;
        float tN, tD = D;

        if (D < 0.0001f)
        {
            sN = 0.0f;
            sD = 1.0f;
            tN = e;
            tD = c;
        }
        else
        {
            sN = (b * e - c * d);
            tN = (a * e - b * d);
            if (sN < 0.0f)
            {
                sN = 0.0f;
                tN = e;
                tD = c;
            }
            else if (sN > sD)
            {
                sN = sD;
                tN = e + b;
                tD = c;
            }
        }

        if (tN < 0.0f)
        {
            tN = 0.0f;
            if (-d < 0.0f)
                sN = 0.0f;
            else if (-d > a)
                sN = sD;
            else
            {
                sN = -d;
                sD = a;
            }
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-d + b) < 0.0f)
                sN = 0.0f;
            else if ((-d + b) > a)
                sN = sD;
            else
            {
                sN = (-d + b);
                sD = a;
            }
        }

        float sc = (Mathf.Abs(sN) < 0.0001f ? 0.0f : sN / sD);
        float tc = (Mathf.Abs(tN) < 0.0001f ? 0.0f : tN / tD);

        closestOnSegment1 = p1 + (sc * u);
        closestOnSegment2 = p3 + (tc * v);

        return Vector3.Distance(closestOnSegment1, closestOnSegment2);
    }
}
