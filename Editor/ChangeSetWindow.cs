using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WasimDevelopment.UnityMcpBridge
{
    internal sealed class ChangeSetWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _completed;
        private string _message = "";
        private GUIStyle _wrappedReviewTextArea;
        private readonly Dictionary<string, Vector2> _reviewScroll = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        private readonly HashSet<string> _expandedReviews = new HashSet<string>(StringComparer.Ordinal);
        private const float ReviewButtonWidth = 92f;
        private const float ReviewDefaultHeight = 210f;
        private const float ReviewExpandedHeight = 400f;

        private void OnEnable() => minSize = new Vector2(280f, 300f);

        [MenuItem("Window/Wasim Development/Change Sets and Actions")]
        public static void Open() => GetWindow<ChangeSetWindow>("Wasim MCP Reviews");
        private void OnGUI()
        {
            if (_wrappedReviewTextArea == null)
                _wrappedReviewTextArea = new GUIStyle(EditorStyles.textArea) { wordWrap = true, richText = false };
            EditorGUILayout.LabelField("Change sets and Editor actions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Approve only after reviewing every operation. Change sets save affected scenes and retain guarded file backups. Test/Play actions execute project code. No remote approve endpoint exists.", MessageType.Info);
            _completed = EditorGUILayout.Toggle("Show completed change sets", _completed);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                foreach (JObject item in ChangeSets.List(_completed, true)) Draw(item, false);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Editor actions", EditorStyles.boldLabel);
                foreach (JObject item in EditorActions.List()) if (_completed || (string)item["status"] == "Pending") Draw(item, true);
            }
            catch (Exception ex) { EditorGUILayout.HelpBox(ex.GetBaseException().Message, MessageType.Error); }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            if (GUILayout.Button("Refresh")) Repaint();
        }
        private void Draw(JObject item, bool action)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string id = (string)item["id"], status = (string)item["status"];
            EditorGUILayout.LabelField(id + " — " + status, EditorStyles.boldLabel);
            EditorGUILayout.LabelField((string)item["summary"] ?? "", EditorStyles.wordWrappedLabel);
            string preview = (action ? item : item["operations"])?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "";
            // Each review gets its own vertical scroll area. A MaxHeight on a
            // word-wrapped TextArea clipped long JSON with no reliable way to
            // reach the bottom (especially in a narrow docked Editor window).
            // Keep full text read-only, selectable and copyable.
            DrawReviewText((action ? "action:" : "change:") + id, preview);
            if (GUILayout.Button("Copy complete review JSON"))
                EditorGUIUtility.systemCopyBuffer = item.ToString(Newtonsoft.Json.Formatting.Indented);
            if (item["scenes"] != null) EditorGUILayout.LabelField("Scenes: " + item["scenes"].ToString(Newtonsoft.Json.Formatting.None), EditorStyles.wordWrappedLabel);
            if (status == "Pending")
            {
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
                {
                    if (GUILayout.Button("Approve", GUILayout.Width(ReviewButtonWidth)))
                        Perform(() => { if (action) EditorActions.Approve(id); else ChangeSets.Approve(id); }, "Approved " + id);
                    if (GUILayout.Button("Reject", GUILayout.Width(ReviewButtonWidth)))
                        Perform(() => { if (action) EditorActions.Reject(id); else ChangeSets.Reject(id); }, "Rejected " + id);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            if (!action && status == "Applied" && (string)item["kind"] == "Changes" && GUILayout.Button("Prepare rollback"))
                Perform(() => ChangeSets.ProposeRollback(id, "Restore state before " + id), "Rollback proposed; review it before approval.");
            if (item["message"] != null) EditorGUILayout.LabelField((string)item["message"] ?? "", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }
        private void DrawReviewText(string key, string preview)
        {
            bool expanded = _expandedReviews.Contains(key);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Review JSON (scroll to read)", EditorStyles.miniLabel);
            if (GUILayout.Button(expanded ? "Compact" : "Expand", GUILayout.Width(72f)))
            {
                if (expanded) _expandedReviews.Remove(key);
                else _expandedReviews.Add(key);
                expanded = !expanded;
            }
            EditorGUILayout.EndHorizontal();

            // Reserve space for the help-box margins and both vertical
            // scrollbars. Avoid the horizontal scrollbar that used to hide
            // the far-right side of long lines.
            float textWidth = Mathf.Max(160f, position.width - 76f);
            float textHeight = Mathf.Max(60f,
                _wrappedReviewTextArea.CalcHeight(new GUIContent(preview), textWidth) + 8f);
            Vector2 previous = _reviewScroll.TryGetValue(key, out Vector2 value)
                ? value : Vector2.zero;
            Vector2 next = EditorGUILayout.BeginScrollView(previous, false, true,
                GUILayout.Height(expanded ? ReviewExpandedHeight : ReviewDefaultHeight),
                GUILayout.ExpandWidth(true));
            EditorGUILayout.SelectableLabel(preview, _wrappedReviewTextArea,
                GUILayout.Width(textWidth), GUILayout.Height(textHeight));
            EditorGUILayout.EndScrollView();
            _reviewScroll[key] = next;
        }

        private void Perform(Action action, string success)
        {
            try { action(); _message = success; }
            catch (Exception ex) { _message = ex.GetBaseException().Message; }
        }
    }
}
