using System.IO;
using UnityEngine;

namespace ImmersiveNPCs
{
    public static class PathUtility
    {
        public static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }
    }
}
