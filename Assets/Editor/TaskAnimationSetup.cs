#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class TaskAnimationSetup
{
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    private const string ClipPath = "Assets/Animations/TaskInteract.anim";
    private const string ArtifactDir = @"C:\Users\hspsz\.gemini\antigravity\brain\b13a7445-c510-4b50-be98-d6c792ca05a1";

    [MenuItem("Tools/Setup Task Interact Animation")]
    public static void SetupAndRender()
    {
        AssetDatabase.Refresh();

        AnimationClip interactClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (interactClip == null)
        {
            Debug.LogError($"[TaskAnimationSetup] Não foi possível carregar o clip em {ClipPath}!");
            return;
        }

        Debug.Log($"[TaskAnimationSetup] interactClip loaded: name={interactClip.name}, isHumanMotion={interactClip.isHumanMotion}, length={interactClip.length}, legacy={interactClip.legacy}");
        AnimationClip kickClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/FrontKick.anim");
        if (kickClip != null)
        {
            Debug.Log($"[TaskAnimationSetup] kickClip: name={kickClip.name}, isHumanMotion={kickClip.isHumanMotion}, length={kickClip.length}, legacy={kickClip.legacy}");
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[TaskAnimationSetup] Não foi possível carregar o controller em {ControllerPath}!");
            return;
        }

        // 1. Garante que os parâmetros existem
        bool hasInteractTrigger = false;
        bool hasIsInteractingBool = false;
        foreach (var p in controller.parameters)
        {
            if (p.name == "Interact" && p.type == AnimatorControllerParameterType.Trigger) hasInteractTrigger = true;
            if (p.name == "IsInteracting" && p.type == AnimatorControllerParameterType.Bool) hasIsInteractingBool = true;
        }

        if (!hasInteractTrigger)
        {
            controller.AddParameter("Interact", AnimatorControllerParameterType.Trigger);
            Debug.Log("[TaskAnimationSetup] Adicionado parâmetro Trigger 'Interact'.");
        }

        if (!hasIsInteractingBool)
        {
            controller.AddParameter("IsInteracting", AnimatorControllerParameterType.Bool);
            Debug.Log("[TaskAnimationSetup] Adicionado parâmetro Bool 'IsInteracting'.");
        }

        // 2. Localiza ou cria o estado 'Task Interact' na Base Layer
        var baseStateMachine = controller.layers[0].stateMachine;
        AnimatorState taskState = null;
        AnimatorState idleState = null;
        AnimatorState deathState = null;

        foreach (var childState in baseStateMachine.states)
        {
            if (childState.state.name == "Task Interact")
            {
                taskState = childState.state;
            }
            else if (childState.state.name == "Idle")
            {
                idleState = childState.state;
            }
            else if (childState.state.name == "Falling Back Death")
            {
                deathState = childState.state;
            }
        }

        if (taskState == null)
        {
            taskState = baseStateMachine.AddState("Task Interact", new Vector3(580f, -280f, 0f));
            Debug.Log("[TaskAnimationSetup] Estado 'Task Interact' criado na Base Layer.");
        }

        taskState.motion = interactClip;
        taskState.speed = 1.0f;

        // 3. Garante transição do AnyState -> Task Interact com a condição 'Interact' (Trigger)
        bool hasAnyTransition = false;
        foreach (var anyTrans in baseStateMachine.anyStateTransitions)
        {
            if (anyTrans.destinationState == taskState)
            {
                hasAnyTransition = true;
                break;
            }
        }

        if (!hasAnyTransition)
        {
            var anyTrans = baseStateMachine.AddAnyStateTransition(taskState);
            anyTrans.duration = 0.12f;
            anyTrans.hasExitTime = false;
            anyTrans.canTransitionToSelf = false;
            anyTrans.AddCondition(AnimatorConditionMode.If, 0, "Interact");
            Debug.Log("[TaskAnimationSetup] Transição AnyState -> Task Interact configurada com trigger 'Interact'.");
        }

        // 4. Garante transição de saída: Task Interact -> Idle quando IsInteracting == false
        bool hasExitTransition = false;
        foreach (var trans in taskState.transitions)
        {
            if (trans.destinationState == idleState)
            {
                hasExitTransition = true;
                break;
            }
        }

        if (!hasExitTransition && idleState != null)
        {
            var exitTrans = taskState.AddTransition(idleState);
            exitTrans.hasExitTime = false;
            exitTrans.duration = 0.20f;
            exitTrans.canTransitionToSelf = false;
            exitTrans.AddCondition(AnimatorConditionMode.IfNot, 0, "IsInteracting");
            Debug.Log("[TaskAnimationSetup] Transição Task Interact -> Idle configurada com IsInteracting == false.");
        }

        // 5. Garante transição para Death caso o player seja morto enquanto interage
        if (deathState != null)
        {
            bool hasDeathTrans = false;
            foreach (var trans in taskState.transitions)
            {
                if (trans.destinationState == deathState) { hasDeathTrans = true; break; }
            }
            if (!hasDeathTrans)
            {
                var deathTrans = taskState.AddTransition(deathState);
                deathTrans.hasExitTime = false;
                deathTrans.duration = 0.08f;
                deathTrans.canTransitionToSelf = false;
                deathTrans.AddCondition(AnimatorConditionMode.If, 0, "Die");
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[TaskAnimationSetup] PlayerAnimator.controller salvo com sucesso!");

        // 6. Gera capturas de tela de preview para verificação visual da animação
        RenderPreviews(interactClip);
    }

    private static void RenderPreviews(AnimationClip interactClip)
    {
        string playerPrefabPath = "Assets/Prefab/Player.prefab";
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogWarning("[TaskAnimationSetup] Player.prefab não encontrado para preview.");
            return;
        }

        GameObject inst = Object.Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) cam.enabled = false;

        Animator anim = inst.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.enabled = true;
            anim.Update(0.1f); // Inicializa o Animator
            anim.Play("Task Interact", 0, 0.40f); // Transiciona para a animação de task
            anim.Update(0.1f); // Avalia a pose do Task Interact
        }

        GameObject env = new GameObject("TestEnv");
        // Chão neutro
        CreateEnvCube(new Vector3(0f, -0.05f, 0f), new Vector3(10f, 0.1f, 10f), new Color(0.20f, 0.22f, 0.26f), env.transform);
        // Estação de Task / Console em frente ao personagem a Z = 1.1f
        CreateEnvCube(new Vector3(0f, 0.5f, 1.1f), new Vector3(0.9f, 1.0f, 0.6f), new Color(0.35f, 0.38f, 0.45f), env.transform);
        // Tela do terminal / botões
        CreateEnvCube(new Vector3(0f, 1.05f, 1.1f), new Vector3(0.7f, 0.1f, 0.4f), new Color(0.2f, 0.7f, 0.9f), env.transform);

        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.25f;
        lightObj.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

        GameObject camObj = new GameObject("CaptureCam");
        Camera c = camObj.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        c.fieldOfView = 45f;

        int width = 960;
        int height = 540;
        RenderTexture rt = new RenderTexture(width, height, 24);
        c.targetTexture = rt;

        // Ângulo Frontal / 3/4 mostrando claramente o chute na estação de task
        camObj.transform.position = new Vector3(2.0f, 1.0f, 1.8f);
        camObj.transform.LookAt(new Vector3(0f, 0.65f, 0.6f));
        c.Render();
        SaveRenderTexture(rt, Path.Combine(ArtifactDir, "preview_task_interact_front_three_quarter.png"));

        // Ângulo Lateral
        camObj.transform.position = new Vector3(2.2f, 1.25f, 0.5f);
        camObj.transform.LookAt(new Vector3(0f, 1.0f, 0.5f));
        c.Render();
        SaveRenderTexture(rt, Path.Combine(ArtifactDir, "preview_task_interact_side.png"));

        // Ângulo Atrás do ombro (Over the shoulder)
        camObj.transform.position = new Vector3(0.55f, 1.45f, -1.2f);
        camObj.transform.LookAt(new Vector3(0f, 0.95f, 1.1f));
        c.Render();
        SaveRenderTexture(rt, Path.Combine(ArtifactDir, "preview_task_interact_over_shoulder.png"));

        c.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camObj);
        Object.DestroyImmediate(lightObj);
        Object.DestroyImmediate(env);
        Object.DestroyImmediate(inst);

        Debug.Log("[TaskAnimationSetup] Previews gerados com sucesso no diretório de artefatos!");
    }

    private static void CreateEnvCube(Vector3 pos, Vector3 scale, Color color, Transform parent)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = pos;
        cube.transform.localScale = scale;
        cube.transform.SetParent(parent);
        var renderer = cube.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            renderer.material = mat;
        }
    }

    private static void SaveRenderTexture(RenderTexture rt, string outPath)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(outPath, png);
        Debug.Log($"[TaskAnimationSetup] Preview salvo em: {outPath}");
    }
}
#endif
