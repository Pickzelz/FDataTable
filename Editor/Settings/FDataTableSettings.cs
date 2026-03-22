using System;
using System.Collections.Generic;
using UnityEngine;

namespace FDataTable.Editor
{
    /// <summary>
    /// Settings for one SO type tracked by FDataTable.
    /// </summary>
    [Serializable]
    public class FDataTableTypeConfig
    {
        [Tooltip("The fully qualified type name of the ScriptableObject.")]
        public string typeName;

        [Tooltip("Folder where new assets will be created (relative to Assets/).")]
        public string targetFolder = "Assets/Data";

        [Tooltip("Prefix used when naming new asset files.")]
        public string filePrefix = "New";

        [Tooltip("Fields to show. Leave empty to show all supported fields.")]
        public List<string> includedFields = new List<string>();

        [Tooltip("Fields to always hide.")]
        public List<string> excludedFields = new List<string>();
    }

    /// <summary>
    /// Project-wide settings ScriptableObject for FDataTable.
    /// Stored at Assets/FDataTable/Editor/Settings/FDataTableSettings.asset
    /// </summary>
    public class FDataTableSettings : ScriptableObject
    {
        private const string AssetPath = "Assets/FDataTable/Editor/Settings/FDataTableSettings.asset";

        [Tooltip("Default folder where new assets will be created when opening a new tab.")]
        public string globalDefaultFolder = "Assets/Data";

        [Tooltip("Default prefix used when naming new asset files.")]
        public string globalDefaultPrefix = "New";

        [SerializeField] private List<FDataTableTypeConfig> _typeConfigs = new List<FDataTableTypeConfig>();

        public List<FDataTableTypeConfig> TypeConfigs => _typeConfigs;

        public static FDataTableSettings GetOrCreate()
        {
#if UNITY_EDITOR
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<FDataTableSettings>(AssetPath);
            if (settings == null)
            {
                settings = CreateInstance<FDataTableSettings>();
                UnityEditor.AssetDatabase.CreateAsset(settings, AssetPath);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            return settings;
#else
            return null;
#endif
        }

        public FDataTableTypeConfig GetConfig(Type type)
        {
            var name = type.AssemblyQualifiedName;
            return _typeConfigs.Find(c => c.typeName == name);
        }

        public FDataTableTypeConfig GetOrAddConfig(Type type)
        {
            var config = GetConfig(type);
            if (config == null)
            {
                config = new FDataTableTypeConfig
                {
                    typeName = type.AssemblyQualifiedName,
                    targetFolder = "Assets/Data",
                    filePrefix = type.Name
                };
                _typeConfigs.Add(config);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
            return config;
        }
    }
}
