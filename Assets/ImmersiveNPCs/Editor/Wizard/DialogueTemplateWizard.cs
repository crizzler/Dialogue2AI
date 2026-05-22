#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ImmersiveNPCs.Editor
{
    public class DialogueTemplateWizard : EditorWindow
    {
        private string npcId = "npc_1";
        private int slots = 4;
        private string outputFolder = "Assets/ImmersiveNPCs/Examples/Content";
        private string fileName = "LivingDialogueTemplate.yarn";

        [MenuItem("Tools/Immersive NPCs/Dialogue Template Wizard")]
        private static void Open()
        {
            GetWindow<DialogueTemplateWizard>("Dialogue Template Wizard");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Create a dialogue template", EditorStyles.boldLabel);
            npcId = EditorGUILayout.TextField("NPC Id", npcId);
            slots = EditorGUILayout.IntSlider("Slots", slots, 2, 6);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            fileName = EditorGUILayout.TextField("File Name", fileName);

            if (GUILayout.Button("Generate Template"))
            {
                GenerateTemplate();
            }
        }

        private void GenerateTemplate()
        {
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "LivingDialogueTemplate.yarn";
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            string path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, BuildTemplate());
            AssetDatabase.Refresh();
            Debug.Log("Template created at: " + path);
        }

        private string BuildTemplate()
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("title: AI_Hub");
            builder.AppendLine("---");
            builder.AppendLine("<<declare $ai_npc_line = \"\">>");
            for (int i = 0; i < slots; i++)
            {
                builder.AppendLine("<<declare $ai_opt_" + i + " = \"\">>");
            }
            builder.AppendLine("<<declare $ai_opt_count = 0>>");
            builder.AppendLine("<<declare $ai_last_choice = \"\">>");
            builder.AppendLine("<<ai_prefetch npcId=\"" + npcId + "\" slots=" + slots + ">>");
            builder.AppendLine("{$ai_npc_line}");
            builder.AppendLine();

            for (int i = 0; i < slots; i++)
            {
                builder.AppendLine("-> {$ai_opt_" + i + "}");
                builder.AppendLine("    <<jump AI_Opt_" + i + ">>");
            }

            builder.AppendLine("===");
            builder.AppendLine();

            for (int i = 0; i < slots; i++)
            {
                builder.AppendLine("title: AI_Opt_" + i);
                builder.AppendLine("---");
                builder.AppendLine("<<ai_choose slot=" + i + ">>");
                builder.AppendLine("<<jump AI_Hub>>");
                builder.AppendLine("===");
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
#endif
