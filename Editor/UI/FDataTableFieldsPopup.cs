using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FDataTable.Editor
{
    /// <summary>
    /// Small popup shown when the user clicks "Fields..." in the per-tab settings bar.
    /// Single checkbox per field: checked = visible, unchecked = hidden.
    /// Default (no config saved): all checked.
    /// </summary>
    internal class FDataTableFieldsPopup : PopupWindowContent
    {
        private const float PopupW  = 250f;
        private const float RowH    = 20f;
        private const float HeaderH = 22f;
        private const float FooterH = 28f;
        private const float Padding = 6f;

        private readonly FDataTableSettings   _settings;
        private readonly FDataTableTypeConfig _config;
        private readonly List<FieldWrapper>   _fields = new List<FieldWrapper>();
        private readonly Action               _onApply;
        private Vector2 _scroll;

        private class FieldWrapper
        {
            public string Name;
            public string DisplayName;
            public bool   Visible;
        }

        public FDataTableFieldsPopup(FDataTableSettings settings, Type type, Action onApply)
        {
            _settings = settings;
            _config   = settings.GetOrAddConfig(type);
            _onApply  = onApply;

            var allFields = FDataTableFieldResolver.GetDisplayableFields(type, null);
            bool hasWhitelist = _config.includedFields.Count > 0;

            foreach (var f in allFields)
            {
                _fields.Add(new FieldWrapper
                {
                    Name        = f.Name,
                    DisplayName = ObjectNames.NicifyVariableName(f.Name),
                    // No whitelist saved yet -> all visible; otherwise only those in the list
                    Visible     = !hasWhitelist || _config.includedFields.Contains(f.Name),
                });
            }
        }

        public override Vector2 GetWindowSize()
        {
            float listH = Mathf.Min(_fields.Count * RowH, 280f);
            return new Vector2(PopupW, HeaderH + RowH + Padding + listH + Padding + FooterH);
        }

        public override void OnGUI(Rect rect)
        {
            // ── Header row (column labels) ────────────────────────────────
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), new Color(0.18f, 0.18f, 0.18f));
            GUI.Label(new Rect(rect.x + Padding, rect.y + 3f, 160f, HeaderH - 4f), "Field", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(rect.xMax - 50f,  rect.y + 3f,  44f, HeaderH - 4f), "Show",  EditorStyles.miniBoldLabel);

            // ── "All" toggle row ──────────────────────────────────────────
            float allRowY = rect.y + HeaderH;
            EditorGUI.DrawRect(new Rect(rect.x, allRowY, rect.width, RowH), new Color(0.24f, 0.24f, 0.26f));
            GUI.Label(new Rect(rect.x + Padding, allRowY + 2f, 160f, RowH - 2f), "All", EditorStyles.miniBoldLabel);

            bool allChecked = true;
            foreach (var fw in _fields) if (!fw.Visible) { allChecked = false; break; }

            bool newAll = EditorGUI.Toggle(
                new Rect(rect.xMax - 14f - 20f, allRowY + 2f, 16f, 16f), allChecked);
            if (newAll != allChecked)
                foreach (var fw in _fields) fw.Visible = newAll;

            // ── Field list ────────────────────────────────────────────────
            float listY = rect.y + HeaderH + RowH + Padding;
            float listH = rect.height - HeaderH - RowH - Padding - FooterH - Padding;

            Rect scrollView  = new Rect(rect.x, listY, rect.width, listH);
            Rect contentRect = new Rect(0, 0, rect.width - 14f, _fields.Count * RowH);
            _scroll = GUI.BeginScrollView(scrollView, _scroll, contentRect);

            for (int i = 0; i < _fields.Count; i++)
            {
                var   fw = _fields[i];
                float ry = i * RowH;

                EditorGUI.DrawRect(new Rect(0, ry, contentRect.width, RowH),
                    i % 2 == 0 ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.22f, 0.22f, 0.22f));

                GUI.Label(new Rect(Padding, ry + 2f, contentRect.width - 28f, RowH - 2f),
                    fw.DisplayName, EditorStyles.miniLabel);

                fw.Visible = EditorGUI.Toggle(
                    new Rect(contentRect.width - 20f, ry + 2f, 16f, 16f), fw.Visible);
            }

            GUI.EndScrollView();

            // ── Footer ────────────────────────────────────────────────────
            float footerY = rect.y + rect.height - FooterH;
            EditorGUI.DrawRect(new Rect(rect.x, footerY, rect.width, FooterH), new Color(0.15f, 0.15f, 0.15f));

            if (GUI.Button(new Rect(rect.x + Padding, footerY + 4f, rect.width - Padding * 2f, FooterH - 8f), "Apply"))
            {
                Save();
                editorWindow.Close();
            }
        }

        // Auto-save when clicking outside the popup
        public override void OnClose() => Save();

        private void Save()
        {
            _config.includedFields.Clear();
            _config.excludedFields.Clear();

            bool allVisible = true;
            foreach (var fw in _fields)
                if (!fw.Visible) { allVisible = false; break; }

            // All shown -> empty whitelist (resolver shows everything)
            // Some hidden -> store only the visible ones as whitelist
            if (!allVisible)
                foreach (var fw in _fields)
                    if (fw.Visible) _config.includedFields.Add(fw.Name);

            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            _onApply?.Invoke();
        }
    }
}
