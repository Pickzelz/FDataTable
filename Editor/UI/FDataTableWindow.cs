using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FDataTable.Editor
{
    /// <summary>
    /// Main FDataTable Editor Window.
    /// Displays ScriptableObject assets of a chosen type in a spreadsheet-like table.
    /// Menu: Window/FDataTable/Data Table
    /// </summary>
    public class FDataTableWindow : EditorWindow
    {
        // ── Layout constants ────────────────────────────────────────────────
        private const float RowHeight       = 22f;
        private const float LineHeight       = 17f;
        private const float CellPadding      = 4f;
        private const float HeaderHeight     = 24f;
        private const float RowNumWidth      = 40f;
        private const float NameColWidth     = 160f;   // fixed-width file name column
        private const float DefaultColWidth  = 140f;
        private const float MinColWidth      = 60f;
        private const float ToolbarHeight    = 26f;
        private const int   CollapseThreshold = 6;

        // ── State ───────────────────────────────────────────────────────────
        private Type                      _selectedType;       // for type picker popup
        private List<Type>                _soTypes           = new List<Type>();
        private List<string>              _soTypeNames       = new List<string>();

        // ── Tab state ────────────────────────────────────────────────────────
        private List<FDataTableTab>       _tabs              = new List<FDataTableTab>();
        private int                       _activeTabIndex    = -1;
        private FDataTableTab             ActiveTab          => (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                                                                  ? _tabs[_activeTabIndex] : null;

        // Convenience shorthands that delegate to active tab
        private List<ScriptableObject>    Rows               => ActiveTab?.Rows;
        private List<FieldInfo>           Columns            => ActiveTab?.Columns;
        private List<float>               ColumnWidths       => ActiveTab?.ColumnWidths;

        private FDataTableSettings        _settings;

        private const string GlobalFilterControlName = "FDataTable.GlobalFilter";

        // Column resize (not per-tab — only one being resized at a time)
        private int                       _resizingCol       = -1;
        private float                     _resizeStartX;
        private float                     _resizeStartWidth;



        // ── Menu entry ──────────────────────────────────────────────────────
        [MenuItem("Window/FDataTable/Data Table")]
        public static void Open()
        {
            var window = GetWindow<FDataTableWindow>("FDataTable");
            window.minSize = new Vector2(600f, 400f);
            window.Show();
        }

        // ── Persistence keys ─────────────────────────────────────────────────
        private const string PrefKeyTabList   = "FDataTable.TabList";     // comma-separated type names
        private const string PrefKeyActiveTab = "FDataTable.ActiveTab";
        // legacy key kept for migration:
        private const string PrefKeyTypeName  = "FDataTable.SelectedTypeName";
        // Per-tab folder/prefix: "FDataTable.Tab.[TypeName].Folder" etc.
        private static string PrefKeyTabFolder(string typeName)    => $"FDataTable.Tab.{typeName}.Folder";
        private static string PrefKeyTabPrefix(string typeName)     => $"FDataTable.Tab.{typeName}.Prefix";
        // Per-tab row order: comma-separated GUIDs in user's display order
        private static string PrefKeyTabRowOrder(string typeName)   => $"FDataTable.Tab.{typeName}.RowOrder";

        private void OnEnable()
        {
            _settings = FDataTableSettings.GetOrCreate();
            RefreshTypeList();

            // Always start clean — after recompile Unity may have partially serialized
            // _tabs with null Types, causing duplicates if we append to the existing list.
            _tabs.Clear();
            _activeTabIndex = -1;

            // Restore tabs from EditorPrefs
            string tabList = EditorPrefs.GetString(PrefKeyTabList,
                EditorPrefs.GetString(PrefKeyTypeName, string.Empty));
            if (!string.IsNullOrEmpty(tabList))
            {
                foreach (var typeName in tabList.Split(','))
                {
                    if (string.IsNullOrEmpty(typeName)) continue;
                    int idx = _soTypeNames.IndexOf(typeName);
                    if (idx >= 0)
                    {
                        OpenTab(_soTypes[idx]);
                        // Restore per-tab folder/prefix saved from previous session
                        var restoredTab = _tabs[_tabs.Count - 1];
                        string savedFolder = EditorPrefs.GetString(PrefKeyTabFolder(typeName), null);
                        string savedPrefix = EditorPrefs.GetString(PrefKeyTabPrefix(typeName), null);
                        if (!string.IsNullOrEmpty(savedFolder)) restoredTab.TabFolder = savedFolder;
                        if (!string.IsNullOrEmpty(savedPrefix)) restoredTab.TabPrefix = savedPrefix;
                    }
                }
            }

            string activeTypeName = EditorPrefs.GetString(PrefKeyActiveTab, string.Empty);
            int activeIdx = _tabs.FindIndex(t => t.TypeName == activeTypeName);
            _activeTabIndex = activeIdx >= 0 ? activeIdx : (_tabs.Count > 0 ? 0 : -1);
        }

        internal void SaveTabPrefs()
        {
            EditorPrefs.SetString(PrefKeyTabList,
                string.Join(",", _tabs.ConvertAll(t => t.TypeName)));
            EditorPrefs.SetString(PrefKeyActiveTab,
                ActiveTab?.TypeName ?? string.Empty);

            // Save per-tab folder/prefix
            foreach (var tab in _tabs)
            {
                if (!string.IsNullOrEmpty(tab.TabFolder))
                    EditorPrefs.SetString(PrefKeyTabFolder(tab.TypeName), tab.TabFolder);
                if (!string.IsNullOrEmpty(tab.TabPrefix))
                    EditorPrefs.SetString(PrefKeyTabPrefix(tab.TypeName), tab.TabPrefix);
            }
        }

        private void OnGUI()
        {
            // Ctrl+F focuses the global search field
            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.F
                && Event.current.modifiers == EventModifiers.Control)
            {
                GUI.FocusControl(GlobalFilterControlName);
                Event.current.Use();
            }

            DrawToolbar();
            DrawTabBar();

            var tab = ActiveTab;
            if (tab == null)
            {
                EditorGUILayout.HelpBox("Click + in the tab bar or use the toolbar dropdown to open a ScriptableObject type as a tab.", MessageType.Info);
                return;
            }

            // Sync all cached SerializedObjects with the underlying asset data
            foreach (var kvp in tab.SerializedCache)
                kvp.Value.Update();

            DrawTable();
        }

        // ── Toolbar ─────────────────────────────────────────────────────────
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight));

            // Refresh
            if (GUILayout.Button("↺ Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                Reload();

            GUILayout.Space(6f);

            // Global search — filters visible rows across all columns
            var activeTab = ActiveTab;
            if (activeTab != null)
            {
                GUI.SetNextControlName(GlobalFilterControlName);
                EditorGUI.BeginChangeCheck();
                activeTab.GlobalFilter = GUILayout.TextField(
                    activeTab.GlobalFilter, EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(100f), GUILayout.MaxWidth(240f));
                if (EditorGUI.EndChangeCheck())
                {
                    RebuildFilteredIndices(activeTab);
                    Repaint();
                }

                bool anyFilter = !string.IsNullOrEmpty(activeTab.GlobalFilter)
                              || !string.IsNullOrEmpty(activeTab.NameFilter)
                              || activeTab.ColFilters.Exists(f => !string.IsNullOrEmpty(f));
                if (anyFilter)
                    GUILayout.Label($"{activeTab.FilteredIndices.Count}/{activeTab.Rows.Count}",
                        EditorStyles.toolbarButton, GUILayout.Width(46f));
            }

            GUILayout.FlexibleSpace();

            // Settings
            if (GUILayout.Button("⚙ Settings", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                FDataTableSettingsWindow.Open(_settings, ActiveTab?.Type);

            EditorGUILayout.EndHorizontal();
        }

        // ── Tab Bar ──────────────────────────────────────────────────────────
        private const float TabHeight  = 24f;
        private const float TabMinW    = 80f;
        private const float TabMaxW    = 160f;
        private const float TabCloseW  = 16f;
        private const float TabAddW    = 26f;

        private void DrawTabBar()
        {
            Rect barRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(TabHeight));

            // Bar background
            if (Event.current.type == EventType.Repaint)
                Styles.TabBarBackground.Draw(barRect, false, false, false, false);

            for (int i = 0; i < _tabs.Count; i++)
            {
                var t    = _tabs[i];
                bool active = i == _activeTabIndex;
                string label = t.TypeName;
                float  tw    = Mathf.Clamp(
                    EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + TabCloseW + 12f,
                    TabMinW, TabMaxW);

                GUIStyle style = active ? Styles.TabActive : Styles.TabInactive;
                Rect tabRect   = GUILayoutUtility.GetRect(tw, TabHeight, GUILayout.Width(tw));

                // Tab background
                if (Event.current.type == EventType.Repaint)
                    style.Draw(tabRect, false, false, false, false);

                // Close button — must be handled BEFORE the tab-select click
                // so its rect can be excluded from the selection handler.
                Rect closeRect = new Rect(tabRect.xMax - TabCloseW - 2f,
                    tabRect.y + (TabHeight - 14f) * 0.5f, 14f, 14f);
                if (GUI.Button(closeRect, "✕", Styles.TabClose))
                {
                    bool confirm = EditorUtility.DisplayDialog(
                        "Close Tab",
                        $"Close the tab \"{t.TypeName}\"? (No assets will be deleted.)",
                        "Close", "Cancel");
                    if (confirm)
                    {
                        _tabs.RemoveAt(i);
                        if (_activeTabIndex >= _tabs.Count)
                            _activeTabIndex = _tabs.Count - 1;
                        SaveTabPrefs();
                        Repaint();
                        break; // list modified — stop iteration
                    }
                }

                // Label (click to switch) — exclude the close-button area
                Rect labelRect = new Rect(tabRect.x + 4f, tabRect.y + 3f,
                    tabRect.width - TabCloseW - 6f, TabHeight - 4f);
                GUI.Label(labelRect, label, active ? Styles.TabLabelActive : Styles.TabLabelInactive);
                if (Event.current.type == EventType.MouseDown
                    && tabRect.Contains(Event.current.mousePosition)
                    && !closeRect.Contains(Event.current.mousePosition))
                {
                    _activeTabIndex = i;
                    SaveTabPrefs();
                    Repaint();
                    Event.current.Use();
                }
            }

            // Excel-style "+" button to add a new tab
            if (GUILayout.Button("+", Styles.TabAdd, GUILayout.Width(TabAddW), GUILayout.Height(TabHeight)))
            {
                Rect btnRect = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(btnRect, new FDataTableTypePickerPopup(this, _soTypes, _soTypeNames));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Separator line
            Rect sep = EditorGUILayout.GetControlRect(false, 1f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(sep, new Color(0.1f, 0.1f, 0.1f));

            DrawTabSettings();
        }

        // ── Per-Tab Settings Bar ────────────────────────────────────────────
        private const float TabSettingsH = 22f;

        private void DrawTabSettings()
        {
            var tab = ActiveTab; if (tab == null) return;

            Rect barRect = EditorGUILayout.BeginHorizontal(
                Styles.TabSettingsBar, GUILayout.Height(TabSettingsH));

            // Folder icon + label
            GUILayout.Label("📂", GUILayout.Width(18f));
            GUILayout.Label("Folder:", EditorStyles.miniLabel, GUILayout.Width(44f));

            // Folder path — editable text field (per-tab, does NOT touch global config)
            EditorGUI.BeginChangeCheck();
            string newFolder = EditorGUILayout.TextField(tab.TabFolder,
                EditorStyles.miniTextField, GUILayout.MinWidth(80f), GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                tab.TabFolder = newFolder;
                SaveTabPrefs();
            }

            // Browse button
            if (GUILayout.Button("…", EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Select Folder", tab.TabFolder, "");
                if (!string.IsNullOrEmpty(chosen))
                {
                    if (chosen.StartsWith(Application.dataPath))
                        chosen = "Assets" + chosen.Substring(Application.dataPath.Length);
                    tab.TabFolder = chosen;
                    SaveTabPrefs();
                    GUI.FocusControl(null);
                }
            }

            GUILayout.Space(12f);

            // Prefix label + field
            GUILayout.Label("Prefix:", EditorStyles.miniLabel, GUILayout.Width(40f));
            EditorGUI.BeginChangeCheck();
            string newPrefix = EditorGUILayout.TextField(tab.TabPrefix,
                EditorStyles.miniTextField, GUILayout.Width(100f));
            if (EditorGUI.EndChangeCheck())
            {
                tab.TabPrefix = newPrefix;
                SaveTabPrefs();
            }

            GUILayout.Space(8f);

            // Field visibility popup
            Rect fieldsBtn = GUILayoutUtility.GetRect(
                new GUIContent("Fields…"), EditorStyles.miniButton, GUILayout.Width(54f));
            if (GUI.Button(fieldsBtn, "Fields…", EditorStyles.miniButton))
            {
                var popup = new FDataTableFieldsPopup(_settings, tab.Type, () =>
                {
                    tab.Columns = FDataTableFieldResolver.GetDisplayableFields(tab.Type, _settings.GetOrAddConfig(tab.Type));
                    int colDiff = tab.Columns.Count - tab.ColumnWidths.Count;
                    if (colDiff > 0)
                        for (int i = 0; i < colDiff; i++) tab.ColumnWidths.Add(DefaultColWidth);
                    else if (colDiff < 0)
                        tab.ColumnWidths.RemoveRange(tab.Columns.Count, -colDiff);
                    EnsureFilterState(tab);
                    RebuildFilteredIndices(tab);
                    RecomputeAllRowHeights();
                    Repaint();
                });
                PopupWindow.Show(fieldsBtn, popup);
            }

            EditorGUILayout.EndHorizontal();

            // Bottom separator
            Rect sep2 = EditorGUILayout.GetControlRect(false, 1f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(sep2, new Color(0.1f, 0.1f, 0.1f));
        }

        // ── Table ────────────────────────────────────────────────────────────
        private void DrawTable()
        {
            var tab = ActiveTab;
            EnsureFilterState(tab);

            // Compute total content width so the sticky header can span all columns.
            float totalW = RowNumWidth + NameColWidth;
            foreach (var cw in tab.ColumnWidths) totalW += cw;

            float stickyH = HeaderHeight + FilterRowHeight;

            // ── Sticky header & filter row ────────────────────────────────────
            // Allocate screen space for the rows, then offset them horizontally
            // using GUI.BeginClip so they stay in sync with the data scroll view.
            Rect stickyArea = EditorGUILayout.GetControlRect(false, stickyH,
                GUILayout.ExpandWidth(true));
            GUI.BeginClip(stickyArea);
            GUILayout.BeginArea(new Rect(-tab.ScrollPos.x, 0f, totalW, stickyH));
            DrawTableHeader();
            DrawFilterRow();
            GUILayout.EndArea();
            GUI.EndClip();

            // ── Scrollable data rows ──────────────────────────────────────────
            tab.ScrollPos = GUILayout.BeginScrollView(tab.ScrollPos);
            foreach (int r in tab.FilteredIndices)
                DrawRow(r);
            DrawGhostRow();
            GUILayout.EndScrollView();
        }

        private void DrawGhostRow()
        {
            var tab = ActiveTab; if (tab == null) return;
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowHeight));

            if (Event.current.type == EventType.Repaint)
                Styles.GhostRow.Draw(rowRect, false, false, false, false);

            GUILayout.Box("+", Styles.RowNumCell, GUILayout.Width(RowNumWidth), GUILayout.Height(RowHeight));
            GUILayout.Label($"  Click to add new {tab.TypeName}…", Styles.GhostLabel);

            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown
                && rowRect.Contains(Event.current.mousePosition))
            {
                AddRow();
                Event.current.Use();
            }
        }

        private void DrawTableHeader()
        {
            var tab = ActiveTab; if (tab == null) return;
            Rect headerBar = EditorGUILayout.BeginHorizontal(GUILayout.Height(HeaderHeight));
            GUI.Box(headerBar, GUIContent.none, Styles.HeaderBackground);
            GUILayout.Box("#", Styles.HeaderCell, GUILayout.Width(RowNumWidth), GUILayout.Height(HeaderHeight));

            // Fixed "Name" column header
            Rect nameHeaderRect = GUILayoutUtility.GetRect(NameColWidth, HeaderHeight, GUILayout.Width(NameColWidth));
            GUI.Box(nameHeaderRect, GUIContent.none, Styles.HeaderBackground);
            GUI.Label(new Rect(nameHeaderRect.x + 4f, nameHeaderRect.y + 3f, nameHeaderRect.width - 8f, nameHeaderRect.height),
                "Name (file)", Styles.HeaderLabel);

            for (int c = 0; c < tab.Columns.Count; c++)
            {
                float w = tab.ColumnWidths[c];
                Rect cellRect = GUILayoutUtility.GetRect(w, HeaderHeight, GUILayout.Width(w));
                GUI.Box(cellRect, GUIContent.none, Styles.HeaderBackground);
                GUI.Label(new Rect(cellRect.x + 4f, cellRect.y + 3f, cellRect.width - 10f, cellRect.height),
                    ObjectNames.NicifyVariableName(tab.Columns[c].Name), Styles.HeaderLabel);
                Rect resizeHandle = new Rect(cellRect.xMax - 4f, cellRect.y, 8f, cellRect.height);
                EditorGUIUtility.AddCursorRect(resizeHandle, MouseCursor.ResizeHorizontal);
                HandleColumnResize(c, resizeHandle);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRow(int rowIndex)
        {
            var tab = ActiveTab; if (tab == null) return;
            ScriptableObject so = tab.Rows[rowIndex];
            bool isSelected = rowIndex == tab.SelectedRow;

            float rowH = rowIndex < tab.RowHeights.Count ? tab.RowHeights[rowIndex] : RowHeight;
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(rowH));

            // Row background
            if (Event.current.type == EventType.Repaint)
            {
                GUIStyle bg = isSelected ? Styles.RowSelected
                    : (rowIndex % 2 == 0 ? Styles.RowEven : Styles.RowOdd);
                bg.Draw(rowRect, false, false, false, false);
            }

            // Left-click to select row
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && rowRect.Contains(Event.current.mousePosition))
            {
                tab.SelectedRow = rowIndex;
                GUI.changed = true;
                Repaint();
            }

            // Right-click context menu
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1
                && rowRect.Contains(Event.current.mousePosition))
            {
                tab.SelectedRow = rowIndex;
                int capturedIndex = rowIndex;
                ScriptableObject capturedSo = so;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Remove Row…"), false, () => DeleteRow(capturedIndex));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Ping in Project"), false,
                    () => EditorGUIUtility.PingObject(capturedSo));
                menu.AddItem(new GUIContent("Open in Inspector"), false,
                    () => Selection.activeObject = capturedSo);
                menu.ShowAsContext();
                Event.current.Use();
            }

            // Row number (double-click to ping)
            GUILayout.Box((rowIndex + 1).ToString(), Styles.RowNumCell,
                GUILayout.Width(RowNumWidth), GUILayout.Height(rowH));

            Rect numRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2
                && numRect.Contains(Event.current.mousePosition))
                EditorGUIUtility.PingObject(so);

            // Cells
            if (so != null)
            {
                if (!tab.SerializedCache.TryGetValue(so, out SerializedObject serialized))
                {
                    serialized = new SerializedObject(so);
                    tab.SerializedCache[so] = serialized;
                }

                // Name column — always first, lets user rename the asset file
                DrawNameCell(so, rowIndex, rowH, isSelected);

                for (int c = 0; c < tab.Columns.Count; c++)
                    DrawCell(serialized, rowIndex, c, rowH);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static readonly Color SelectedCellBg    = new Color(0.17f, 0.36f, 0.53f, 1f);
        private static readonly Color SelectedCellTint   = new Color(0.55f, 0.75f, 1.00f, 1f);
        private static readonly Color SelectedCellOverlay= new Color(0.25f, 0.50f, 0.80f, 0.18f);

        private void DrawNameCell(ScriptableObject so, int rowIndex, float rowH, bool isSelected)
        {
            Rect cellRect = GUILayoutUtility.GetRect(NameColWidth, rowH, GUILayout.Width(NameColWidth));

            // Background
            if (Event.current.type == EventType.Repaint)
            {
                if (isSelected)
                    EditorGUI.DrawRect(cellRect, SelectedCellBg);
                else
                    Styles.NameCellBg.Draw(cellRect, false, false, false, false);
            }

            Rect fieldRect = new Rect(cellRect.x + 4f, cellRect.y + 2f, cellRect.width - 8f, RowHeight - 4f);
            string controlName = $"NameCell_{rowIndex}";
            GUI.SetNextControlName(controlName);

            Color prevBg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = SelectedCellTint;

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUI.DelayedTextField(fieldRect, so.name, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(newName) && newName != so.name)
            {
                // Sanitize: remove characters invalid in file names
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                    newName = newName.Replace(c.ToString(), string.Empty);
                newName = newName.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    string assetPath = AssetDatabase.GetAssetPath(so);
                    AssetDatabase.RenameAsset(assetPath, newName);
                    AssetDatabase.SaveAssets();
                }
            }

            GUI.backgroundColor = prevBg;
        }

        private void DrawCell(SerializedObject so, int rowIndex, int colIndex, float rowH)
        {
            var tab = ActiveTab; if (tab == null) return;
            float w = tab.ColumnWidths[colIndex];
            FieldInfo field = tab.Columns[colIndex];
            bool isSelected = tab.SelectedRow == rowIndex;

            SerializedProperty prop = so.FindProperty(field.Name);
            Rect cellRect = GUILayoutUtility.GetRect(w, rowH, GUILayout.Width(w));

            // Cell background — selection color or normal border
            if (Event.current.type == EventType.Repaint)
            {
                if (isSelected)
                    EditorGUI.DrawRect(cellRect, SelectedCellBg);
                else
                    Styles.CellBorder.Draw(cellRect, false, false, false, false);
            }

            // Tint EditorGUI controls to match selection
            Color prevBg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = SelectedCellTint;

            if (prop == null)
            {
                GUI.Label(new Rect(cellRect.x + 2f, cellRect.y + 1f, cellRect.width - 4f, cellRect.height - 2f),
                    field.GetValue(so.targetObject)?.ToString() ?? "null", EditorStyles.miniLabel);
                GUI.backgroundColor = prevBg;
                return;
            }

            // Complex types → vertical multi-line display + Edit button
            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                GUI.backgroundColor = prevBg;
                DrawGenericCell(cellRect, so, prop, rowIndex, colIndex, isSelected);
                return;
            }

            // Simple types — draw directly without decorator drawers
            Rect fieldRect = new Rect(cellRect.x + 2f, cellRect.y + 1f, cellRect.width - 4f, RowHeight - 2f);
            EditorGUI.BeginChangeCheck();
            DrawPropertyDirect(fieldRect, prop, field.FieldType);
            if (EditorGUI.EndChangeCheck())
                so.ApplyModifiedProperties();

            // Semi-transparent overlay so selection is visible even over control backgrounds
            if (isSelected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(fieldRect, SelectedCellOverlay);

            GUI.backgroundColor = prevBg;
        }

        private void DrawGenericCell(Rect cellRect, SerializedObject so, SerializedProperty prop, int rowIndex, int colIndex, bool isSelected)
        {
            if (isSelected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(cellRect, SelectedCellBg);

            if (prop.isArray)
                DrawInlineArray(cellRect, so, prop, rowIndex, colIndex);
            else
                DrawInlineStruct(cellRect, so, prop);
        }

        // Minimum content widths per property type for inline sub-columns
        private static float SubColMinWidth(SerializedPropertyType t) => t switch
        {
            SerializedPropertyType.String          => 130f,
            SerializedPropertyType.ObjectReference => 150f,
            SerializedPropertyType.Generic         => 200f,
            SerializedPropertyType.Boolean         => 40f,
            SerializedPropertyType.Color           => 70f,
            SerializedPropertyType.Vector2         => 130f,
            SerializedPropertyType.Vector3         => 180f,
            _                                      => 70f,
        };

        // Inline array: mini header + editable rows + add button, all within cellRect
        private void DrawInlineArray(Rect cr, SerializedObject so, SerializedProperty prop, int rowIndex = -1, int colIndex = -1)
        {
            const float idxW = 22f;
            const float delW = 18f;
            const float px   = 2f;

            // Build sub-columns with per-type minimum widths
            var subCols   = new List<string>();
            var colWidths = new List<float>();
            if (prop.arraySize > 0)
            {
                var e0 = prop.GetArrayElementAtIndex(0);
                if (e0.propertyType == SerializedPropertyType.Generic && !e0.isArray)
                {
                    var sc = e0.Copy(); var se = e0.GetEndProperty();
                    if (sc.NextVisible(true))
                        while (!SerializedProperty.EqualContents(sc, se))
                        {
                            subCols.Add(sc.name);
                            colWidths.Add(SubColMinWidth(sc.propertyType));
                            sc.NextVisible(false);
                        }
                }
            }

            // Natural content width required to show all sub-columns without clipping
            float naturalW = idxW + delW;
            foreach (var w in colWidths) naturalW += w;

            // Auto-expand the parent column so content is never cut off
            var tab = ActiveTab;
            if (colIndex >= 0 && tab != null && colIndex < tab.ColumnWidths.Count)
            {
                float needed = naturalW + px * 2f;
                if (tab.ColumnWidths[colIndex] < needed - 0.5f)
                {
                    tab.ColumnWidths[colIndex] = needed;
                    Repaint();
                }
            }

            float vw       = cr.width - px * 2f;
            float contentW = Mathf.Max(vw, naturalW);
            float ox       = cr.x + px;
            float rowY     = cr.y + CellPadding;
            float availDataW = contentW - idxW - delW;

            // ── Header ─────────────────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(ox, rowY, contentW, LineHeight), new Color(0.22f, 0.22f, 0.22f));
            GUI.Label(new Rect(ox + 1f, rowY + 1f, idxW - 2f, LineHeight - 2f),
                "#", EditorStyles.centeredGreyMiniLabel);
            float hx = ox + idxW;
            for (int ci = 0; ci < subCols.Count; ci++)
            {
                GUI.Label(new Rect(hx + 2f, rowY + 1f, colWidths[ci] - 4f, LineHeight - 2f),
                    ObjectNames.NicifyVariableName(subCols[ci]), EditorStyles.miniLabel);
                hx += colWidths[ci];
            }
            if (subCols.Count == 0)
                GUI.Label(new Rect(ox + idxW + 2f, rowY + 1f, availDataW - 4f, LineHeight - 2f),
                    "Value", EditorStyles.miniLabel);
            rowY += LineHeight;

            // ── Rows ───────────────────────────────────────────────────────────
            int delIdx = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var   elem  = prop.GetArrayElementAtIndex(i);
                float elemH = GetElemRowHeight(elem, subCols, colWidths);

                EditorGUI.DrawRect(new Rect(ox, rowY, contentW, elemH),
                    i % 2 == 0 ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.20f, 0.20f, 0.20f));

                GUI.Label(new Rect(ox + 1f, rowY + 1f, idxW - 2f, LineHeight - 2f),
                    i.ToString(), EditorStyles.centeredGreyMiniLabel);

                if (subCols.Count > 0)
                {
                    float ex = ox + idxW;
                    for (int ci = 0; ci < subCols.Count; ci++)
                    {
                        var cp = elem.FindPropertyRelative(subCols[ci]);
                        if (cp != null)
                        {
                            Rect cellR = new Rect(ex + 1f, rowY + 1f, colWidths[ci] - 2f, elemH - 2f);
                            if (cp.propertyType == SerializedPropertyType.Generic && cp.isArray)
                                DrawNestedArrayCell(cellR, cp, so);
                            else if (cp.propertyType == SerializedPropertyType.Generic)
                                DrawNestedStructCell(cellR, cp, so);
                            else
                                DrawPrimitiveInline(
                                    new Rect(ex + 1f, rowY + (elemH - LineHeight) * 0.5f, colWidths[ci] - 2f, LineHeight - 2f),
                                    cp, so);
                        }
                        ex += colWidths[ci];
                    }
                }
                else
                    DrawPrimitiveInline(
                        new Rect(ox + idxW + 1f, rowY + (elemH - LineHeight) * 0.5f, availDataW - 2f, LineHeight - 2f),
                        elem, so);

                if (GUI.Button(new Rect(ox + contentW - delW, rowY + 1f, delW - 1f, LineHeight - 2f),
                    "−", EditorStyles.miniButton))
                    delIdx = i;

                rowY += elemH;
            }

            if (delIdx >= 0)
            {
                prop.DeleteArrayElementAtIndex(delIdx);
                so.ApplyModifiedProperties();
                RecomputeAllRowHeights(); Repaint();
            }

            if (GUI.Button(new Rect(ox + 1f, rowY + 1f, contentW - 2f, LineHeight - 2f),
                "+ Add", EditorStyles.miniButton))
            {
                prop.arraySize++;
                so.ApplyModifiedProperties();
                RecomputeAllRowHeights(); Repaint();
            }
        }

        // Height for one element row inside DrawInlineArray (takes the tallest column)
        private float GetElemRowHeight(SerializedProperty elem, List<string> cols, List<float> _)
        {
            float h = LineHeight + 4f;
            for (int ci = 0; ci < cols.Count; ci++)
            {
                var cp = elem.FindPropertyRelative(cols[ci]);
                if (cp == null || cp.propertyType != SerializedPropertyType.Generic) continue;
                float ch = cp.isArray
                    ? (1 + cp.arraySize + 1) * LineHeight + 4f   // header + rows + add
                    : CountDirectChildren(cp) * LineHeight + 4f;   // struct rows
                h = Mathf.Max(h, ch);
            }
            return h;
        }

        // Draws a nested array inline within a cell (no outer add/delete — all self-contained)
        private void DrawNestedArrayCell(Rect r, SerializedProperty arr, SerializedObject so)
        {
            const float delW = 16f;
            // sub-sub-columns
            var subCols = new List<string>();
            if (arr.arraySize > 0)
            {
                var e0 = arr.GetArrayElementAtIndex(0);
                if (e0.propertyType == SerializedPropertyType.Generic && !e0.isArray)
                {
                    var sc = e0.Copy(); var se = e0.GetEndProperty();
                    if (sc.NextVisible(true))
                        while (!SerializedProperty.EqualContents(sc, se))
                        { subCols.Add(sc.name); sc.NextVisible(false); }
                }
            }
            float colW = subCols.Count > 0 ? (r.width - delW) / subCols.Count : r.width - delW;
            float y = r.y;

            // Header
            EditorGUI.DrawRect(new Rect(r.x, y, r.width, LineHeight), new Color(0.26f, 0.26f, 0.30f));
            if (subCols.Count > 0)
            {
                float hx = r.x;
                foreach (var col in subCols)
                {
                    GUI.Label(new Rect(hx + 2f, y + 1f, colW - 4f, LineHeight - 2f),
                        ObjectNames.NicifyVariableName(col), EditorStyles.miniLabel);
                    hx += colW;
                }
            }
            else
                GUI.Label(new Rect(r.x + 2f, y + 1f, r.width - delW - 4f, LineHeight - 2f),
                    "Value", EditorStyles.miniLabel);
            y += LineHeight;

            int delIdx = -1;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var elem = arr.GetArrayElementAtIndex(i);
                EditorGUI.DrawRect(new Rect(r.x, y, r.width, LineHeight),
                    i % 2 == 0 ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.23f, 0.23f, 0.23f));
                if (subCols.Count > 0)
                {
                    float ex = r.x;
                    foreach (var col in subCols)
                    {
                        var cp = elem.FindPropertyRelative(col);
                        if (cp != null)
                            DrawPrimitiveInline(new Rect(ex + 1f, y + 1f, colW - 2f, LineHeight - 2f), cp, so);
                        ex += colW;
                    }
                }
                else
                    DrawPrimitiveInline(new Rect(r.x + 1f, y + 1f, r.width - delW - 2f, LineHeight - 2f), elem, so);

                if (GUI.Button(new Rect(r.xMax - delW + 1f, y + 1f, delW - 2f, LineHeight - 2f),
                    "−", EditorStyles.miniButton))
                    delIdx = i;
                y += LineHeight;
            }

            if (delIdx >= 0)
            { arr.DeleteArrayElementAtIndex(delIdx); so.ApplyModifiedProperties(); RecomputeAllRowHeights(); Repaint(); }

            if (GUI.Button(new Rect(r.x + 1f, y + 1f, r.width - 2f, LineHeight - 2f), "+", EditorStyles.miniButton))
            { arr.arraySize++; so.ApplyModifiedProperties(); RecomputeAllRowHeights(); Repaint(); }
        }

        // Inline struct: "Field : [control]" rows, Generic fields rendered inline
        private void DrawInlineStruct(Rect cr, SerializedObject so, SerializedProperty prop)
        {
            const float px = 2f;
            float vw     = cr.width - px * 2f;
            float labelW = vw * 0.42f;
            float y      = cr.y + CellPadding;
            int   row    = 0;

            var child = prop.Copy(); var end = prop.GetEndProperty();
            if (child.NextVisible(true))
            {
                while (!SerializedProperty.EqualContents(child, end))
                {
                    bool isGeneric = child.propertyType == SerializedPropertyType.Generic;
                    float fh = isGeneric && child.isArray
                        ? (1 + child.arraySize + 1) * LineHeight + 4f
                        : isGeneric
                            ? CountDirectChildren(child) * LineHeight + 4f
                            : LineHeight;

                    EditorGUI.DrawRect(new Rect(cr.x + px, y, vw, fh),
                        row % 2 == 0 ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.20f, 0.20f, 0.20f));

                    GUI.Label(new Rect(cr.x + px + 2f, y + 1f, labelW - 4f, LineHeight - 2f),
                        ObjectNames.NicifyVariableName(child.name), EditorStyles.miniLabel);

                    float cx = cr.x + px + labelW;
                    float cw = vw - labelW - 1f;
                    if (isGeneric)
                    {
                        if (child.isArray)
                            DrawNestedArrayCell(new Rect(cx, y + 1f, cw, fh - 2f), child.Copy(), so);
                        else
                            DrawNestedStructCell(new Rect(cx, y + 1f, cw, fh - 2f), child.Copy(), so);
                    }
                    else
                        DrawPrimitiveInline(new Rect(cx + 1f, y + 1f, cw - 2f, LineHeight - 2f), child.Copy(), so);

                    y += fh; row++;
                    child.NextVisible(false);
                }
            }
        }

        // Draws a nested struct inline (label : value rows) within a cell
        private void DrawNestedStructCell(Rect r, SerializedProperty p, SerializedObject so)
        {
            float labelW = r.width * 0.45f;
            float cy = r.y;
            var child = p.Copy(); var end = p.GetEndProperty();
            if (child.NextVisible(true))
                while (!SerializedProperty.EqualContents(child, end))
                {
                    GUI.Label(new Rect(r.x + 2f, cy + 1f, labelW - 4f, LineHeight - 2f),
                        ObjectNames.NicifyVariableName(child.name), EditorStyles.miniLabel);
                    if (child.propertyType != SerializedPropertyType.Generic)
                        DrawPrimitiveInline(new Rect(r.x + labelW, cy + 1f, r.width - labelW - 1f, LineHeight - 2f), child.Copy(), so);
                    cy += LineHeight;
                    child.NextVisible(false);
                }
        }

        // Draw a single editable primitive control at rect
        /// Returns the declared C# type of an ObjectReference SerializedProperty by walking
        /// the property path through the target object's field hierarchy via reflection.
        private static Type ResolveObjectReferenceType(SerializedProperty p)
        {
            // Always resolve from the declared field type via reflection
            // so the picker always shows the correct type regardless of what is currently assigned.
            try
            {
                Type currentType = p.serializedObject.targetObject.GetType();
                string[] parts   = p.propertyPath.Replace("Array.data[", "[").Split('.');

                foreach (var part in parts)
                {
                    if (part.StartsWith("[")) { // array element — unwrap to element type
                        if (currentType.IsArray)
                            currentType = currentType.GetElementType();
                        else if (currentType.IsGenericType)
                            currentType = currentType.GetGenericArguments()[0];
                        continue;
                    }

                    FieldInfo fi = null;
                    var t = currentType;
                    while (t != null && fi == null)
                    {
                        fi = t.GetField(part,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        t = t.BaseType;
                    }
                    if (fi == null) return typeof(UnityEngine.Object);

                    currentType = fi.FieldType;
                    // Unwrap array / List<T>
                    if (currentType.IsArray)
                        currentType = currentType.GetElementType();
                    else if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(List<>))
                        currentType = currentType.GetGenericArguments()[0];
                }

                return typeof(UnityEngine.Object).IsAssignableFrom(currentType)
                    ? currentType
                    : typeof(UnityEngine.Object);
            }
            catch
            {
                return typeof(UnityEngine.Object);
            }
        }

        private void DrawPrimitiveInline(Rect r, SerializedProperty p, SerializedObject so)
        {
            if (p.propertyType == SerializedPropertyType.Generic) return; // safety
            EditorGUI.BeginChangeCheck();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    p.intValue = EditorGUI.IntField(r, p.intValue); break;
                case SerializedPropertyType.Float:
                    p.floatValue = EditorGUI.FloatField(r, p.floatValue); break;
                case SerializedPropertyType.Boolean:
                    p.boolValue = EditorGUI.Toggle(new Rect(r.x + 1f, r.y, 14f, r.height), p.boolValue); break;
                case SerializedPropertyType.String:
                    p.stringValue = EditorGUI.TextField(r, p.stringValue); break;
                case SerializedPropertyType.Enum:
                    p.enumValueIndex = EditorGUI.Popup(r, p.enumValueIndex, p.enumDisplayNames); break;
                case SerializedPropertyType.Color:
                    p.colorValue = EditorGUI.ColorField(r, p.colorValue); break;
                case SerializedPropertyType.ObjectReference:
                    // Clamp to 16px to avoid Unity's thumbnail-mode rendering for Sprite / Texture.
                    var objRectI = new Rect(r.x, r.y + (r.height - 16f) * 0.5f, r.width, 16f);
                    p.objectReferenceValue = EditorGUI.ObjectField(objRectI, p.objectReferenceValue,
                        ResolveObjectReferenceType(p), false); break;
                case SerializedPropertyType.Vector2:
                    p.vector2Value = EditorGUI.Vector2Field(r, GUIContent.none, p.vector2Value); break;
                case SerializedPropertyType.Vector3:
                    p.vector3Value = EditorGUI.Vector3Field(r, GUIContent.none, p.vector3Value); break;
                case SerializedPropertyType.Vector2Int:
                    p.vector2IntValue = EditorGUI.Vector2IntField(r, GUIContent.none, p.vector2IntValue); break;
                case SerializedPropertyType.Vector3Int:
                    p.vector3IntValue = EditorGUI.Vector3IntField(r, GUIContent.none, p.vector3IntValue); break;
                default:
                    GUI.Label(r, GetPropertyValueString(p), EditorStyles.miniLabel); break;
            }
            if (EditorGUI.EndChangeCheck())
                so.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws a SerializedProperty using type-specific EditorGUI methods,
        /// bypassing Unity's DecoratorDrawers ([Header], [Space], etc.) that would
        /// otherwise inject extra content into the cell.
        /// </summary>
        private static void DrawPropertyDirect(Rect rect, SerializedProperty prop, Type fieldType)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = EditorGUI.IntField(rect, prop.intValue);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = EditorGUI.FloatField(rect, prop.floatValue);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = EditorGUI.Toggle(rect, prop.boolValue);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = EditorGUI.TextField(rect, prop.stringValue);
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = EditorGUI.ColorField(rect, prop.colorValue);
                    break;
                case SerializedPropertyType.Enum:
                    if (fieldType != null && fieldType.IsEnum)
                    {
                        var names  = prop.enumDisplayNames;
                        var index  = prop.enumValueIndex;
                        prop.enumValueIndex = EditorGUI.Popup(rect, index, names);
                    }
                    else
                    {
                        prop.enumValueIndex = EditorGUI.Popup(rect, prop.enumValueIndex, prop.enumDisplayNames);
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                    // Clamp to 16px — above that Unity enters "thumbnail mode" and renders
                    // a large preview image (Sprite, Texture2D, etc.) instead of the compact field.
                    var objRectD = new Rect(rect.x, rect.y + (rect.height - 16f) * 0.5f, rect.width, 16f);
                    prop.objectReferenceValue = EditorGUI.ObjectField(
                        objRectD, prop.objectReferenceValue,
                        fieldType ?? typeof(UnityEngine.Object), false);
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = DrawLayerMask(rect, prop.intValue);
                    break;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = EditorGUI.Vector2Field(rect, GUIContent.none, prop.vector2Value);
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = EditorGUI.Vector3Field(rect, GUIContent.none, prop.vector3Value);
                    break;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = EditorGUI.Vector4Field(rect, GUIContent.none, prop.vector4Value);
                    break;
                case SerializedPropertyType.Vector2Int:
                    prop.vector2IntValue = EditorGUI.Vector2IntField(rect, GUIContent.none, prop.vector2IntValue);
                    break;
                case SerializedPropertyType.Vector3Int:
                    prop.vector3IntValue = EditorGUI.Vector3IntField(rect, GUIContent.none, prop.vector3IntValue);
                    break;
                case SerializedPropertyType.Rect:
                    prop.rectValue = EditorGUI.RectField(rect, GUIContent.none, prop.rectValue);
                    break;
                case SerializedPropertyType.RectInt:
                    prop.rectIntValue = EditorGUI.RectIntField(rect, GUIContent.none, prop.rectIntValue);
                    break;
                case SerializedPropertyType.Bounds:
                    prop.boundsValue = EditorGUI.BoundsField(rect, GUIContent.none, prop.boundsValue);
                    break;
                case SerializedPropertyType.BoundsInt:
                    prop.boundsIntValue = EditorGUI.BoundsIntField(rect, GUIContent.none, prop.boundsIntValue);
                    break;
                case SerializedPropertyType.AnimationCurve:
                    prop.animationCurveValue = EditorGUI.CurveField(rect, prop.animationCurveValue);
                    break;
                case SerializedPropertyType.Gradient:
                    // GradientField is internal in older Unity — use PropertyField as fallback (no decorators issue here)
                    EditorGUI.PropertyField(rect, prop, GUIContent.none);
                    break;
                case SerializedPropertyType.Quaternion:
                    var euler = prop.quaternionValue.eulerAngles;
                    euler = EditorGUI.Vector3Field(rect, GUIContent.none, euler);
                    prop.quaternionValue = Quaternion.Euler(euler);
                    break;
                default:
                    // Fallback — should only hit non-generic unknown types
                    EditorGUI.PropertyField(rect, prop, GUIContent.none);
                    break;
            }
        }

        // ── Generic cell height helpers ────────────────────────────────────────

        private void RecomputeAllRowHeights()
        {
            var tab = ActiveTab; if (tab == null) return;
            tab.RowHeights.Clear();
            for (int r = 0; r < tab.Rows.Count; r++)
                tab.RowHeights.Add(ComputeRowHeight(tab, r));
        }

        private float ComputeRowHeight(FDataTableTab tab, int rowIndex)
        {
            var asset = tab.Rows[rowIndex];
            if (!tab.SerializedCache.TryGetValue(asset, out var serialized))
            {
                serialized = new SerializedObject(asset);
                tab.SerializedCache[asset] = serialized;
            }
            serialized.Update();

            float maxH = RowHeight;
            for (int c = 0; c < tab.Columns.Count; c++)
            {
                var prop = serialized.FindProperty(tab.Columns[c].Name);
                if (prop != null && prop.propertyType == SerializedPropertyType.Generic)
                    maxH = Mathf.Max(maxH, ComputeGenericCellHeight(prop, (rowIndex, c), tab));
            }
            return maxH;
        }

        private float ComputeGenericCellHeight(SerializedProperty prop, (int r, int c) key, FDataTableTab tab)
        {
            if (prop.isArray)
            {
                if (prop.arraySize == 0)
                    return (1 + 1) * LineHeight + CellPadding * 2f; // header + add button

                // Check if elements are structs with column info
                var subCols = new List<string>();
                var e0 = prop.GetArrayElementAtIndex(0);
                if (e0.propertyType == SerializedPropertyType.Generic && !e0.isArray)
                {
                    var sc = e0.Copy(); var se = e0.GetEndProperty();
                    if (sc.NextVisible(true))
                        while (!SerializedProperty.EqualContents(sc, se))
                        { subCols.Add(sc.name); sc.NextVisible(false); }
                }

                float totalH = LineHeight + CellPadding * 2f; // header
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    float elemH = LineHeight + 4f;
                    foreach (var col in subCols)
                    {
                        var cp = elem.FindPropertyRelative(col);
                        if (cp == null || cp.propertyType != SerializedPropertyType.Generic) continue;
                        float ch = cp.isArray
                            ? (1 + cp.arraySize + 1) * LineHeight + 4f
                            : CountDirectChildren(cp) * LineHeight + 4f;
                        elemH = Mathf.Max(elemH, ch);
                    }
                    totalH += elemH;
                }
                totalH += LineHeight; // add button
                return totalH;
            }
            else
            {
                // Struct: each field takes LineHeight, but Generic fields (arrays/nested structs) take more
                float h = CellPadding * 2f;
                var child = prop.Copy(); var end = prop.GetEndProperty();
                if (child.NextVisible(true))
                    while (!SerializedProperty.EqualContents(child, end))
                    {
                        if (child.propertyType == SerializedPropertyType.Generic)
                            h += child.isArray
                                ? (1 + child.arraySize + 1) * LineHeight + 4f
                                : CountDirectChildren(child) * LineHeight + 4f;
                        else
                            h += LineHeight;
                        child.NextVisible(false);
                    }
                return Mathf.Max(RowHeight, h);
            }
        }

        private static int CountContentLines(SerializedProperty prop)
        {
            if (prop.isArray)
            {
                int lines = 0;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    lines += elem.propertyType == SerializedPropertyType.Generic
                        ? 1 + CountDirectChildren(elem)
                        : 1;
                }
                return Mathf.Max(1, lines);
            }
            return Mathf.Max(1, CountDirectChildren(prop));
        }

        private static int CountDirectChildren(SerializedProperty prop)
        {
            int count = 0;
            var child = prop.Copy();
            var end   = prop.GetEndProperty();
            bool next = child.NextVisible(true);
            while (next && !SerializedProperty.EqualContents(child, end))
            {
                count++;
                next = child.NextVisible(false);
            }
            return count;
        }

        private static string GetPropertyValueString(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:       return prop.intValue.ToString();
                case SerializedPropertyType.Float:         return prop.floatValue.ToString("0.##");
                case SerializedPropertyType.Boolean:       return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(prop.stringValue) ? "(empty)" : prop.stringValue;
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.intValue.ToString();
                case SerializedPropertyType.Color:
                    return $"#{ColorUtility.ToHtmlStringRGB(prop.colorValue)}";
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "None";
                case SerializedPropertyType.Vector2:    return prop.vector2Value.ToString("0.##");
                case SerializedPropertyType.Vector3:    return prop.vector3Value.ToString("0.##");
                case SerializedPropertyType.Vector2Int: return prop.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return prop.vector3IntValue.ToString();
                case SerializedPropertyType.LayerMask:
                    return LayerMask.LayerToName((int)Mathf.Log(Mathf.Max(1, prop.intValue), 2));
                case SerializedPropertyType.Generic:
                    return prop.isArray ? $"[{prop.arraySize}]" : $"{{{prop.type}}}";
                default: return prop.type;
            }
        }

        private static int DrawLayerMask(Rect rect, int maskValue)
        {
            var layerNames = new System.Collections.Generic.List<string>();
            var layerValues = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layerNames.Add(name);
                    layerValues.Add(i);
                }
            }
            return EditorGUI.MaskField(rect, maskValue, layerNames.ToArray());
        }

        // ── Column Resize ────────────────────────────────────────────────────
        private void HandleColumnResize(int col, Rect handle)
        {
            var tab = ActiveTab; if (tab == null) return;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (handle.Contains(e.mousePosition))
                    {
                        _resizingCol = col;
                        _resizeStartX = e.mousePosition.x;
                        _resizeStartWidth = tab.ColumnWidths[col];
                        GUIUtility.hotControl = controlId;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_resizingCol == col && GUIUtility.hotControl == controlId)
                    {
                        float delta = e.mousePosition.x - _resizeStartX;
                        tab.ColumnWidths[col] = Mathf.Max(MinColWidth, _resizeStartWidth + delta);
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        _resizingCol = -1;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        // ── Data Operations ──────────────────────────────────────────────────
        private void AddRow()
        {
            var tab = ActiveTab; if (tab == null) return;
            // Build a temporary config using tab-local folder/prefix instead of global config
            var effectiveConfig = new FDataTableTypeConfig
            {
                typeName     = tab.Config?.typeName ?? tab.TypeName,
                targetFolder = tab.TabFolder,
                filePrefix   = tab.TabPrefix,
                includedFields = tab.Config?.includedFields ?? new System.Collections.Generic.List<string>(),
                excludedFields = tab.Config?.excludedFields ?? new System.Collections.Generic.List<string>()
            };
            var newAsset = FDataTableAssetFactory.Create(tab.Type, effectiveConfig);
            tab.Rows.Add(newAsset);
            tab.SelectedRow = tab.Rows.Count - 1;
            SaveRowOrder(tab);
            RebuildFilteredIndices(tab);
            RecomputeAllRowHeights();
            Repaint();
        }

        private void DeleteRow(int index)
        {
            var tab = ActiveTab; if (tab == null) return;
            if (index < 0 || index >= tab.Rows.Count) return;

            bool confirm = EditorUtility.DisplayDialog(
                "Remove Row",
                $"Remove '{tab.Rows[index].name}' from the table?\nThe asset file will be deleted from the project.",
                "Remove", "Cancel");

            if (!confirm) return;

            var target = tab.Rows[index];
            tab.SerializedCache.Remove(target);
            FDataTableAssetFactory.Delete(target);
            tab.Rows.RemoveAt(index);
            tab.SelectedRow = Mathf.Clamp(tab.SelectedRow, -1, tab.Rows.Count - 1);
            SaveRowOrder(tab);
            RebuildFilteredIndices(tab);
            RecomputeAllRowHeights();
            Repaint();
        }

        // ── Type Handling ────────────────────────────────────────────────────
        private void RefreshTypeList()
        {
            _soTypes.Clear();
            _soTypeNames.Clear();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.IsAbstract || type.IsGenericType) continue;
                        if (!typeof(ScriptableObject).IsAssignableFrom(type)) continue;
                        if (type.Namespace != null && type.Namespace.StartsWith("Unity")) continue;
                        if (type.Namespace != null && type.Namespace.StartsWith("FDataTable")) continue;

                        _soTypes.Add(type);
                        _soTypeNames.Add(type.Name);
                    }
                }
                catch { /* ignore assemblies that throw on GetTypes */ }
            }
        }

        internal void OpenTab(Type type)
        {
            if (type == null) return;
            // Switch to existing tab if already open
            int existing = _tabs.FindIndex(t => t.Type == type);
            if (existing >= 0)
            {
                _activeTabIndex = existing;
                return;
            }
            // Create new tab — init folder/prefix from single global default
            var tab = new FDataTableTab { Type = type, TypeName = type.Name };
            tab.Config    = _settings.GetOrAddConfig(type);
            tab.TabFolder = _settings.globalDefaultFolder;
            tab.TabPrefix = _settings.globalDefaultPrefix;
            tab.Rows    = ApplySavedRowOrder(FDataTableScanner.FindAll(type), tab.TypeName);
            tab.Columns = FDataTableFieldResolver.GetDisplayableFields(type, tab.Config);
            foreach (var _ in tab.Columns) tab.ColumnWidths.Add(DefaultColWidth);
            _tabs.Add(tab);
            _activeTabIndex = _tabs.Count - 1;
            EnsureFilterState(tab);
            RebuildFilteredIndices(tab);
            RecomputeAllRowHeights();
        }

        // ── Row order persistence ─────────────────────────────────────────────
        /// Saves the current in-memory row order as comma-separated GUIDs to EditorPrefs.
        private void SaveRowOrder(FDataTableTab tab)
        {
            var guids = new System.Text.StringBuilder();
            for (int i = 0; i < tab.Rows.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(tab.Rows[i]);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                {
                    if (guids.Length > 0) guids.Append(',');
                    guids.Append(guid);
                }
            }
            EditorPrefs.SetString(PrefKeyTabRowOrder(tab.TypeName), guids.ToString());
        }

        /// Reorders <paramref name="rows"/> to match the GUID order saved in EditorPrefs.
        /// Rows not in the saved order are appended at the end (preserves new additions).
        private List<ScriptableObject> ApplySavedRowOrder(List<ScriptableObject> rows, string typeName)
        {
            string saved = EditorPrefs.GetString(PrefKeyTabRowOrder(typeName), string.Empty);
            if (string.IsNullOrEmpty(saved)) return rows;

            var guidOrder = new List<string>(saved.Split(','));

            // Build GUID → asset map from the scanned list
            var map = new Dictionary<string, ScriptableObject>();
            foreach (var r in rows)
            {
                string path = AssetDatabase.GetAssetPath(r);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) map[guid] = r;
            }

            var ordered   = new List<ScriptableObject>();
            var seenGuids = new HashSet<string>();

            // First: add rows in saved order
            foreach (var guid in guidOrder)
            {
                if (map.TryGetValue(guid, out var asset))
                {
                    ordered.Add(asset);
                    seenGuids.Add(guid);
                }
            }

            // Then: append any rows not covered by the saved order (newly discovered assets)
            foreach (var kvp in map)
                if (!seenGuids.Contains(kvp.Key))
                    ordered.Add(kvp.Value);

            return ordered;
        }

        private void Reload()
        {
            var tab = ActiveTab; if (tab == null) return;
            tab.Rows.Clear();
            tab.Columns.Clear();
            tab.ColumnWidths.Clear();
            tab.SerializedCache.Clear();
            tab.RowHeights.Clear();
            tab.ExpandedCells.Clear();
            tab.SelectedRow = -1;

            tab.Config  = _settings.GetOrAddConfig(tab.Type);
            tab.Rows    = ApplySavedRowOrder(FDataTableScanner.FindAll(tab.Type), tab.TypeName);
            tab.Columns = FDataTableFieldResolver.GetDisplayableFields(tab.Type, tab.Config);
            foreach (var _ in tab.Columns) tab.ColumnWidths.Add(DefaultColWidth);

            EnsureFilterState(tab);
            RebuildFilteredIndices(tab);
            RecomputeAllRowHeights();
            Repaint();
        }

        // ── Filtering ────────────────────────────────────────────────────────
        private const float FilterRowHeight = 18f;

        private static void EnsureFilterState(FDataTableTab tab)
        {
            while (tab.ColFilters.Count < tab.Columns.Count) tab.ColFilters.Add(string.Empty);
            while (tab.ColFilters.Count > tab.Columns.Count)
                tab.ColFilters.RemoveAt(tab.ColFilters.Count - 1);
        }

        private void RebuildFilteredIndices(FDataTableTab tab)
        {
            tab.FilteredIndices.Clear();
            bool hasGlobal = !string.IsNullOrEmpty(tab.GlobalFilter);
            bool hasName   = !string.IsNullOrEmpty(tab.NameFilter);
            bool anyCols   = tab.ColFilters.Exists(f => !string.IsNullOrEmpty(f));

            for (int i = 0; i < tab.Rows.Count; i++)
            {
                var so = tab.Rows[i]; if (so == null) continue;

                // Name column filter
                if (hasName && so.name.IndexOf(tab.NameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Lazily get/create SerializedObject for value access
                SerializedObject serialized = null;
                if (anyCols || hasGlobal)
                {
                    if (!tab.SerializedCache.TryGetValue(so, out serialized))
                    {
                        serialized = new SerializedObject(so);
                        tab.SerializedCache[so] = serialized;
                    }
                }

                // Per-column filters
                if (anyCols)
                {
                    bool colMatch = true;
                    for (int c = 0; c < tab.Columns.Count && c < tab.ColFilters.Count; c++)
                    {
                        string f = tab.ColFilters[c];
                        if (string.IsNullOrEmpty(f)) continue;
                        string val = serialized != null
                            ? GetPropertyValueAsString(serialized.FindProperty(tab.Columns[c].Name))
                            : string.Empty;
                        if (val.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                        { colMatch = false; break; }
                    }
                    if (!colMatch) continue;
                }

                // Global filter — row passes if any field contains the term
                if (hasGlobal)
                {
                    bool globalMatch = so.name.IndexOf(tab.GlobalFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!globalMatch && serialized != null)
                    {
                        for (int c = 0; c < tab.Columns.Count && !globalMatch; c++)
                        {
                            string val = GetPropertyValueAsString(serialized.FindProperty(tab.Columns[c].Name));
                            if (val.IndexOf(tab.GlobalFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                                globalMatch = true;
                        }
                    }
                    if (!globalMatch) continue;
                }

                tab.FilteredIndices.Add(i);
            }
        }

        private static string GetPropertyValueAsString(SerializedProperty prop)
        {
            if (prop == null) return string.Empty;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:      return prop.stringValue;
                case SerializedPropertyType.Integer:     return prop.intValue.ToString();
                case SerializedPropertyType.Boolean:     return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:       return prop.floatValue.ToString("F2");
                case SerializedPropertyType.Enum:
                    return (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length)
                        ? prop.enumDisplayNames[prop.enumValueIndex] : string.Empty;
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : string.Empty;
                case SerializedPropertyType.Color:       return prop.colorValue.ToString();
                case SerializedPropertyType.Vector2:     return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3:     return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4:     return prop.vector4Value.ToString();
                case SerializedPropertyType.Vector2Int:  return prop.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int:  return prop.vector3IntValue.ToString();
                default:                                 return string.Empty;
            }
        }

        private void DrawFilterRow()
        {
            var tab = ActiveTab; if (tab == null) return;
            EnsureFilterState(tab);

            Rect filterBar = EditorGUILayout.BeginHorizontal(GUILayout.Height(FilterRowHeight));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(filterBar, new Color(0.14f, 0.16f, 0.20f));

            // # column — shows ✕ to clear all per-column filters when any are active
            bool anyColFilter = !string.IsNullOrEmpty(tab.NameFilter)
                             || tab.ColFilters.Exists(f => !string.IsNullOrEmpty(f));
            Rect numRect = GUILayoutUtility.GetRect(RowNumWidth, FilterRowHeight, GUILayout.Width(RowNumWidth));
            if (anyColFilter)
            {
                if (GUI.Button(numRect, new GUIContent("✕", "Clear column filters"), Styles.RowNumCell))
                {
                    tab.NameFilter = string.Empty;
                    for (int i = 0; i < tab.ColFilters.Count; i++) tab.ColFilters[i] = string.Empty;
                    RebuildFilteredIndices(tab);
                    Repaint();
                    GUI.FocusControl(null);
                }
            }
            else
            {
                GUI.Box(numRect, GUIContent.none, Styles.RowNumCell);
            }

            // Name column filter
            Rect nameRect = GUILayoutUtility.GetRect(NameColWidth, FilterRowHeight, GUILayout.Width(NameColWidth));
            EditorGUI.BeginChangeCheck();
            string newNameFilter = DrawFilterField(nameRect, tab.NameFilter);
            if (EditorGUI.EndChangeCheck())
            {
                tab.NameFilter = newNameFilter;
                RebuildFilteredIndices(tab);
                Repaint();
            }

            // Per-column filters
            for (int c = 0; c < tab.Columns.Count; c++)
            {
                float w = tab.ColumnWidths[c];
                Rect colRect = GUILayoutUtility.GetRect(w, FilterRowHeight, GUILayout.Width(w));
                string current = c < tab.ColFilters.Count ? tab.ColFilters[c] : string.Empty;
                EditorGUI.BeginChangeCheck();
                string newFilter = DrawFilterField(colRect, current);
                if (EditorGUI.EndChangeCheck())
                {
                    while (tab.ColFilters.Count <= c) tab.ColFilters.Add(string.Empty);
                    tab.ColFilters[c] = newFilter;
                    RebuildFilteredIndices(tab);
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string DrawFilterField(Rect rect, string current)
        {
            Rect fieldRect = new Rect(rect.x + 2f, rect.y + 1f, rect.width - 4f, rect.height - 2f);
            string result = EditorGUI.TextField(fieldRect, current, EditorStyles.miniTextField);
            if (string.IsNullOrEmpty(result) && Event.current.type == EventType.Repaint)
                GUI.Label(fieldRect, "filter…", Styles.FilterHint);
            return result;
        }

        // ── Styles (lazy init) ────────────────────────────────────────────────
        private static class Styles
        {
            private static GUIStyle _headerBackground;
            private static GUIStyle _headerCell;
            private static GUIStyle _headerLabel;
            private static GUIStyle _rowEven;
            private static GUIStyle _rowOdd;
            private static GUIStyle _rowSelected;
            private static GUIStyle _rowNumCell;
            private static GUIStyle _cellBorder;
            private static GUIStyle _summaryLabel;

            public static GUIStyle HeaderBackground => _headerBackground ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.22f, 0.22f, 0.22f)) }
            };
            public static GUIStyle HeaderCell => _headerCell ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                normal = { textColor = Color.white, background = MakeTexture(1, 1, new Color(0.22f, 0.22f, 0.22f)) }
            };
            public static GUIStyle HeaderLabel => _headerLabel ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
            public static GUIStyle RowEven => _rowEven ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.18f, 0.18f, 0.18f)) }
            };
            public static GUIStyle RowOdd => _rowOdd ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.21f, 0.21f, 0.21f)) }
            };
            public static GUIStyle RowSelected => _rowSelected ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.17f, 0.36f, 0.53f)) }
            };
            public static GUIStyle RowNumCell => _rowNumCell ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            public static GUIStyle CellBorder => _cellBorder ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.13f, 0.13f, 0.13f)) },
                border = new RectOffset(1, 1, 1, 1),
                margin = new RectOffset(0, 1, 0, 1)
            };
            public static GUIStyle SummaryLabel => _summaryLabel ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment  = TextAnchor.UpperLeft,
                clipping   = TextClipping.Clip,
                wordWrap   = false,
                normal     = { textColor = new Color(0.75f, 0.85f, 1f) }
            };
            private static GUIStyle _summaryLabelBold;
            public static GUIStyle SummaryLabelBold => _summaryLabelBold ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment  = TextAnchor.UpperLeft,
                clipping   = TextClipping.Clip,
                wordWrap   = false,
                fontStyle  = FontStyle.Bold,
                normal     = { textColor = new Color(0.9f, 0.9f, 0.6f) }
            };

            // ── Tab styles
            private static GUIStyle _tabBarBackground;
            private static GUIStyle _tabActive;
            private static GUIStyle _tabInactive;
            private static GUIStyle _tabLabelActive;
            private static GUIStyle _tabLabelInactive;
            private static GUIStyle _tabClose;

            public static GUIStyle TabBarBackground => _tabBarBackground ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.16f, 0.16f, 0.16f)) }
            };
            public static GUIStyle TabActive => _tabActive ??= new GUIStyle
            {
                normal   = { background = MakeTexture(1, 1, new Color(0.24f, 0.24f, 0.28f)) },
                border   = new RectOffset(2, 2, 2, 0),
                margin   = new RectOffset(1, 1, 2, 0),
                padding  = new RectOffset(4, 4, 2, 0)
            };
            public static GUIStyle TabInactive => _tabInactive ??= new GUIStyle
            {
                normal   = { background = MakeTexture(1, 1, new Color(0.18f, 0.18f, 0.20f)) },
                border   = new RectOffset(2, 2, 2, 0),
                margin   = new RectOffset(1, 1, 2, 0),
                padding  = new RectOffset(4, 4, 2, 0)
            };
            public static GUIStyle TabLabelActive => _tabLabelActive ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white }
            };
            public static GUIStyle TabLabelInactive => _tabLabelInactive ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) }
            };
            public static GUIStyle TabClose => _tabClose ??= new GUIStyle(EditorStyles.miniButton)
            {
                fontSize  = 8,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 0, 0)
            };

            private static GUIStyle _tabAddBtn;
            public static GUIStyle TabAdd => _tabAddBtn ??= new GUIStyle(GUI.skin.button)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { background = MakeTexture(1, 1, new Color(0.16f, 0.22f, 0.16f)), textColor = new Color(0.5f, 0.92f, 0.5f) },
                hover     = { background = MakeTexture(1, 1, new Color(0.20f, 0.28f, 0.20f)), textColor = Color.white },
                active    = { background = MakeTexture(1, 1, new Color(0.14f, 0.18f, 0.14f)), textColor = Color.white },
                border    = new RectOffset(2, 2, 2, 2),
                margin    = new RectOffset(2, 2, 2, 2),
                padding   = new RectOffset(0, 0, 0, 2)
            };

            private static GUIStyle _ghostRow;
            public static GUIStyle GhostRow => _ghostRow ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.16f, 0.20f, 0.16f)) }
            };
            private static GUIStyle _ghostLabel;
            public static GUIStyle GhostLabel => _ghostLabel ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Italic,
                normal    = { textColor = new Color(0.45f, 0.70f, 0.45f) }
            };

            private static GUIStyle _tabSettingsBar;
            public static GUIStyle TabSettingsBar => _tabSettingsBar ??= new GUIStyle
            {
                normal  = { background = MakeTexture(1, 1, new Color(0.14f, 0.14f, 0.16f)) },
                padding = new RectOffset(6, 6, 2, 2),
                margin  = new RectOffset(0, 0, 0, 0)
            };

            private static GUIStyle _nameCellBg;
            public static GUIStyle NameCellBg => _nameCellBg ??= new GUIStyle
            {
                normal = { background = MakeTexture(1, 1, new Color(0.15f, 0.18f, 0.22f)) },
                border = new RectOffset(1, 1, 1, 1),
                margin = new RectOffset(0, 1, 0, 1)
            };

            private static GUIStyle _filterHint;
            public static GUIStyle FilterHint => _filterHint ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = new Color(0.40f, 0.40f, 0.40f) }
            };

            private static Texture2D MakeTexture(int w, int h, Color col)
            {
                var tex = new Texture2D(w, h);
                tex.SetPixel(0, 0, col);
                tex.Apply();
                return tex;
            }
        }
    }

    /// <summary>
    /// Holds all per-tab state for one ScriptableObject type.
    /// </summary>
    [Serializable]
    internal sealed class FDataTableTab
    {
        public string                                          TypeName;
        public Type                                            Type;
        public List<ScriptableObject>                          Rows           = new List<ScriptableObject>();
        public List<FieldInfo>                                 Columns        = new List<FieldInfo>();
        public List<float>                                     ColumnWidths   = new List<float>();
        public List<float>                                     RowHeights     = new List<float>();
        public HashSet<(int, int)>                             ExpandedCells  = new HashSet<(int, int)>();
        public Dictionary<ScriptableObject, SerializedObject>  SerializedCache= new Dictionary<ScriptableObject, SerializedObject>();
        public Vector2                                         ScrollPos;
        public int                                             SelectedRow    = -1;
        public FDataTableTypeConfig                            Config;

        // Per-tab overrides — independent of global Config so changing one tab
        // does not affect other tabs or the global default.
        public string                                          TabFolder;
        public string                                          TabPrefix;

        // Filter state — per-tab, reset each editor session
        public string       GlobalFilter    = string.Empty;
        public string       NameFilter      = string.Empty;
        public List<string> ColFilters      = new List<string>();
        public List<int>    FilteredIndices = new List<int>();
    }

    /// <summary>
    /// Popup that displays a complex property (array / List / serializable class) as a mini-table.
    /// Columns are: index + fields of the element type (for arrays) or "Field | Value" (for structs).
    /// </summary>
    internal sealed class FDataTablePropertyPopup : PopupWindowContent
    {
        private readonly SerializedObject _so;
        private string   _propPath;
        private Vector2  _scroll;

        // Drill-down for deeply-nested generics (3+ levels)
        private readonly Stack<string>  _pathStack   = new Stack<string>();
        private readonly Stack<Vector2> _scrollStack = new Stack<Vector2>();

        private const float RowH      = 20f;
        private const float HeaderH   = 22f;
        private const float NavBarH   = 24f;
        private const float IndexColW = 28f;
        private const float DelBtnW   = 22f;
        private const float LabelRatio = 0.40f;

        private readonly List<string> _colNames  = new List<string>();
        private readonly List<float>  _colWidths = new List<float>();
        private string _lastBuiltPath;

        private int   _resizingCol  = -1;
        private float _resizeStartX;
        private float _resizeStartW;

        public FDataTablePropertyPopup(SerializedObject so, string propPath)
        {
            _so       = so;
            _propPath = propPath;
        }

        public override Vector2 GetWindowSize() => new Vector2(560f, 420f);

        public override void OnGUI(Rect rect)
        {
            _so.Update();
            var prop = _so.FindProperty(_propPath);
            if (prop == null)
            {
                EditorGUI.HelpBox(new Rect(4f, 4f, rect.width - 8f, 40f),
                    "Property not found.", MessageType.Error);
                return;
            }

            if (_lastBuiltPath != _propPath)
                RebuildColumns(prop);

            float y = 0f;
            if (_pathStack.Count > 0)
                DrawNavBar(rect, ref y);

            EditorGUI.DrawRect(new Rect(0, y, rect.width, HeaderH), new Color(0.18f, 0.18f, 0.22f));
            GUI.Label(new Rect(6f, y + 2f, rect.width - 6f, HeaderH - 2f),
                ObjectNames.NicifyVariableName(prop.name), EditorStyles.boldLabel);
            y += HeaderH;

            if (prop.isArray)
                DrawArrayTable(prop, rect, ref y);
            else
                DrawStructRows(prop, rect, ref y);

            if (_so.ApplyModifiedProperties())
                editorWindow?.Repaint();
        }

        // ── Columns ───────────────────────────────────────────────────────────
        private void RebuildColumns(SerializedProperty prop)
        {
            _lastBuiltPath = _propPath;
            _colNames.Clear();
            _colWidths.Clear();

            if (!prop.isArray || prop.arraySize == 0) return;
            var elem = prop.GetArrayElementAtIndex(0);
            if (elem.propertyType != SerializedPropertyType.Generic || elem.isArray) return;

            var child = elem.Copy();
            var end   = elem.GetEndProperty();
            if (child.NextVisible(true))
                while (!SerializedProperty.EqualContents(child, end))
                {
                    _colNames.Add(child.name);
                    _colWidths.Add(120f);
                    child.NextVisible(false);
                }
        }

        // ── Drill-down (fallback for deeply nested generics) ──────────────────
        private void NavigateTo(string childPath)
        {
            _pathStack.Push(_propPath);
            _scrollStack.Push(_scroll);
            _propPath      = childPath;
            _scroll        = Vector2.zero;
            _lastBuiltPath = null;
            editorWindow?.Repaint();
        }

        private void DrawNavBar(Rect rect, ref float y)
        {
            EditorGUI.DrawRect(new Rect(0, y, rect.width, NavBarH), new Color(0.12f, 0.14f, 0.18f));
            if (GUI.Button(new Rect(4f, y + 3f, 60f, NavBarH - 6f), "← Back", EditorStyles.miniButton))
            {
                _propPath = _pathStack.Pop(); _scroll = _scrollStack.Pop();
                _lastBuiltPath = null; editorWindow?.Repaint();
            }
            GUI.Label(new Rect(70f, y + 4f, rect.width - 74f, NavBarH - 8f),
                $"Level {_pathStack.Count + 1}", EditorStyles.centeredGreyMiniLabel);
            y += NavBarH;
        }

        // ── ARRAY TABLE: variable-height rows, nested arrays inline ───────────
        private void DrawArrayTable(SerializedProperty prop, Rect rect, ref float y)
        {
            bool structElems = _colNames.Count > 0;

            // Fixed column header
            EditorGUI.DrawRect(new Rect(0, y, rect.width, RowH), new Color(0.22f, 0.22f, 0.22f));
            float hx = 0f;
            GUI.Label(new Rect(hx + 2f, y + 2f, IndexColW - 4f, RowH - 4f),
                "#", EditorStyles.centeredGreyMiniLabel);
            hx += IndexColW;

            if (structElems)
            {
                for (int c = 0; c < _colNames.Count; c++)
                {
                    GUI.Label(new Rect(hx + 3f, y + 2f, _colWidths[c] - 6f, RowH - 4f),
                        ObjectNames.NicifyVariableName(_colNames[c]), EditorStyles.miniLabel);
                    Rect rh = new Rect(hx + _colWidths[c] - 3f, y, 6f, RowH);
                    EditorGUIUtility.AddCursorRect(rh, MouseCursor.ResizeHorizontal);
                    HandleColResize(c, rh);
                    hx += _colWidths[c];
                }
            }
            else
            {
                GUI.Label(new Rect(hx + 3f, y + 2f, rect.width - hx - DelBtnW - 6f, RowH - 4f),
                    "Value", EditorStyles.miniLabel);
            }
            y += RowH;

            // Total content height (variable per row)
            float totalH = 0f;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var elem = prop.GetArrayElementAtIndex(i);
                totalH += structElems ? GetStructRowH(elem) : RowH;
            }
            totalH += RowH + 4f; // add-element button

            float scrollH = rect.height - y;
            float vw      = rect.width - 14f;
            _scroll = GUI.BeginScrollView(new Rect(0, y, rect.width, scrollH), _scroll,
                new Rect(0, 0, vw, Mathf.Max(totalH, scrollH)));

            float ry = 0f;
            int toDelete = -1;

            for (int i = 0; i < prop.arraySize; i++)
            {
                var   elem  = prop.GetArrayElementAtIndex(i);
                float rowH  = structElems ? GetStructRowH(elem) : RowH;

                EditorGUI.DrawRect(new Rect(0, ry, vw, rowH),
                    i % 2 == 0 ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.20f, 0.20f, 0.20f));

                GUI.Label(new Rect(2f, ry + 2f, IndexColW - 4f, RowH - 4f),
                    i.ToString(), EditorStyles.centeredGreyMiniLabel);

                if (structElems)
                {
                    float cx = IndexColW;
                    for (int c = 0; c < _colNames.Count; c++)
                    {
                        var cell = elem.FindPropertyRelative(_colNames[c]);
                        if (cell != null)
                        {
                            float ch = GetCellH(cell);
                            DrawCellInplace(new Rect(cx + 1f, ry + 1f, _colWidths[c] - 2f, ch - 2f), cell);
                        }
                        cx += _colWidths[c];
                    }
                }
                else
                {
                    DrawCellInplace(new Rect(IndexColW + 1f, ry + 1f,
                        vw - IndexColW - DelBtnW - 2f, RowH - 2f), elem);
                }

                if (GUI.Button(new Rect(vw - DelBtnW - 1f, ry + 2f, DelBtnW - 2f, RowH - 4f),
                    "−", EditorStyles.miniButton))
                    toDelete = i;

                ry += rowH;
            }

            if (toDelete >= 0)
            {
                prop.DeleteArrayElementAtIndex(toDelete);
                _so.ApplyModifiedProperties();
            }

            // Add element
            if (GUI.Button(new Rect(2f, ry + 2f, vw - 4f, RowH - 4f),
                "+ Add Element", EditorStyles.miniButton))
            {
                prop.arraySize++;
                _lastBuiltPath = null;
                RebuildColumns(prop);
                _so.ApplyModifiedProperties();
            }

            GUI.EndScrollView();
        }

        // ── STRUCT ROWS: "Field : [control]", nested arrays expand inline ─────
        private void DrawStructRows(SerializedProperty prop, Rect rect, ref float y)
        {
            float totalH = 0f;
            var counter = prop.Copy(); var cEnd = prop.GetEndProperty();
            if (counter.NextVisible(true))
                while (!SerializedProperty.EqualContents(counter, cEnd))
                { totalH += GetCellH(counter); counter.NextVisible(false); }

            float scrollH  = rect.height - y;
            float vw       = rect.width - 14f;
            float labelW   = vw * LabelRatio;

            _scroll = GUI.BeginScrollView(new Rect(0, y, rect.width, scrollH), _scroll,
                new Rect(0, 0, vw, Mathf.Max(totalH, scrollH)));

            float ry = 0f; int row = 0;
            var child = prop.Copy(); var end = prop.GetEndProperty();
            if (child.NextVisible(true))
            {
                while (!SerializedProperty.EqualContents(child, end))
                {
                    float fh = GetCellH(child);
                    EditorGUI.DrawRect(new Rect(0, ry, vw, fh),
                        row % 2 == 0 ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.20f, 0.20f, 0.20f));

                    GUI.Label(new Rect(4f, ry + 2f, labelW - 6f, RowH - 4f),
                        ObjectNames.NicifyVariableName(child.name), EditorStyles.miniLabel);

                    DrawCellInplace(new Rect(labelW + 2f, ry + 1f, vw - labelW - 4f, fh - 2f), child.Copy());

                    ry += fh; row++;
                    child.NextVisible(false);
                }
            }

            GUI.EndScrollView();
        }

        // ── Height calculation ────────────────────────────────────────────────
        // How tall does the row for a struct element need to be?
        private float GetStructRowH(SerializedProperty structElem)
        {
            float maxH = RowH;
            foreach (var col in _colNames)
            {
                var cell = structElem.FindPropertyRelative(col);
                if (cell != null) maxH = Mathf.Max(maxH, GetCellH(cell));
            }
            return maxH;
        }

        // How tall does a single cell (property) need to be when drawn inline?
        private float GetCellH(SerializedProperty p)
        {
            if (p.propertyType != SerializedPropertyType.Generic) return RowH;
            if (p.isArray)
                // mini-header row + one row per element + add-button row
                return RowH + p.arraySize * RowH + RowH;
            // nested struct: one row per visible child
            int n = 0;
            var c = p.Copy(); var e = p.GetEndProperty();
            if (c.NextVisible(true))
                while (!SerializedProperty.EqualContents(c, e)) { n++; c.NextVisible(false); }
            return Mathf.Max(RowH, n * RowH);
        }

        // ── Cell drawing: inline for Generic, primitive controls otherwise ────
        private void DrawCellInplace(Rect r, SerializedProperty p)
        {
            if (p.propertyType == SerializedPropertyType.Generic)
            {
                if (p.isArray) DrawNestedArrayInCell(r, p);
                else           DrawNestedStructInCell(r, p);
                return;
            }
            DrawPrimitive(r, p);
        }

        // Nested array: mini-table (header + rows + add btn) inside cell rect
        private void DrawNestedArrayInCell(Rect r, SerializedProperty arr)
        {
            // Collect sub-column names if elements are structs
            var subCols = new List<string>();
            if (arr.arraySize > 0)
            {
                var e0 = arr.GetArrayElementAtIndex(0);
                if (e0.propertyType == SerializedPropertyType.Generic && !e0.isArray)
                {
                    var sc = e0.Copy(); var se = e0.GetEndProperty();
                    if (sc.NextVisible(true))
                        while (!SerializedProperty.EqualContents(sc, se))
                        { subCols.Add(sc.name); sc.NextVisible(false); }
                }
            }

            float subW = subCols.Count > 0
                ? (r.width - DelBtnW) / subCols.Count
                : r.width - DelBtnW;

            // Mini header
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, RowH), new Color(0.28f, 0.28f, 0.32f));
            if (subCols.Count > 0)
            {
                float hx = r.x;
                foreach (var col in subCols)
                {
                    GUI.Label(new Rect(hx + 2f, r.y + 2f, subW - 4f, RowH - 4f),
                        ObjectNames.NicifyVariableName(col), EditorStyles.miniLabel);
                    hx += subW;
                }
            }
            else
            {
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width - DelBtnW - 4f, RowH - 4f),
                    "Value", EditorStyles.miniLabel);
            }

            float ey = r.y + RowH;
            int delIdx = -1;

            for (int i = 0; i < arr.arraySize; i++)
            {
                var elem = arr.GetArrayElementAtIndex(i);
                EditorGUI.DrawRect(new Rect(r.x, ey, r.width, RowH),
                    i % 2 == 0 ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.24f, 0.24f, 0.24f));

                if (subCols.Count > 0)
                {
                    float ex = r.x;
                    foreach (var col in subCols)
                    {
                        var cp = elem.FindPropertyRelative(col);
                        if (cp != null)
                            DrawPrimitive(new Rect(ex + 1f, ey + 1f, subW - 2f, RowH - 2f), cp);
                        ex += subW;
                    }
                }
                else
                {
                    DrawPrimitive(new Rect(r.x + 1f, ey + 1f,
                        r.width - DelBtnW - 2f, RowH - 2f), elem);
                }

                if (GUI.Button(new Rect(r.xMax - DelBtnW + 1f, ey + 2f, DelBtnW - 3f, RowH - 4f),
                    "−", EditorStyles.miniButton))
                    delIdx = i;

                ey += RowH;
            }

            if (delIdx >= 0) { arr.DeleteArrayElementAtIndex(delIdx); _so.ApplyModifiedProperties(); }

            // Add button
            if (GUI.Button(new Rect(r.x + 1f, ey + 1f, r.width - 2f, RowH - 2f),
                "+", EditorStyles.miniButton))
            { arr.arraySize++; _so.ApplyModifiedProperties(); }
        }

        // Nested struct: stacked "label : control" rows inside cell rect
        private void DrawNestedStructInCell(Rect r, SerializedProperty p)
        {
            float labelW = r.width * 0.45f;
            float cy = r.y;
            var child = p.Copy(); var end = p.GetEndProperty();
            if (child.NextVisible(true))
            {
                while (!SerializedProperty.EqualContents(child, end))
                {
                    GUI.Label(new Rect(r.x + 2f, cy + 2f, labelW - 4f, RowH - 4f),
                        ObjectNames.NicifyVariableName(child.name), EditorStyles.miniLabel);
                    DrawPrimitive(new Rect(r.x + labelW, cy + 1f,
                        r.width - labelW - 1f, RowH - 2f), child.Copy());
                    cy += RowH;
                    child.NextVisible(false);
                }
            }
        }

        // Primitive field control (no Generic handling — use DrawCellInplace for that)
        private void DrawPrimitive(Rect r, SerializedProperty p)
        {
            // Safety fallback for any remaining generics (3+ levels deep)
            if (p.propertyType == SerializedPropertyType.Generic)
            {
                if (GUI.Button(r, p.isArray ? $"[{p.arraySize}] Edit →" : "Edit →", EditorStyles.miniButton))
                    NavigateTo(p.propertyPath);
                return;
            }
            EditorGUI.BeginChangeCheck();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    p.intValue = EditorGUI.IntField(r, p.intValue); break;
                case SerializedPropertyType.Float:
                    p.floatValue = EditorGUI.FloatField(r, p.floatValue); break;
                case SerializedPropertyType.Boolean:
                    p.boolValue = EditorGUI.Toggle(new Rect(r.x + 2f, r.y, 14f, r.height), p.boolValue); break;
                case SerializedPropertyType.String:
                    p.stringValue = EditorGUI.TextField(r, p.stringValue); break;
                case SerializedPropertyType.Enum:
                    p.enumValueIndex = EditorGUI.Popup(r, p.enumValueIndex, p.enumDisplayNames); break;
                case SerializedPropertyType.Color:
                    p.colorValue = EditorGUI.ColorField(r, p.colorValue); break;
                case SerializedPropertyType.ObjectReference:
                    p.objectReferenceValue = EditorGUI.ObjectField(r, p.objectReferenceValue,
                        typeof(UnityEngine.Object), false); break;
                case SerializedPropertyType.Vector2:
                    p.vector2Value = EditorGUI.Vector2Field(r, GUIContent.none, p.vector2Value); break;
                case SerializedPropertyType.Vector3:
                    p.vector3Value = EditorGUI.Vector3Field(r, GUIContent.none, p.vector3Value); break;
                case SerializedPropertyType.Vector2Int:
                    p.vector2IntValue = EditorGUI.Vector2IntField(r, GUIContent.none, p.vector2IntValue); break;
                case SerializedPropertyType.Vector3Int:
                    p.vector3IntValue = EditorGUI.Vector3IntField(r, GUIContent.none, p.vector3IntValue); break;
                default:
                    EditorGUI.PropertyField(r, p, GUIContent.none, false); break;
            }
            if (EditorGUI.EndChangeCheck())
                _so.ApplyModifiedProperties();
        }

        // ── Column resize ─────────────────────────────────────────────────────
        private void HandleColResize(int col, Rect handle)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e  = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (handle.Contains(e.mousePosition))
                    { _resizingCol = col; _resizeStartX = e.mousePosition.x; _resizeStartW = _colWidths[col]; GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (_resizingCol == col && GUIUtility.hotControl == id)
                    { _colWidths[col] = Mathf.Max(50f, _resizeStartW + e.mousePosition.x - _resizeStartX); editorWindow?.Repaint(); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    { _resizingCol = -1; GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }
    }


    /// <summary>
    /// Searchable popup for picking a ScriptableObject type to open as a tab.
    /// Opened by the "+" button in the tab bar.
    /// </summary>
    internal sealed class FDataTableTypePickerPopup : PopupWindowContent
    {
        private readonly FDataTableWindow _window;
        private readonly List<Type>       _types;
        private readonly List<string>     _typeNames;

        private string      _search   = string.Empty;
        private Vector2     _scroll;
        private List<int>   _filtered = new List<int>();
        private bool        _focusDone;

        private const string SearchControlName = "FDataTableTypeSearch";

        public FDataTableTypePickerPopup(FDataTableWindow window, List<Type> types, List<string> typeNames)
        {
            _window    = window;
            _types     = types;
            _typeNames = typeNames;
            RebuildFilter();
        }

        public override Vector2 GetWindowSize() => new Vector2(260f, 320f);

        public override void OnOpen()
        {
            _focusDone = false;
        }

        public override void OnGUI(Rect rect)
        {
            // Auto-focus the search field once on open
            if (!_focusDone)
            {
                GUI.FocusControl(SearchControlName);
                _focusDone = true;
            }

            EditorGUILayout.LabelField("Open Type as Tab", EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);

            // Search bar
            GUI.SetNextControlName(SearchControlName);
            EditorGUI.BeginChangeCheck();
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
                RebuildFilter();

            EditorGUILayout.Space(2f);

            // Filtered type list
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUIStyle btnStyle = new GUIStyle(EditorStyles.toolbarButton)
            {
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(6, 4, 2, 2)
            };

            if (_filtered.Count == 0)
            {
                GUILayout.Label("No types found.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (int i in _filtered)
                {
                    if (GUILayout.Button(_typeNames[i], btnStyle))
                    {
                        _window.OpenTab(_types[i]);
                        _window.SaveTabPrefs();
                        editorWindow.Close();
                        break;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            // Close on Escape
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                editorWindow.Close();
                Event.current.Use();
            }
        }

        private void RebuildFilter()
        {
            _filtered.Clear();
            for (int i = 0; i < _typeNames.Count; i++)
            {
                if (string.IsNullOrEmpty(_search) ||
                    _typeNames[i].IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filtered.Add(i);
                }
            }
        }
    }
}
