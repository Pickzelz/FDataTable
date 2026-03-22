using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FDataTable.Editor
{
    /// <summary>
    /// Scans an asset folder for ScriptableObject assets of a given type.
    /// </summary>
    public static class FDataTableScanner
    {
        /// <summary>
        /// Returns all ScriptableObject assets of the given type found anywhere in the project.
        /// </summary>
        public static List<ScriptableObject> FindAll(Type type)
        {
            var result = new List<ScriptableObject>();
            string typeName = type.Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeName}");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
                if (asset != null)
                    result.Add(asset);
            }

            return result;
        }

        /// <summary>
        /// Returns all ScriptableObject assets of the given type found in the specified folder.
        /// </summary>
        public static List<ScriptableObject> FindInFolder(Type type, string folder)
        {
            var result = new List<ScriptableObject>();
            string typeName = type.Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeName}", new[] { folder });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
                if (asset != null)
                    result.Add(asset);
            }

            return result;
        }
    }
}
