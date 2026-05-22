#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;

namespace ImmersiveNPCs.Editor
{
    [InitializeOnLoad]
    internal static class DialogueBackendDefineSymbols
    {
        private const string Symbol = "IMMERSIVE_NPCS_YARN";
        private static bool isUpdating;
        private static readonly Dictionary<string, bool> namespaceCache = new Dictionary<string, bool>();

        static DialogueBackendDefineSymbols()
        {
            AssemblyReloadEvents.afterAssemblyReload += OnDomainReload;
            EditorApplication.projectChanged += QueueUpdate;
            QueueUpdate();
        }

        private static void OnDomainReload()
        {
            namespaceCache.Clear();
            QueueUpdate();
        }

        private static void QueueUpdate()
        {
            if (isUpdating) return;
            isUpdating = true;
            EditorApplication.delayCall += UpdateDefineSymbols;
        }

        private static void UpdateDefineSymbols()
        {
            try
            {
                bool backendPresent = IsNamespacePresent("Yarn.Unity");

                var group = EditorUserBuildSettings.selectedBuildTargetGroup;
                NamedBuildTarget nbt = NamedBuildTarget.FromBuildTargetGroup(group);
                string current = PlayerSettings.GetScriptingDefineSymbols(nbt);
                var list = current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                if (backendPresent)
                {
                    if (!list.Contains(Symbol)) list.Add(Symbol);
                }
                else
                {
                    list.RemoveAll(s => s == Symbol);
                }

                string updated = string.Join(";", list);
                if (!string.Equals(updated, current, StringComparison.Ordinal))
                {
                    PlayerSettings.SetScriptingDefineSymbols(nbt, updated);
                }
            }
            finally
            {
                isUpdating = false;
            }
        }

        private static bool IsNamespacePresent(string namespaceName)
        {
            if (namespaceCache.TryGetValue(namespaceName, out bool present))
            {
                return present;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || asm.ReflectionOnly) continue;
                try
                {
                    if (asm.GetTypes().Any(t => t.Namespace == namespaceName))
                    {
                        namespaceCache[namespaceName] = true;
                        return true;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
            }

            namespaceCache[namespaceName] = false;
            return false;
        }
    }
}
#endif
