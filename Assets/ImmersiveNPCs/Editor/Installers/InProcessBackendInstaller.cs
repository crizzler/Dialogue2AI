#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace ImmersiveNPCs.Editor
{
    [InitializeOnLoad]
    [DefaultExecutionOrder(-240)]
    internal static class InProcessBackendInstaller
    {
        private const string PromptKey = "ImmersiveNPCs.InProcessBackendPrompted";
        private const string PluginRoot = "Assets/ImmersiveNPCs/Plugins";
        private const string PluginName = "immersivenpcs_llama";
        private const string PluginZipUrl = "https://github.com/immersive-npcs/immersive-npcs-llama/releases/download/v0.1.0/immersivenpcs_llama-unity.zip";

        static InProcessBackendInstaller() => EditorApplication.delayCall += FirstCheck;

        public static bool IsPluginInstalled()
        {
            if (!Directory.Exists(PluginRoot))
            {
                return false;
            }

            string[] files = Directory.GetFiles(PluginRoot, "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileNameWithoutExtension(files[i]);
                if (!string.Equals(fileName, PluginName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                if (ext == ".dll" || ext == ".so" || ext == ".dylib")
                {
                    return true;
                }
            }

            return false;
        }

        public static void InstallFromMenu()
        {
            if (!ConfirmInstallPrompt())
            {
                return;
            }

            InstallPlugin();
        }

        private static void FirstCheck()
        {
            EditorApplication.delayCall -= FirstCheck;

            if (IsPluginInstalled())
            {
                return;
            }

            AIConversationSettings settings = AISettingsAssetUtility.FindAnySettings();
            if (settings == null || settings.localBackend != LocalBackendMode.InProcess)
            {
                return;
            }

            if (SessionState.GetBool(PromptKey, false))
            {
                return;
            }

            SessionState.SetBool(PromptKey, true);
            if (ConfirmInstallPrompt())
            {
                InstallPlugin();
            }
        }

        // Menu entry: Tools/Immersive NPCs/Install In-Process Backend
        [MenuItem("Tools/Immersive NPCs/Install In-Process Backend", priority = 2100)]
        private static void MenuInstall()
        {
            InstallFromMenu();
        }

        [MenuItem("Tools/Immersive NPCs/Install In-Process Backend", true)]
        private static bool MenuValidateInstall()
        {
            return !IsPluginInstalled();
        }

        private static bool ConfirmInstallPrompt()
        {
            return EditorUtility.DisplayDialog(
                "Install In-Process Backend",
                "Download and install the in-process backend plugin now?\n\n" +
                "This will fetch a prebuilt native library from GitHub and place it under Assets/ImmersiveNPCs/Plugins.\n\n" +
                "You can cancel and install it manually later.",
                "Install", "Cancel");
        }

        private static void InstallPlugin()
        {
            try
            {
                string tempZipPath = Path.Combine(Path.GetTempPath(), "ImmersiveNPCs_Llama.zip");
                string extractPath = Path.Combine(Path.GetTempPath(), "ImmersiveNPCs_Llama");

                new WebClient().DownloadFile(PluginZipUrl, tempZipPath);

                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                ZipFile.ExtractToDirectory(tempZipPath, extractPath);

                string sourceDir = FindPluginSource(extractPath);
                if (string.IsNullOrEmpty(sourceDir))
                {
                    Debug.LogError("[ImmersiveNPCs] Plugin download did not contain expected files.");
                    return;
                }

                CopyDirectory(sourceDir, PluginRoot);
                Debug.Log("[ImmersiveNPCs] In-process backend installed to " + PluginRoot);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ImmersiveNPCs] In-process backend installation failed: " + ex);
            }
            finally
            {
                AssetDatabase.Refresh();
            }
        }

        private static string FindPluginSource(string extractRoot)
        {
            string[] pluginFiles = Directory.GetFiles(extractRoot, PluginName + ".*", SearchOption.AllDirectories);
            for (int i = 0; i < pluginFiles.Length; i++)
            {
                string ext = Path.GetExtension(pluginFiles[i]).ToLowerInvariant();
                if (ext == ".dll" || ext == ".so" || ext == ".dylib")
                {
                    string directory = Path.GetDirectoryName(pluginFiles[i]);
                    if (directory == null)
                    {
                        continue;
                    }

                    if (Path.GetFileName(directory).Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                    {
                        return directory;
                    }

                    DirectoryInfo parent = Directory.GetParent(directory);
                    if (parent != null && parent.Name.Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                    {
                        return parent.FullName;
                    }

                    return Directory.GetParent(directory)?.FullName ?? directory;
                }
            }

            return string.Empty;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceDir.Length + 1);
                string destination = Path.Combine(targetDir, relative);
                string destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                File.Copy(file, destination, true);
            }
        }
    }
}
#endif
