#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class HunterPrefabCreator
{
    [MenuItem("Tools/Generate Hunter Prefab From Player")]
    public static void GenerateHunterPrefab()
    {
        string playerPrefabPath = "Assets/Prefab/Player.prefab";
        string hunterModelPath = "Assets/Models/Hunter.fbx";
        string hunterPrefabPath = "Assets/Prefab/Hunter.prefab";
        string animatorControllerPath = "Assets/Animations/PlayerAnimator.controller";

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError($"[HunterPrefabCreator] Player.prefab nÃ£o encontrado em: {playerPrefabPath}");
            return;
        }

        GameObject hunterModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hunterModelPath);
        if (hunterModelPrefab == null)
        {
            Debug.LogError($"[HunterPrefabCreator] Hunter.fbx nÃ£o encontrado em: {hunterModelPath}");
            return;
        }

        RuntimeAnimatorController animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(animatorControllerPath);

        // Instancia uma cÃ³pia do Player na cena para editar
        GameObject hunterInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        hunterInstance.name = "Hunter";

        // Desconecta o link do prefab original do Player
        PrefabUtility.UnpackPrefabInstance(hunterInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // 1. Manter o CharacterController nas mesmas dimensÃµes do Player (~2m de altura)
        CharacterController charController = hunterInstance.GetComponent<CharacterController>();
        CharacterController playerCC = playerPrefab.GetComponent<CharacterController>();
        if (charController != null && playerCC != null)
        {
            charController.height = playerCC.height;
            charController.radius = playerCC.radius;
            charController.center = playerCC.center;
            charController.slopeLimit = playerCC.slopeLimit;
            charController.stepOffset = playerCC.stepOffset;
            charController.skinWidth = playerCC.skinWidth;
            charController.minMoveDistance = playerCC.minMoveDistance;
        }

        // Configurar o material M_GrimReaper com o Toon Outline shader do player
        ConfigureHunterToonMaterial();

        // 2. Remover PlayerCharacterRandomizer (pois o Hunter Ã© um personagem fixo Ãºnico)
        PlayerCharacterRandomizer randomizer = hunterInstance.GetComponent<PlayerCharacterRandomizer>();
        if (randomizer != null)
        {
            Object.DestroyImmediate(randomizer);
        }

        // 3. Limpar o GameObject Visuals e colocar o modelo do Hunter
        Transform visualsTransform = hunterInstance.transform.Find("Visuals");
        if (visualsTransform == null)
        {
            GameObject visualsObj = new GameObject("Visuals");
            visualsObj.transform.SetParent(hunterInstance.transform, false);
            visualsTransform = visualsObj.transform;
        }
        else
        {
            // Remover todos os personagens filhos antigos do Visuals
            List<GameObject> childrenToDestroy = new List<GameObject>();
            for (int i = 0; i < visualsTransform.childCount; i++)
            {
                childrenToDestroy.Add(visualsTransform.GetChild(i).gameObject);
            }
            foreach (var child in childrenToDestroy)
            {
                Object.DestroyImmediate(child);
            }
        }

        // Instanciar o modelo Hunter.fbx dentro de Visuals
        GameObject hunterModelInstance = (GameObject)PrefabUtility.InstantiatePrefab(hunterModelPrefab, visualsTransform);
        hunterModelInstance.name = "Hunter_Model";
        hunterModelInstance.transform.localPosition = Vector3.zero;
        hunterModelInstance.transform.localRotation = Quaternion.identity;
        hunterModelInstance.transform.localScale = Vector3.one;

        // 4. Configurar Animator no Visuals
        Animator animator = visualsTransform.GetComponent<Animator>();
        if (animator == null)
        {
            animator = visualsTransform.gameObject.AddComponent<Animator>();
        }
        if (animController != null)
        {
            animator.runtimeAnimatorController = animController;
        }

        // Buscar e vincular o Avatar Humanoid do Hunter.fbx
        Avatar hunterAvatar = null;
        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(hunterModelPath);
        foreach (var sub in subAssets)
        {
            if (sub is Avatar av)
            {
                hunterAvatar = av;
                Debug.Log($"[HunterPrefabCreator] Avatar Humanoid encontrado: {av.name} (isHuman: {av.isHuman}, isValid: {av.isValid})");
                break;
            }
        }
        if (hunterAvatar != null)
        {
            animator.avatar = hunterAvatar;
        }
        else
        {
            Debug.LogWarning("[HunterPrefabCreator] Nenhum Avatar Humanoid encontrado em Hunter.fbx!");
        }

        // 5. Vincular Animator ao ClientNetworkAnimator
        ClientNetworkAnimator clientNetworkAnimator = hunterInstance.GetComponent<ClientNetworkAnimator>();
        if (clientNetworkAnimator != null)
        {
            SerializedObject soNetAnim = new SerializedObject(clientNetworkAnimator);
            SerializedProperty animProp = soNetAnim.FindProperty("m_Animator");
            if (animProp != null)
            {
                animProp.objectReferenceValue = animator;
                soNetAnim.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // 6. Assegurar que os targets do PlayerInput apontem para os componentes do Hunter
        PlayerInput playerInput = hunterInstance.GetComponent<PlayerInput>();
        PlayerController playerController = hunterInstance.GetComponent<PlayerController>();
        if (playerInput != null && playerController != null)
        {
            SerializedObject soInput = new SerializedObject(playerInput);
            SerializedProperty actionEvents = soInput.FindProperty("m_ActionEvents");
            if (actionEvents != null && actionEvents.isArray)
            {
                for (int i = 0; i < actionEvents.arraySize; i++)
                {
                    SerializedProperty eventProp = actionEvents.GetArrayElementAtIndex(i);
                    SerializedProperty calls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                    if (calls != null && calls.isArray)
                    {
                        for (int c = 0; c < calls.arraySize; c++)
                        {
                            SerializedProperty call = calls.GetArrayElementAtIndex(c);
                            call.FindPropertyRelative("m_Target").objectReferenceValue = playerController;
                        }
                    }
                }
                soInput.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        if (playerController != null)
        {
            SerializedObject soCtrl = new SerializedObject(playerController);
            SerializedProperty isHunterProp = soCtrl.FindProperty("isHunter");
            if (isHunterProp != null)
            {
                isHunterProp.boolValue = true;
            }

            GameObject fireballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/HunterFireball.prefab");
            GameObject hitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Eric VFX Studio/Free Game VFX/Prefab/FX_Purple_Hit_02.prefab");
            GameObject handChargePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/HunterFireballInvocation.prefab");

            if (fireballPrefab != null) soCtrl.FindProperty("fireballPrefab").objectReferenceValue = fireballPrefab;
            if (hitPrefab != null) soCtrl.FindProperty("fireballExplosionPrefab").objectReferenceValue = hitPrefab;
            if (handChargePrefab != null) soCtrl.FindProperty("handChargeVfxPrefab").objectReferenceValue = handChargePrefab;
            soCtrl.FindProperty("fireballCooldown").floatValue = 1.8f;

            soCtrl.ApplyModifiedPropertiesWithoutUndo();
        }

        PlayerMovementLogic movLogic = hunterInstance.GetComponent<PlayerMovementLogic>();
        if (movLogic != null)
        {
            SerializedObject soMov = new SerializedObject(movLogic);
            soMov.FindProperty("orientToCamera").boolValue = true;
            soMov.ApplyModifiedPropertiesWithoutUndo();
        }

        PlayerCameraSetup camSetup = hunterInstance.GetComponent<PlayerCameraSetup>();
        if (camSetup != null)
        {
            SerializedObject soCam = new SerializedObject(camSetup);
            soCam.FindProperty("normalFov").floatValue = 65f;
            soCam.FindProperty("aimFov").floatValue = 48f;
            soCam.FindProperty("normalRadius").floatValue = 3.6f;
            soCam.FindProperty("aimRadius").floatValue = 2.4f;
            soCam.FindProperty("zoomSpeed").floatValue = 10f;
            soCam.FindProperty("normalScreenPosition").vector2Value = Vector2.zero;
            soCam.FindProperty("aimScreenPosition").vector2Value = new Vector2(-0.28f, 0f);
            soCam.FindProperty("aimShoulderOffset").floatValue = 0.80f;
            soCam.FindProperty("aimVerticalOffset").floatValue = 0f;
            soCam.FindProperty("normalTargetOffset").vector3Value = new Vector3(0f, 1.4f, 0f);
            soCam.FindProperty("minGroundClearance").floatValue = 0.35f;
            soCam.FindProperty("groundLayerMask").intValue = LayerMask.GetMask("Default");
            soCam.ApplyModifiedPropertiesWithoutUndo();
        }

        // Configurar cÃ¢mera estilo Genshin Impact no Hunter (centralizada, Ã³rbita 360Â°, amplo alcance vertical e anti-colisÃ£o)
        Transform lpc = hunterInstance.transform.Find("LocalPlayerCamera");
        if (lpc != null)
        {
            Unity.Cinemachine.CinemachineCamera cmCam = lpc.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>(true);
            if (cmCam != null)
            {
                Unity.Cinemachine.CinemachineOrbitalFollow orbital = cmCam.GetComponent<Unity.Cinemachine.CinemachineOrbitalFollow>();
                if (orbital != null)
                {
                    orbital.OrbitStyle = Unity.Cinemachine.CinemachineOrbitalFollow.OrbitStyles.Sphere;
                    orbital.Radius = 3.6f;
                    orbital.TargetOffset = new Vector3(0, 1.4f, 0);

                    SerializedObject soOrbital = new SerializedObject(orbital);
                    soOrbital.FindProperty("TrackerSettings.BindingMode").enumValueIndex = 4; // WorldSpace (permite rotaÃ§Ã£o 360 livre independente do corpo)
                    soOrbital.FindProperty("VerticalAxis.Range").vector2Value = new Vector2(-65f, 75f);
                    soOrbital.FindProperty("VerticalAxis.Center").floatValue = 10f;
                    soOrbital.FindProperty("VerticalAxis.Wrap").boolValue = false;
                    soOrbital.FindProperty("HorizontalAxis.Range").vector2Value = new Vector2(-180f, 180f);
                    soOrbital.FindProperty("HorizontalAxis.Wrap").boolValue = true;
                    soOrbital.ApplyModifiedPropertiesWithoutUndo();
                }

                Unity.Cinemachine.CinemachineRotationComposer composer = cmCam.GetComponent<Unity.Cinemachine.CinemachineRotationComposer>();
                if (composer != null)
                {
                    composer.TargetOffset = new Vector3(0, 1.4f, 0);
                    composer.Composition.ScreenPosition = Vector2.zero; // Centralizado estilo Genshin Impact
                }

                // Adicionar e configurar CinemachineDeoccluder para impedir que a cÃ¢mera atravesse paredes e o solo
                Unity.Cinemachine.CinemachineDeoccluder deoccluder = cmCam.GetComponent<Unity.Cinemachine.CinemachineDeoccluder>();
                if (deoccluder == null)
                {
                    deoccluder = cmCam.gameObject.AddComponent<Unity.Cinemachine.CinemachineDeoccluder>();
                }
                deoccluder.CollideAgainst = LayerMask.GetMask("Default");
                deoccluder.MinimumDistanceFromTarget = 0.6f;

                SerializedObject soDeocc = new SerializedObject(deoccluder);
                soDeocc.FindProperty("AvoidObstacles.Enabled").boolValue = true;
                soDeocc.FindProperty("AvoidObstacles.CameraRadius").floatValue = 0.25f;
                soDeocc.FindProperty("AvoidObstacles.Strategy").enumValueIndex = 0; // PullCameraForward
                soDeocc.FindProperty("AvoidObstacles.Damping").floatValue = 0.05f;
                soDeocc.FindProperty("AvoidObstacles.DampingWhenOccluded").floatValue = 0.01f;
                soDeocc.FindProperty("AvoidObstacles.UseFollowTarget.Enabled").boolValue = true;
                soDeocc.FindProperty("AvoidObstacles.UseFollowTarget.YOffset").floatValue = 1.4f;
                soDeocc.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // 7. Configurar Arma da Morte (Death_Scythe) no osso RightHand e efeito de corte (fireslash)
        Transform rightHandBone = null;
        foreach (var t in hunterInstance.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "RightHand")
            {
                rightHandBone = t;
                break;
            }
        }

        GameObject scytheInstance = null;
        if (rightHandBone != null)
        {
            string scythePath = "Assets/Models/Weapons/Death_Scythe.fbx";
            GameObject scythePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(scythePath);
            if (scythePrefab != null)
            {
                scytheInstance = (GameObject)PrefabUtility.InstantiatePrefab(scythePrefab, rightHandBone);
                scytheInstance.name = "Death_Scythe";
                scytheInstance.transform.localPosition = new Vector3(1.08f, 0.11f, -0.76f);
                scytheInstance.transform.localRotation = Quaternion.Euler(359.25f, 302.97f, 249.25f);
                scytheInstance.transform.localScale = new Vector3(1.42f, 1.42f, 1.42f);

                Material scytheMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Weapons/M_DeathScythe.mat");
                if (scytheMat != null)
                {
                    foreach (var r in scytheInstance.GetComponentsInChildren<Renderer>(true))
                    {
                        r.sharedMaterial = scytheMat;
                    }
                }

                // Remove colliders da foice para nÃ£o colidir com o CharacterController
                foreach (var col in scytheInstance.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }

                // Foice inicia oculta (surge apenas durante o golpe da foice)
                scytheInstance.SetActive(false);
            }
            else
            {
                Debug.LogWarning($"[HunterPrefabCreator] Death_Scythe.fbx nÃ£o encontrado em: {scythePath}");
            }
        }

        // Instanciar efeito de corte fireslash no Hunter
        GameObject slashInstance = null;
        string slashPath = "Assets/NamuFX/Simple Stylized Slash/Prefabs/fireslash.prefab";
        GameObject slashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(slashPath);
        if (slashPrefab != null)
        {
            slashInstance = (GameObject)PrefabUtility.InstantiatePrefab(slashPrefab, hunterInstance.transform);
            slashInstance.name = "fireslash";
            slashInstance.transform.localPosition = new Vector3(0.02f, 1.18f, -0.10f);
            slashInstance.transform.localRotation = Quaternion.Euler(294.59f, 295.76f, 240.39f);
            slashInstance.transform.localScale = new Vector3(-0.18f, 0.18f, 0.18f);

            // Aplicar a malha volumÃ©trica "Gordinho B" (2.3x espessura e curvatura 3D)
            string gordinhoMeshPath = "Assets/NamuFX/_CommonAssets/Models/FX_Models/SlashMesh_Gordinho_B.asset";
            UnityEngine.Mesh gordinhoMesh = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(gordinhoMeshPath);
            if (gordinhoMesh != null)
            {
                var fs2 = slashInstance.GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(r => r.name == "fireslash2");
                var fs2_1 = slashInstance.GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(r => r.name == "fireslash2_1");
                if (fs2 != null)
                {
                    fs2.mesh = gordinhoMesh;
                    fs2.SetMeshes(new UnityEngine.Mesh[] { gordinhoMesh });
                    EditorUtility.SetDirty(fs2);
                }
                if (fs2_1 != null)
                {
                    fs2_1.mesh = gordinhoMesh;
                    fs2_1.SetMeshes(new UnityEngine.Mesh[] { gordinhoMesh });
                    EditorUtility.SetDirty(fs2_1);
                }
            }

            // Aumentar as chamas volumÃ©tricas billboard
            var flames = slashInstance.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => p.name == "flames");
            if (flames != null)
            {
                var main = flames.main;
                main.startSize = new ParticleSystem.MinMaxCurve(4f, 5.5f);
                var em = flames.emission;
                ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[1];
                bursts[0] = new ParticleSystem.Burst(0f, 12);
                em.SetBursts(bursts);
                EditorUtility.SetDirty(flames);
            }

            slashInstance.SetActive(true);

            var ps = slashInstance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        // Configurar componente HunterWeaponVisuals no Hunter
        HunterWeaponVisuals weaponVisuals = hunterInstance.GetComponent<HunterWeaponVisuals>();
        if (weaponVisuals == null)
        {
            weaponVisuals = hunterInstance.AddComponent<HunterWeaponVisuals>();
        }
        if (weaponVisuals != null)
        {
            SerializedObject soWeapon = new SerializedObject(weaponVisuals);
            if (scytheInstance != null) soWeapon.FindProperty("scytheObject").objectReferenceValue = scytheInstance;
            if (slashInstance != null) soWeapon.FindProperty("fireSlashEffect").objectReferenceValue = slashInstance.GetComponent<ParticleSystem>();
            soWeapon.FindProperty("attackDuration").floatValue = 1.10f;
            soWeapon.FindProperty("slashDelay").floatValue = 0.20f;
            soWeapon.ApplyModifiedPropertiesWithoutUndo();
        }

        // Configurar componente HunterKnockdownLogic no Hunter
        HunterKnockdownLogic knockdownLogic = hunterInstance.GetComponent<HunterKnockdownLogic>();
        if (knockdownLogic == null)
        {
            knockdownLogic = hunterInstance.AddComponent<HunterKnockdownLogic>();
        }

        // 8. Salvar o novo Prefab independente
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(hunterInstance, hunterPrefabPath);
        Object.DestroyImmediate(hunterInstance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[HunterPrefabCreator] Prefab do Hunter criado com sucesso em: {hunterPrefabPath}!");

        // 8. Registrar no DefaultNetworkPrefabs
        RegisterInNetworkPrefabs(savedPrefab);
    }

    private static void RegisterInNetworkPrefabs(GameObject prefab)
    {
        string networkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        NetworkPrefabsList prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(networkPrefabsPath);
        if (prefabsList == null)
        {
            Debug.LogWarning("[HunterPrefabCreator] DefaultNetworkPrefabs.asset nÃ£o encontrado.");
            return;
        }

        bool alreadyContains = false;
        foreach (var item in prefabsList.PrefabList)
        {
            if (item.Prefab == prefab)
            {
                alreadyContains = true;
                break;
            }
        }

        if (!alreadyContains)
        {
            NetworkPrefab newEntry = new NetworkPrefab();
            newEntry.Prefab = prefab;
            prefabsList.Add(newEntry);
            EditorUtility.SetDirty(prefabsList);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HunterPrefabCreator] Hunter.prefab registrado com sucesso no NetworkPrefabsList!");
        }
        else
        {
            Debug.Log($"[HunterPrefabCreator] Hunter.prefab jÃ¡ constava no NetworkPrefabsList.");
        }
    }

    [MenuItem("Tools/Configure Hunter Toon Material")]
    public static void ConfigureHunterToonMaterial()
    {
        string matPath = "Assets/Models/M_GrimReaper.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Debug.LogError($"[HunterPrefabCreator] {matPath} nÃ£o encontrado!");
            return;
        }

        Shader toonShader = Shader.Find("Toon/TGS_CustomToonOutline");
        if (toonShader == null)
        {
            toonShader = Shader.Find("Toon/TGS_CustomToonOutline");
        }
        if (toonShader == null)
        {
            Debug.LogError("[HunterPrefabCreator] Shader toon nÃ£o encontrado!");
            return;
        }

        mat.shader = toonShader;

        Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Textures/Hunter_BaseColor.png");
        if (baseTex != null)
        {
            if (mat.HasProperty("_TextureSample")) mat.SetTexture("_TextureSample", baseTex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseTex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseTex);
        }

        if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.003f);
        if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", Color.black);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_ReceiveShadows")) mat.SetFloat("_ReceiveShadows", 1f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("[HunterPrefabCreator] M_GrimReaper configurado com Toon/TGS_CustomToonOutline (silhouette/outline preta)!");
    }

    [MenuItem("Tools/Assign Hunter Prefab To GameSpawner")]
    public static void AssignHunterToGameSpawner()
    {
        string gameScenePath = "Assets/Scenes/GameScene.unity";
        string hunterPrefabPath = "Assets/Prefab/Hunter.prefab";

        GameObject hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hunterPrefabPath);
        if (hunterPrefab == null)
        {
            Debug.LogError($"[HunterPrefabCreator] Hunter.prefab nÃ£o encontrado em: {hunterPrefabPath}");
            return;
        }

        // Abre GameScene de forma aditiva para nÃ£o perder o estado atual
        UnityEngine.SceneManagement.Scene gameScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(gameScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        if (!gameScene.IsValid())
        {
            Debug.LogError($"[HunterPrefabCreator] NÃ£o foi possÃ­vel abrir a cena: {gameScenePath}");
            return;
        }

        // Procura o componente GameSpawner na cena
        GameSpawner[] spawners = Object.FindObjectsByType<GameSpawner>(FindObjectsInactive.Include);
        GameSpawner targetSpawner = null;
        foreach (var s in spawners)
        {
            if (s.gameObject.scene == gameScene)
            {
                targetSpawner = s;
                break;
            }
        }

        if (targetSpawner != null)
        {
            SerializedObject so = new SerializedObject(targetSpawner);
            SerializedProperty hunterProp = so.FindProperty("hunterPrefab");
            if (hunterProp != null)
            {
                hunterProp.objectReferenceValue = hunterPrefab;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(targetSpawner);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameScene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(gameScene);
                Debug.Log("[HunterPrefabCreator] hunterPrefab atribuÃ­do com sucesso no GameSpawner da GameScene!");
            }
            else
            {
                Debug.LogWarning("[HunterPrefabCreator] Propriedade 'hunterPrefab' nÃ£o encontrada no GameSpawner.");
            }
        }
        else
        {
            Debug.LogWarning("[HunterPrefabCreator] Nenhum GameSpawner encontrado na GameScene.");
        }

        // Fecha a cena aditiva
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(gameScene, true);
    }

    [MenuItem("Tools/Render Hunter Comparison Preview")]
    public static void RenderHunterComparisonPreview()
    {
        string hunterPath = "Assets/Prefab/Hunter.prefab";
        GameObject hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hunterPath);
        if (hunterPrefab == null) return;

        GameObject hunterInst = Object.Instantiate(hunterPrefab, Vector3.zero, Quaternion.identity);
        foreach (var cam in hunterInst.GetComponentsInChildren<Camera>(true)) cam.enabled = false;

        GameObject camObj = new GameObject("PreviewCam");
        Camera camComp = camObj.AddComponent<Camera>();
        camComp.backgroundColor = new Color(0.2f, 0.25f, 0.3f, 1f);
        camComp.clearFlags = CameraClearFlags.SolidColor;
        // Camera looking at the right profile (Hunter faces -Z or +Z?)
        // Let's set position to (1.2f, 1.7f, 0.2f)
        camObj.transform.position = new Vector3(1.1f, 1.7f, 0.3f);
        camObj.transform.LookAt(new Vector3(0f, 1.7f, 0f));

        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.2f;
        lightObj.transform.rotation = Quaternion.Euler(40f, -60f, 0f);

        RenderTexture rt = new RenderTexture(1024, 1024, 24);
        camComp.targetTexture = rt;
        camComp.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
        tex.Apply();

        string outPath = "C:/Users/hspsz/.gemini/antigravity/brain/8ede8a74-ea3d-41de-b91a-5e28abec99c1/hunter_head_test.png";
        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());

        camComp.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camObj);
        Object.DestroyImmediate(lightObj);
        Object.DestroyImmediate(hunterInst);

        Debug.Log($"[HunterPrefabCreator] Head closeup salvo em: {outPath}");

        RenderTransparencyPreviews();
    }

    [MenuItem("Tools/Render Transparency Previews")]
    public static void RenderTransparencyPreviews()
    {
        string artifactDir = "C:/Users/hspsz/.gemini/antigravity/brain/8073acce-8ef1-4abc-b0fe-a079acb56ed2";
        CaptureCharacterPreview("Assets/Prefab/Player.prefab", System.IO.Path.Combine(artifactDir, "player_normal_opaque.png"), 1.0f, false);
        CaptureCharacterPreview("Assets/Prefab/Player.prefab", System.IO.Path.Combine(artifactDir, "player_lookup_transparent.png"), 0.25f, true);
        CaptureCharacterPreview("Assets/Prefab/Hunter.prefab", System.IO.Path.Combine(artifactDir, "hunter_normal_opaque.png"), 1.0f, false);
        CaptureCharacterPreview("Assets/Prefab/Hunter.prefab", System.IO.Path.Combine(artifactDir, "hunter_lookup_transparent.png"), 0.25f, true);
        Debug.Log("[HunterPrefabCreator] Todos os previews de transparÃªncia gerados com sucesso!");
    }

    private static void CaptureCharacterPreview(string prefabPath, string outPath, float alphaValue, bool isLookUp)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return;

        GameObject inst = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) cam.enabled = false;

        // Fundo com pilares e parede para demonstrar claramente a visÃ£o atravÃ©s do personagem
        GameObject env = new GameObject("TestEnv");
        CreateEnvCube(new Vector3(0f, 2.5f, 4f), new Vector3(3.5f, 5f, 0.4f), new Color(0.85f, 0.35f, 0.2f), env.transform);
        CreateEnvCube(new Vector3(-2.2f, 1.5f, 2.5f), new Vector3(0.8f, 3f, 0.8f), new Color(0.2f, 0.7f, 0.85f), env.transform);
        CreateEnvCube(new Vector3(2.2f, 1.5f, 2.5f), new Vector3(0.8f, 3f, 0.8f), new Color(0.95f, 0.85f, 0.2f), env.transform);
        CreateEnvCube(new Vector3(0f, -0.05f, 1.5f), new Vector3(8f, 0.1f, 8f), new Color(0.3f, 0.35f, 0.4f), env.transform);

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        int charAlphaId = Shader.PropertyToID("_CharacterAlpha");
        mpb.SetFloat(charAlphaId, alphaValue);
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            r.SetPropertyBlock(mpb);
        }

        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.2f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -40f, 0f);

        GameObject camObj = new GameObject("CaptureCam");
        Camera c = camObj.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        c.fieldOfView = 60f;

        if (isLookUp)
        {
            // CÃ¢mera baixa e prÃ³xima olhando para cima (atravÃ©s do personagem)
            camObj.transform.position = new Vector3(0f, 0.55f, -1.5f);
            camObj.transform.rotation = Quaternion.Euler(-28f, 0f, 0f);
        }
        else
        {
            // CÃ¢mera normal em Ã³rbita de jogo
            camObj.transform.position = new Vector3(0f, 2.2f, -3.4f);
            camObj.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
        }

        RenderTexture rt = new RenderTexture(1024, 768, 24);
        c.targetTexture = rt;
        c.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(1024, 768, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0);
        tex.Apply();

        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());

        c.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camObj);
        Object.DestroyImmediate(lightObj);
        Object.DestroyImmediate(env);
        Object.DestroyImmediate(inst);
    }

    private static void CreateEnvCube(Vector3 pos, Vector3 scale, Color col, Transform parent)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = pos;
        cube.transform.localScale = scale;
        cube.transform.SetParent(parent);
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = col;
        cube.GetComponent<Renderer>().material = mat;
    }
}
#endif
