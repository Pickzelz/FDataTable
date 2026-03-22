using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FDataTable.Editor
{
    /// <summary>
    /// Popup window to configure per-type settings (folder, prefix, included/excluded fields).
    /// </summary>
    public class FDataTableSettingsWindow : EditorWindow
    {
        private FDataTableSettings    _settings;
        private Type                  _type;
        private Vector2               _scroll;

        public static void Open(FDataTableSettings settings, Type type)
        {
            var win = GetWindow<FDataTableSettingsWindow>(true, "FDataTable Settings", true);
            win.minSize = new Vector2(340f, 200f);
            win.maxSize = new Vector2(500f, 300f);
            win._settings = settings;
            win._type     = type;
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These are global defaults applied when opening any new tab.\n" +
                "You can override them per-tab in the folder bar below the tab bar.",
                MessageType.Info);
            EditorGUILayout.Space(4f);

            var settings = _settings;
            EditorGUILayout.LabelField("Default Folder", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newFolder = EditorGUILayout.TextField(settings.globalDefaultFolder);
            if (EditorGUI.EndChangeCheck())
            {
                settings.globalDefaultFolder = newFolder;
                EditorUtility.SetDirty(settings);
            }
            if (GUILayout.Button("Browse", GUILayout.Width(64f)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Select Folder", settings.globalDefaultFolder, "");
                if (!string.IsNullOrEmpty(chosen))
                {
                    if (chosen.StartsWith(Application.dataPath))
                        chosen = "Assets" + chosen.Substring(Application.dataPath.Length);
                    settings.globalDefaultFolder = chosen;
                    EditorUtility.SetDirty(settings);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Default Prefix", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            string newPrefix = EditorGUILayout.TextField(settings.globalDefaultPrefix);
            if (EditorGUI.EndChangeCheck())
            {
                settings.globalDefaultPrefix = newPrefix;
                EditorUtility.SetDirty(settings);
            }

            EditorGUILayout.Space(10f);

            if (GUILayout.Button("Close"))
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                Close();
            }
        }
    }
}
