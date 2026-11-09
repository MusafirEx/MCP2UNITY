using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace WasimDevelopment.UnityMcpBridge
{
    // Dedicated, allowlisted layout operation applied through ChangeSets only.
    internal static class UiLayoutTool
    {
        public static JObject Propose(JObject args)
        {
            GameObject go = ObjectAccess.SelectedOrId(args);
            var op = new JObject {
                ["kind"] = "set_ui_layout",
                ["targetId"] = ObjectAccess.Id(go),
                ["layoutType"] = args["layoutType"]?.DeepClone()
            };
            foreach (string field in new[] { "spacing", "padding", "cellSize", "constraint",
                "constraintCount", "childControlWidth", "childControlHeight",
                "childForceExpandWidth", "childForceExpandHeight" })
                if (args[field] != null) op[field] = args[field].DeepClone();
            Validate(go, op);
            return ChangeSets.Propose(new JObject {
                ["summary"] = (string)args["summary"] ?? "Configure uGUI layout of " + go.name,
                ["operations"] = new JArray(op)
            });
        }

        public static void Validate(GameObject go, JObject op)
        {
            if (go == null || !(go.transform is RectTransform) || go.GetComponentInParent<Canvas>() == null)
                throw new ArgumentException("Layout target must be a RectTransform in a Canvas.");
            string type = (string)op["layoutType"];
            if (type != "horizontal" && type != "vertical" && type != "grid")
                throw new ArgumentException("layoutType must be horizontal, vertical or grid.");
            LayoutGroup existing = go.GetComponent<LayoutGroup>();
            if (existing != null && !MatchesType(type, existing))
                throw new ArgumentException("Another LayoutGroup is already attached; replacing it is not supported.");

            if (op["spacing"] != null) Number(op["spacing"], "spacing", 0, 1024);
            if (op["padding"] != null)
            {
                if (!(op["padding"] is JObject padding) || padding.Count != 4)
                    throw new ArgumentException("padding requires left/right/top/bottom.");
                foreach (string key in new[] { "left", "right", "top", "bottom" })
                    Integer(padding[key], "padding." + key, 0, 1024);
            }
            if (op["cellSize"] != null)
            {
                if (type != "grid") throw new ArgumentException("cellSize requires grid layout.");
                if (!(op["cellSize"] is JObject size) || size.Count != 2)
                    throw new ArgumentException("cellSize requires x and y.");
                Number(size["x"], "cellSize.x", 1, 8192);
                Number(size["y"], "cellSize.y", 1, 8192);
            }
            if (op["constraint"] != null)
            {
                if (type != "grid" || (string)op["constraint"] != "flexible"
                    && (string)op["constraint"] != "fixedColumnCount"
                    && (string)op["constraint"] != "fixedRowCount")
                    throw new ArgumentException("constraint must be a grid option.");
            }
            if (op["constraintCount"] != null)
            {
                if (type != "grid") throw new ArgumentException("constraintCount requires grid layout.");
                Integer(op["constraintCount"], "constraintCount", 1, 1000);
            }
            foreach (string key in new[] { "childControlWidth", "childControlHeight",
                "childForceExpandWidth", "childForceExpandHeight" })
                if (op[key] != null && (type == "grid" || op[key].Type != JTokenType.Boolean))
                    throw new ArgumentException(key + " is a boolean and requires horizontal/vertical layout.");
        }

        public static void Apply(GameObject go, JObject op)
        {
            Validate(go, op);
            string type = (string)op["layoutType"];
            LayoutGroup layout = go.GetComponent<LayoutGroup>();
            if (layout == null)
            {
                switch (type)
                {
                    case "horizontal": layout = Undo.AddComponent<HorizontalLayoutGroup>(go); break;
                    case "vertical": layout = Undo.AddComponent<VerticalLayoutGroup>(go); break;
                    case "grid": layout = Undo.AddComponent<GridLayoutGroup>(go); break;
                }
            }
            if (layout == null) throw new InvalidOperationException("Unity refused to create LayoutGroup.");

            Undo.RecordObject(layout, "Configure uGUI Layout");
            if (op["padding"] is JObject padding)
                layout.padding = new RectOffset((int)padding["left"], (int)padding["right"],
                    (int)padding["top"], (int)padding["bottom"]);
            if (layout is HorizontalOrVerticalLayoutGroup hv)
            {
                if (op["spacing"] != null) hv.spacing = op["spacing"].Value<float>();
                if (op["childControlWidth"] != null) hv.childControlWidth = op["childControlWidth"].Value<bool>();
                if (op["childControlHeight"] != null) hv.childControlHeight = op["childControlHeight"].Value<bool>();
                if (op["childForceExpandWidth"] != null) hv.childForceExpandWidth = op["childForceExpandWidth"].Value<bool>();
                if (op["childForceExpandHeight"] != null) hv.childForceExpandHeight = op["childForceExpandHeight"].Value<bool>();
            }
            else if (layout is GridLayoutGroup grid)
            {
                if (op["spacing"] != null)
                    grid.spacing = new Vector2(op["spacing"].Value<float>(), op["spacing"].Value<float>());
                if (op["cellSize"] is JObject size)
                    grid.cellSize = new Vector2(size["x"].Value<float>(), size["y"].Value<float>());
                if (op["constraint"] != null)
                    grid.constraint = (string)op["constraint"] == "fixedRowCount"
                        ? GridLayoutGroup.Constraint.FixedRowCount
                        : (string)op["constraint"] == "fixedColumnCount"
                            ? GridLayoutGroup.Constraint.FixedColumnCount
                            : GridLayoutGroup.Constraint.Flexible;
                if (op["constraintCount"] != null) grid.constraintCount = op["constraintCount"].Value<int>();
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static bool MatchesType(string type, LayoutGroup group)
            => type == "horizontal" && group is HorizontalLayoutGroup
                || type == "vertical" && group is VerticalLayoutGroup
                || type == "grid" && group is GridLayoutGroup;

        private static void Number(JToken token, string name, double min, double max)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                throw new ArgumentException(name + " must be numeric.");
            double number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || number < min || number > max)
                throw new ArgumentException(name + " must be finite in range [" + min + ", " + max + "].");
        }

        private static void Integer(JToken token, string name, long min, long max)
        {
            if (token == null || token.Type != JTokenType.Integer)
                throw new ArgumentException(name + " must be an integer.");
            long number = token.Value<long>();
            if (number < min || number > max) throw new ArgumentException(name + " outside allowed range.");
        }
    }
}
