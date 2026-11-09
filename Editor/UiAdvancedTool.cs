using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WasimDevelopment.UnityMcpBridge
{
    // Creates uGUI controls only during approved ChangeSet.ApplyOperation.
    // All child objects participate in Unity Undo and the scene-file backup.
    internal static class UiAdvancedTool
    {
        public static bool Supports(string kind)
            => kind == "slider" || kind == "toggle" || kind == "inputField" || kind == "scrollView";

        public static void Validate(JObject op)
        {
            string kind = (string)op["uiType"];
            if (!Supports(kind)) return;
            if (kind == "slider")
            {
                float min = Numeric(op, "minValue", -100000, 100000, 0);
                float max = Numeric(op, "maxValue", -100000, 100000, 1);
                if (max <= min) throw new ArgumentException("Slider maxValue must be greater than minValue.");
                Numeric(op, "value", min, max, (min + max) * 0.5f);
                Boolean(op, "wholeNumbers");
            }
            if (kind == "toggle") Boolean(op, "isOn");
            if (kind == "inputField")
            {
                if (op["placeholder"] != null &&
                    (op["placeholder"].Type != JTokenType.String || ((string)op["placeholder"]).Length > 512))
                    throw new ArgumentException("placeholder must be a string of at most 512 characters.");
                if (op["characterLimit"] != null)
                {
                    if (op["characterLimit"].Type != JTokenType.Integer ||
                        op["characterLimit"].Value<int>() < 0 ||
                        op["characterLimit"].Value<int>() > 4096)
                        throw new ArgumentException("characterLimit must be an integer from 0 to 4096.");
                }
            }
            if (kind == "scrollView")
            {
                Boolean(op, "horizontal");
                Boolean(op, "vertical");
                Numeric(op, "contentHeight", 1, 16384, 640);
                if ((op["horizontal"]?.Value<bool>() ?? false) == false &&
                    (op["vertical"]?.Value<bool>() ?? true) == false)
                    throw new ArgumentException("ScrollView must scroll vertically, horizontally or both.");
            }
        }

        public static void Apply(GameObject go, JObject op)
        {
            string kind = (string)op["uiType"];
            if (!Supports(kind)) throw new InvalidOperationException("Unsupported advanced uGUI type: " + kind);
            Validate(op);
            RectTransform rect = (RectTransform)go.transform;
            if (op["width"] == null || op["height"] == null)
            {
                Vector2 fallback = kind == "slider" ? new Vector2(260, 40)
                    : kind == "toggle" ? new Vector2(260, 46)
                    : kind == "inputField" ? new Vector2(360, 56)
                    : new Vector2(480, 320);
                rect.sizeDelta = new Vector2(op["width"]?.Value<float>() ?? fallback.x,
                    op["height"]?.Value<float>() ?? fallback.y);
            }

            switch (kind)
            {
                case "slider": CreateSlider(go, op); break;
                case "toggle": CreateToggle(go, op); break;
                case "inputField": CreateInput(go, op); break;
                case "scrollView": CreateScroll(go, op); break;
            }
        }

        private static void CreateSlider(GameObject go, JObject op)
        {
            Image baseImage = Undo.AddComponent<Image>(go);
            baseImage.color = new Color(0.17f, 0.19f, 0.24f, 0.8f);
            Slider slider = Undo.AddComponent<Slider>(go);
            RectTransform fillArea = Child(go.transform, "Fill Area", Vector2.zero, Vector2.one);
            fillArea.offsetMin = new Vector2(12, 8);
            fillArea.offsetMax = new Vector2(-12, -8);
            RectTransform fill = Child(fillArea, "Fill", Vector2.zero, Vector2.one);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Image fillImage = Undo.AddComponent<Image>(fill.gameObject);
            fillImage.color = ColorOf(op, new Color(0.20f, 0.62f, 0.95f, 1));
            RectTransform handleArea = Child(go.transform, "Handle Slide Area", Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(12, 0);
            handleArea.offsetMax = new Vector2(-12, 0);
            RectTransform handle = Child(handleArea, "Handle", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f));
            handle.sizeDelta = new Vector2(24, 32);
            Image handleImage = Undo.AddComponent<Image>(handle.gameObject);
            handleImage.color = Color.white;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = op["wholeNumbers"]?.Value<bool>() ?? false;
            slider.minValue = op["minValue"]?.Value<float>() ?? 0;
            slider.maxValue = op["maxValue"]?.Value<float>() ?? 1;
            slider.value = op["value"]?.Value<float>() ?? (slider.minValue + slider.maxValue) * 0.5f;
        }

        private static void CreateToggle(GameObject go, JObject op)
        {
            Toggle toggle = Undo.AddComponent<Toggle>(go);
            RectTransform bg = Child(go.transform, "Background", new Vector2(0, 0.5f),
                new Vector2(0, 0.5f));
            bg.pivot = new Vector2(0, 0.5f);
            bg.sizeDelta = new Vector2(30, 30);
            bg.anchoredPosition = new Vector2(4, 0);
            Image bgImage = Undo.AddComponent<Image>(bg.gameObject);
            bgImage.color = Color.white;
            RectTransform check = Child(bg, "Checkmark", Vector2.zero, Vector2.one);
            check.offsetMin = new Vector2(5, 5);
            check.offsetMax = new Vector2(-5, -5);
            Image checkImage = Undo.AddComponent<Image>(check.gameObject);
            checkImage.color = ColorOf(op, new Color(0.13f, 0.70f, 0.42f, 1));
            RectTransform label = Child(go.transform, "Label", Vector2.zero, Vector2.one);
            label.offsetMin = new Vector2(44, 0);
            label.offsetMax = Vector2.zero;
            Text labelText = NewText(label, (string)op["text"] ?? go.name,
                op["fontSize"]?.Value<float>() ?? 24, TextAnchor.MiddleLeft);
            labelText.color = Color.white;
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = op["isOn"]?.Value<bool>() ?? false;
        }

        private static void CreateInput(GameObject go, JObject op)
        {
            Image background = Undo.AddComponent<Image>(go);
            background.color = ColorOf(op, Color.white);
            InputField input = Undo.AddComponent<InputField>(go);
            RectTransform placeholder = Child(go.transform, "Placeholder", Vector2.zero, Vector2.one);
            placeholder.offsetMin = new Vector2(12, 5);
            placeholder.offsetMax = new Vector2(-12, -5);
            Text hint = NewText(placeholder, (string)op["placeholder"] ?? "Enter text...",
                op["fontSize"]?.Value<float>() ?? 22, TextAnchor.MiddleLeft);
            hint.color = new Color(0.45f, 0.45f, 0.45f, 0.75f);
            RectTransform textArea = Child(go.transform, "Text", Vector2.zero, Vector2.one);
            textArea.offsetMin = new Vector2(12, 5);
            textArea.offsetMax = new Vector2(-12, -5);
            Text text = NewText(textArea, "", op["fontSize"]?.Value<float>() ?? 22,
                TextAnchor.MiddleLeft);
            text.color = Color.black;
            input.targetGraphic = background;
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = op["characterLimit"]?.Value<int>() ?? 4096;
            input.text = (string)op["text"] ?? "";
        }

        private static void CreateScroll(GameObject go, JObject op)
        {
            Image background = Undo.AddComponent<Image>(go);
            background.color = ColorOf(op, new Color(0.12f, 0.15f, 0.20f, 0.65f));
            ScrollRect scroll = Undo.AddComponent<ScrollRect>(go);
            RectTransform viewport = Child(go.transform, "Viewport", Vector2.zero, Vector2.one);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            Undo.AddComponent<RectMask2D>(viewport.gameObject);
            RectTransform content = Child(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1));
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, op["contentHeight"]?.Value<float>() ?? 640);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = op["horizontal"]?.Value<bool>() ?? false;
            scroll.vertical = op["vertical"]?.Value<bool>() ?? true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22;
        }

        private static RectTransform Child(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create UI " + name);
            SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            Undo.SetTransformParent(go.transform, parent, "Parent UI " + name);
            var rect = (RectTransform)go.transform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static Text NewText(RectTransform rect, string content, float size, TextAnchor alignment)
        {
            Text text = Undo.AddComponent<Text>(rect.gameObject);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = Mathf.RoundToInt(size);
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static Color ColorOf(JObject op, Color fallback)
        {
            if (!(op["color"] is JObject c)) return fallback;
            return new Color(c["r"].Value<float>(), c["g"].Value<float>(),
                c["b"].Value<float>(), c["a"].Value<float>());
        }

        private static void Boolean(JObject op, string key)
        {
            if (op[key] != null && op[key].Type != JTokenType.Boolean)
                throw new ArgumentException(key + " must be boolean.");
        }

        private static float Numeric(JObject op, string key, float min, float max, float fallback)
        {
            JToken value = op[key];
            if (value == null) return fallback;
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                throw new ArgumentException(key + " must be numeric.");
            double result = value.Value<double>();
            if (double.IsNaN(result) || double.IsInfinity(result) || result < min || result > max)
                throw new ArgumentException(key + " must be finite between " + min + " and " + max + ".");
            return (float)result;
        }
    }
}
