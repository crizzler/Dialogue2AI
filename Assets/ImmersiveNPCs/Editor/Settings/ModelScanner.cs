#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using ImmersiveNPCs;

namespace ImmersiveNPCs.Editor
{
    internal static class ModelScanner
    {
        public static List<string> ScanModels(string projectRelativeFolder)
        {
            return ScanModels(projectRelativeFolder, LocalBackendMode.InProcess);
        }

        public static List<string> ScanModels(string projectRelativeFolder, LocalBackendMode backendMode)
        {
            List<string> results = new List<string>();
            string folderPath = ImmersiveNPCs.PathUtility.ResolveProjectPath(projectRelativeFolder);
            if (!Directory.Exists(folderPath))
            {
                return results;
            }

            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                if (IsSupportedModelExtension(ext, backendMode))
                {
                    string relative = Path.GetRelativePath(folderPath, files[i]).Replace('\\', '/');
                    results.Add(relative);
                }
            }

            results.Sort();
            return results;
        }

        private static bool IsSupportedModelExtension(string extension, LocalBackendMode backendMode)
        {
            if (backendMode == LocalBackendMode.Sentis)
            {
                return extension == ".sentis";
            }

            return extension == ".gguf" || extension == ".bin" || extension == ".ggml";
        }
    }
}
#endif
