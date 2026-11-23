using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TaskManager))]
public class TaskManagerEditor : Editor
{
    private SerializedProperty kickObjectWeightProp;
    private SerializedProperty kickHunterWeightProp;
    private SerializedProperty danceNearHunterWeightProp;
    private SerializedProperty preventConsecutiveHunterTasksProp;
    private SerializedProperty allowSimultaneousDifferentHunterTasksProp;

    private void OnEnable()
    {
        kickObjectWeightProp = serializedObject.FindProperty("kickObjectWeight");
        kickHunterWeightProp = serializedObject.FindProperty("kickHunterWeight");
        danceNearHunterWeightProp = serializedObject.FindProperty("danceNearHunterWeight");
        preventConsecutiveHunterTasksProp = serializedObject.FindProperty("preventConsecutiveHunterTasks");
        allowSimultaneousDifferentHunterTasksProp = serializedObject.FindProperty("allowSimultaneousDifferentHunterTasks");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 1. Desenha as propriedades normais antes da seção de proporção
        DrawPropertiesExcluding(serializedObject,
            "m_Script",
            "kickObjectWeight",
            "kickHunterWeight",
            "danceNearHunterWeight",
            "preventConsecutiveHunterTasks",
            "allowSimultaneousDifferentHunterTasks"
        );

        EditorGUILayout.Space(12);

        // 2. Seção de Proporção e Probabilidades
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.9f, 0.7f, 0.2f) }
            };
            EditorGUILayout.LabelField("⚖️ Sistema Proporcional de Tasks", headerStyle);
            EditorGUILayout.LabelField("Configure os pesos para definir a chance relativa de cada tipo de task ser sorteada.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.PropertyField(kickObjectWeightProp, new GUIContent("📦 Interagir com Objetos (Cenário)"));
            EditorGUILayout.PropertyField(kickHunterWeightProp, new GUIContent("💀 Chutar a Morte (Hunter)"));
            EditorGUILayout.PropertyField(danceNearHunterWeightProp, new GUIContent("💃 Dançar na Frente da Morte [2 ou 3]"));

            float objWeight = Mathf.Max(0f, kickObjectWeightProp.floatValue);
            float kickWeight = Mathf.Max(0f, kickHunterWeightProp.floatValue);
            float danceWeight = Mathf.Max(0f, danceNearHunterWeightProp.floatValue);
            float total = objWeight + kickWeight + danceWeight;

            float pctObj = total > 0.0001f ? (objWeight / total) * 100f : 0f;
            float pctKick = total > 0.0001f ? (kickWeight / total) * 100f : 0f;
            float pctDance = total > 0.0001f ? (danceWeight / total) * 100f : 0f;

            EditorGUILayout.Space(6);

            // Barra visual colorida
            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(16), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && total > 0.0001f)
            {
                float wObj = (pctObj / 100f) * barRect.width;
                float wKick = (pctKick / 100f) * barRect.width;
                float wDance = barRect.width - (wObj + wKick);

                Rect rObj = new Rect(barRect.x, barRect.y, wObj, barRect.height);
                Rect rKick = new Rect(barRect.x + wObj, barRect.y, wKick, barRect.height);
                Rect rDance = new Rect(barRect.x + wObj + wKick, barRect.y, wDance, barRect.height);

                EditorGUI.DrawRect(rObj, new Color(0.2f, 0.6f, 0.9f));    // Azul: Objetos
                EditorGUI.DrawRect(rKick, new Color(0.9f, 0.3f, 0.3f));   // Vermelho: Chutar Hunter
                EditorGUI.DrawRect(rDance, new Color(0.7f, 0.3f, 0.9f));  // Roxo: Dançar perto Hunter
            }

            EditorGUILayout.Space(4);

            // Resumo de Porcentagens
            string summaryText =
                $"📊 Probabilidade Efetiva de Sorteio:\n" +
                $"  • 📦 Interagir com Objetos: {pctObj:F1}%\n" +
                $"  • 💀 Chutar a Morte: {pctKick:F1}%\n" +
                $"  • 💃 Dançar na Frente da Morte: {pctDance:F1}%\n" +
                $"  (Soma dos pesos: {total:F1})";

            EditorGUILayout.HelpBox(summaryText, MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Predefinições Rápidas:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("Chances Iguais (33.3% cada)", EditorStyles.miniButtonLeft))
                {
                    kickObjectWeightProp.floatValue = 33.333f;
                    kickHunterWeightProp.floatValue = 33.333f;
                    danceNearHunterWeightProp.floatValue = 33.333f;
                }
                if (GUILayout.Button("Foco Objetos (50/25/25)", EditorStyles.miniButtonMid))
                {
                    kickObjectWeightProp.floatValue = 50f;
                    kickHunterWeightProp.floatValue = 25f;
                    danceNearHunterWeightProp.floatValue = 25f;
                }
                if (GUILayout.Button("Mais Objetos (70/15/15)", EditorStyles.miniButtonMid))
                {
                    kickObjectWeightProp.floatValue = 70f;
                    kickHunterWeightProp.floatValue = 15f;
                    danceNearHunterWeightProp.floatValue = 15f;
                }
                if (GUILayout.Button("Apenas Objetos (100/0/0)", EditorStyles.miniButtonRight))
                {
                    kickObjectWeightProp.floatValue = 100f;
                    kickHunterWeightProp.floatValue = 0f;
                    danceNearHunterWeightProp.floatValue = 0f;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.PropertyField(preventConsecutiveHunterTasksProp, new GUIContent("Evitar Tarefas Consecutivas da Morte"));
            EditorGUILayout.PropertyField(allowSimultaneousDifferentHunterTasksProp, new GUIContent("Permitir Hunter Tasks Simultâneas Distintas"));
        }
        EditorGUILayout.EndVertical();

        serializedObject.ApplyModifiedProperties();
    }
}
