using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WasimDevelopment.UnityMcpBridge
{
    // Scene/UI writes are performed ONLY inside an approved ChangeSets.ApplyOperation.
    // The public MCP handlers below only build proposals.
    internal static class UiChangeTool
    {
        private static readonly HashSet<string> UiTypes = new HashSet<string>(StringComparer.Ordinal) {
            "canvas", "panel", "image", "button", "text", "tmpText",
            "slider", "toggle", "inputField", "scrollView"
        };

        public static JObject ProposeCanvas(JObject args)
        {
            var op = new JObject {
                ["kind"] = "create_ui",
                ["uiType"] = "canvas",
                ["name"] = (string)args["name"] ?? "Canvas",
                ["createEventSystem"] = args["createEventSystem"]?.Value<bool>() ?? true,
                ["alias"] = "$createdCanvas"
            };
            if (args["scenePath"] != null) op["scenePath"] = args["scenePath"].DeepClone();
            if (args["referenceResolution"] != null) op["referenceResolution"] = args["referenceResolution"].DeepClone();
            string summary = (string)args["summary"];
            if (string.IsNullOrWhiteSpace(summary)) summary = "Create Canvas with CanvasScaler and GraphicRaycaster";
            return ChangeSets.Propose(new JObject { ["summary"] = summary, ["operations"] = new JArray(op) });
        }

        public static JObject ProposeElement(JObject args)
        {
            var op = new JObject {
                ["kind"] = "create_ui",
                ["uiType"] = args["uiType"]?.DeepClone(),
                ["name"] = args["name"]?.DeepClone(),
                ["parentId"] = args["parentId"]?.DeepClone(),
                ["alias"] = "$createdUi"
            };
            foreach (string field in new[] { "scenePath", "text", "fontSize", "color", "width", "height", "anchoredPosition",
                "minValue", "maxValue", "value", "wholeNumbers", "isOn", "placeholder", "characterLimit",
                "horizontal", "vertical", "contentHeight" })
                if (args[field] != null) op[field] = args[field].DeepClone();
            string summary = (string)args["summary"];
            if (string.IsNullOrWhiteSpace(summary))
                summary = "Create uGUI " + (string)args["uiType"] + ": " + (string)args["name"];
            return ChangeSets.Propose(new JObject { ["summary"] = summary, ["operations"] = new JArray(op) });
        }

        public static void ValidateCreate(JObject op)
        {
            string kind = (string)op["uiType"];
            if (kind == null || !UiTypes.Contains(kind))
                throw new ArgumentException("Unsupported uGUI type. Use canvas, panel, image, button, text, tmpText, slider, toggle, inputField, scrollView.");
            if (kind == "canvas" && op["parentId"] != null)
                throw new ArgumentException("Canvas must be created as a scene root in this version.");
            if (kind != "canvas" && string.IsNullOrWhiteSpace((string)op["parentId"]))
                throw new ArgumentException("UI elements need a parentId belonging to a Canvas hierarchy.");
            if (op["text"] != null && (op["text"].Type != JTokenType.String ||
                ((string)op["text"]).Length > 8192))
                throw new ArgumentException("Text must be a string of at most 8192 characters.");
            if (op["createEventSystem"] != null &&
                (kind != "canvas" || op["createEventSystem"].Type != JTokenType.Boolean))
                throw new ArgumentException("createEventSystem only accepts a boolean on a Canvas.");
            ValidateScalar(op, "width", 1, 8192);
            ValidateScalar(op, "height", 1, 8192);
            ValidateScalar(op, "fontSize", 1, 256);
            ValidateVector(op, "referenceResolution", 1, 16384);
            ValidateVector(op, "anchoredPosition", -100000, 100000);
            if (op["color"] != null)
            {
                if (!(op["color"] is JObject color) || color.Count != 4)
                    throw new ArgumentException("color must contain r, g, b, a.");
                foreach (string axis in new[] { "r", "g", "b", "a" }) Number(color, axis, 0, 1);
            }
            UiAdvancedTool.Validate(op);
        }

        private static float Number(JObject obj, string name, float min, float max)
        {
            JToken token = obj[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new ArgumentException(name + " must be a number.");
            double val = token.Value<double>();
            if (double.IsNaN(val) || double.IsInfinity(val) || val < min || val > max)
                throw new ArgumentException(name + " must be finite in [" + min + ", " + max + "].");
            return (float)val;
        }

        private static void ValidateScalar(JObject op, string key, float min, float max)
        {
            if (op[key] != null) Number(op, key, min, max);
        }

        private static void ValidateVector(JObject op, string key, float min, float max)
        {
            if (op[key] == null) return;
            if (!(op[key] is JObject obj) || obj.Count != 2)
                throw new ArgumentException(key + " must contain exactly x and y.");
            Number(obj, "x", min, max);
            Number(obj, "y", min, max);
        }

        // Called only after validation, scene hash guards and local approval.
        public static GameObject ApplyCreate(JObject op, IDictionary<string, UnityEngine.Object> aliases)
        {
            string kind = (string)op["uiType"];
            Scene scene = SceneManager.GetSceneByPath((string)op["scenePath"]);
            var go = new GameObject((string)op["name"], typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create uGUI " + kind);
            SceneManager.MoveGameObjectToScene(go, scene);
            RectTransform rect = (RectTransform)go.transform;

            string parentId = (string)op["parentId"];
            if (parentId != null)
            {
                GameObject parent = ObjectAccess.GameObjectOf(ObjectAccess.Resolve(parentId, aliases));
                if (parent == null || !(parent.transform is RectTransform) ||
                    parent.GetComponentInParent<Canvas>() == null)
                    throw new InvalidOperationException("UI parent is no longer part of a Canvas hierarchy.");
                Undo.SetTransformParent(rect, parent.transform, "Parent uGUI element");
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                rect.localPosition = Vector3.zero;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(
                    op["width"] == null ? 200 : op["width"].Value<float>(),
                    op["height"] == null ? 80 : op["height"].Value<float>());
                if (op["anchoredPosition"] is JObject pos)
                    rect.anchoredPosition = new Vector2(pos["x"].Value<float>(), pos["y"].Value<float>());
            }

            switch (kind)
            {
                case "canvas":
                    Canvas canvas = Undo.AddComponent<Canvas>(go);
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(go);
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = op["referenceResolution"] is JObject res
                        ? new Vector2(res["x"].Value<float>(), res["y"].Value<float>())
                        : new Vector2(1920, 1080);
                    Undo.AddComponent<GraphicRaycaster>(go);
                    // A Canvas is created in a specific scene. Checking all loaded
                    // scenes can skip this scene's EventSystem when another scene
                    // (including the user's open scene) already contains one.
                    if ((op["createEventSystem"]?.Value<bool>() ?? true) &&
                        !scene.GetRootGameObjects().Any(root =>
                            root.GetComponentInChildren<EventSystem>(true) != null))
                    {
                        var events = new GameObject("EventSystem");
                        Undo.RegisterCreatedObjectUndo(events, "Create UI EventSystem");
                        SceneManager.MoveGameObjectToScene(events, scene);
                        Undo.AddComponent<EventSystem>(events);
                        Undo.AddComponent<StandaloneInputModule>(events);
                    }
                    break;
                case "panel":
                case "image":
                    Image picture = Undo.AddComponent<Image>(go);
                    picture.color = ReadColor(op, kind == "panel"
                        ? new Color(0.12f, 0.14f, 0.18f, 0.90f) : Color.white);
                    if (kind == "panel" && op["width"] == null && op["height"] == null)
                    {
                        rect.anchorMin = Vector2.zero;
                        rect.anchorMax = Vector2.one;
                        rect.offsetMin = rect.offsetMax = Vector2.zero;
                    }
                    break;
                case "button":
                    Image background = Undo.AddComponent<Image>(go);
                    background.color = ReadColor(op, Color.white);
                    Undo.AddComponent<Button>(go);
                    CreateButtonLabel(go.transform, (string)op["text"] ?? go.name,
                        op["fontSize"]?.Value<float>() ?? 24f);
                    break;
                case "text":
                    Text text = Undo.AddComponent<Text>(go);
                    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    text.text = (string)op["text"] ?? go.name;
                    text.fontSize = Mathf.RoundToInt(op["fontSize"]?.Value<float>() ?? 24f);
                    text.alignment = TextAnchor.MiddleCenter;
                    text.color = ReadColor(op, Color.white);
                    break;
                case "tmpText":
                    TextMeshProUGUI tmp = Undo.AddComponent<TextMeshProUGUI>(go);
                    tmp.text = (string)op["text"] ?? go.name;
                    tmp.fontSize = op["fontSize"]?.Value<float>() ?? 24f;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = ReadColor(op, Color.white);
                    break;
                default:
                    if (UiAdvancedTool.Supports(kind))
                    {
                        UiAdvancedTool.Apply(go, op);
                        break;
                    }
                    throw new InvalidOperationException("Unexpected UI type: " + kind);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return go;
        }

        private static Color ReadColor(JObject op, Color fallback)
        {
            if (!(op["color"] is JObject c)) return fallback;
            return new Color(c["r"].Value<float>(), c["g"].Value<float>(),
                c["b"].Value<float>(), c["a"].Value<float>());
        }

        private static void CreateButtonLabel(Transform parent, string value, float fontSize)
        {
            var label = new GameObject("Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(label, "Create button label");
            SceneManager.MoveGameObjectToScene(label, parent.gameObject.scene);
            Undo.SetTransformParent(label.transform, parent, "Parent button label");
            var rect = (RectTransform)label.transform;
            rect.localScale = Vector3.one;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text text = Undo.AddComponent<Text>(label);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = Mathf.RoundToInt(fontSize);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.raycastTarget = false;
        }

        public static JObject ProposeRectTransform(JObject args)
        {
            GameObject target = ObjectAccess.SelectedOrId(args);
            if (!(target.transform is RectTransform))
                throw new ArgumentException("Target must have a RectTransform.");
            string id = ObjectAccess.Id(target.transform);
            var operations = new JArray();
            foreach (var item in new[] {
                new { key = "anchorMin", path = "m_AnchorMin" },
                new { key = "anchorMax", path = "m_AnchorMax" },
                new { key = "pivot", path = "m_Pivot" },
                new { key = "sizeDelta", path = "m_SizeDelta" },
                new { key = "anchoredPosition", path = "m_AnchoredPosition" } })
            {
                if (!(args[item.key] is JObject value)) continue;
                if (value.Count != 2) throw new ArgumentException(item.key + " requires x, y.");
                float min = item.key.StartsWith("anchor", StringComparison.Ordinal) || item.key == "pivot" ? 0 : -100000;
                float max = item.key.StartsWith("anchor", StringComparison.Ordinal) || item.key == "pivot" ? 1 : 100000;
                var vec = new JObject {
                    ["x"] = Number(value, "x", min, max),
                    ["y"] = Number(value, "y", min, max)
                };
                operations.Add(new JObject { ["kind"] = "set_property",
                    ["targetId"] = id, ["propertyPath"] = item.path, ["value"] = vec });
            }
            if (operations.Count == 0) throw new ArgumentException("Provide at least one RectTransform field.");
            return ChangeSets.Propose(new JObject {
                ["summary"] = (string)args["summary"] ?? "Update RectTransform of " + target.name,
                ["operations"] = operations
            });
        }

        public static JObject Inspect(JObject args)
        {
            GameObject root = ObjectAccess.SelectedOrId(args);
            int limit = ObjectAccess.Limit(args, "maximumObjects", 100, 1, 200);
            var nodes = new JArray();
            foreach (GameObject go in ObjectAccess.Walk(root, limit))
            {
                if (!(go.transform is RectTransform rect)) continue;
                var item = new JObject {
                    ["objectId"] = ObjectAccess.Id(go),
                    ["name"] = go.name,
                    ["parentId"] = go.transform.parent == null ? JValue.CreateNull()
                        : new JValue(ObjectAccess.Id(go.transform.parent.gameObject)),
                    ["anchorMin"] = Vector(rect.anchorMin),
                    ["anchorMax"] = Vector(rect.anchorMax),
                    ["pivot"] = Vector(rect.pivot),
                    ["anchoredPosition"] = Vector(rect.anchoredPosition),
                    ["sizeDelta"] = Vector(rect.sizeDelta),
                    ["components"] = new JArray(go.GetComponents<Component>()
                        .Where(c => c != null).Select(c => c.GetType().FullName))
                };
                Canvas canvas = go.GetComponent<Canvas>();
                if (canvas != null) item["renderMode"] = canvas.renderMode.ToString();
                Text text = go.GetComponent<Text>();
                if (text != null) item["text"] = Truncate(text.text);
                TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
                if (tmp != null) item["text"] = Truncate(tmp.text);
                Graphic graphic = go.GetComponent<Graphic>();
                if (graphic != null) item["color"] = ObjectAccess.Value(graphic.color);
                Selectable selectable = go.GetComponent<Selectable>();
                if (selectable != null) item["interactable"] = selectable.interactable;
                Button button = go.GetComponent<Button>();
                if (button != null)
                {
                    var listeners = new JArray();
                    for (int i = 0; i < Mathf.Min(20, button.onClick.GetPersistentEventCount()); i++)
                        listeners.Add(new JObject {
                            ["target"] = ObjectAccess.Reference(button.onClick.GetPersistentTarget(i)),
                            ["method"] = button.onClick.GetPersistentMethodName(i)
                        });
                    item["onClickListeners"] = listeners;
                }
                Slider slider = go.GetComponent<Slider>();
                if (slider != null)
                {
                    item["sliderMin"] = slider.minValue;
                    item["sliderMax"] = slider.maxValue;
                    item["sliderValue"] = slider.value;
                    item["wholeNumbers"] = slider.wholeNumbers;
                    item["fillRect"] = ObjectAccess.Reference(slider.fillRect);
                    item["handleRect"] = ObjectAccess.Reference(slider.handleRect);
                }
                Toggle toggle = go.GetComponent<Toggle>();
                if (toggle != null) item["isOn"] = toggle.isOn;
                InputField input = go.GetComponent<InputField>();
                if (input != null)
                {
                    item["inputText"] = Truncate(input.text);
                    item["characterLimit"] = input.characterLimit;
                    item["placeholder"] = input.placeholder is Text placeholderText
                        ? Truncate(placeholderText.text) : "";
                }
                ScrollRect scroll = go.GetComponent<ScrollRect>();
                if (scroll != null)
                {
                    item["horizontal"] = scroll.horizontal;
                    item["vertical"] = scroll.vertical;
                    item["viewport"] = ObjectAccess.Reference(scroll.viewport);
                    item["content"] = ObjectAccess.Reference(scroll.content);
                }
                LayoutGroup layout = go.GetComponent<LayoutGroup>();
                if (layout != null)
                {
                    item["layoutType"] = layout is HorizontalLayoutGroup ? "horizontal"
                        : layout is VerticalLayoutGroup ? "vertical"
                        : layout is GridLayoutGroup ? "grid" : "other";
                    item["padding"] = new JObject { ["left"] = layout.padding.left,
                        ["right"] = layout.padding.right, ["top"] = layout.padding.top,
                        ["bottom"] = layout.padding.bottom };
                    if (layout is HorizontalOrVerticalLayoutGroup hv)
                        item["spacing"] = hv.spacing;
                    else if (layout is GridLayoutGroup grid)
                    {
                        item["spacing"] = Vector(grid.spacing);
                        item["cellSize"] = Vector(grid.cellSize);
                        item["constraint"] = grid.constraint.ToString();
                        item["constraintCount"] = grid.constraintCount;
                    }
                }
                nodes.Add(item);
            }
            return new JObject {
                ["root"] = ObjectAccess.Reference(root),
                ["nodes"] = nodes,
                ["maximumObjects"] = limit,
                ["truncated"] = ObjectAccess.Walk(root, limit + 1).Skip(limit).Any()
            };
        }

        private static JObject Vector(Vector2 v) => new JObject { ["x"] = v.x, ["y"] = v.y };
        private static string Truncate(string value) => string.IsNullOrEmpty(value) ? "" :
            value.Length > 2048 ? value.Substring(0, 2048) : value;
    }
}
