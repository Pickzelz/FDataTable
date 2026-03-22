using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FDataTable.Editor
{
    /// <summary>
    /// Creates new ScriptableObject assets on disk.
    /// </summary>
    public static class FDataTableAssetFactory
    {
        /// <summary>
        /// Creates a new ScriptableObject asset of the given type inside the configured folder.
        /// Returns the new instance.
        /// </summary>
        public static ScriptableObject Create(Type type, FDataTableTypeConfig config)
        {
            string folder = config?.targetFolder ?? "Assets/Data";
            string prefix = config?.filePrefix ?? type.Name;

            // Ensure folder exists
            if (!AssetDatabase.IsValidFolder(folder))
                CreateFolderRecursive(folder);

            string baseName = $"{prefix}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}.asset");

            var instance = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(instance, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return instance;
        }

        /// <summary>
        /// Deletes and unregisters the given ScriptableObject asset from disk.
        /// </summary>
        public static void Delete(ScriptableObject asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
            }
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            // Strip leading slash if present
            folderPath = folderPath.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string child = Path.GetFileName(folderPath);

            if (!string.IsNullOrEmpty(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
