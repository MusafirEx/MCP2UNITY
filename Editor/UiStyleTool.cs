using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WasimDevelopment.UnityMcpBridge
{
    // One reviewed change set for multiple UI style properties.
    // No scene write is performed by this tool; ChangeSets applies each
    // serialized property after local approval and stale-scene validation.
    internal static class UiStyleTool
    {
        public static JObject Propose(JObject args)
        {
            GameObject go = ObjectAccess.SelectedOrId(args);
            if (!(go.transform is RectTransform) || go.GetComponentInParent<Canvas>() == null)
                throw new ArgumentException("Select a uGUI element under a Canvas.");

            var operations = new JArray();
            Graphic graphic = go.GetComponent<Graphic>();
            Image image = go.GetComponent<Image>();
            Text legacyText = go.GetComponent<Text>();
            TextMeshProUGUI tmpText = go.GetComponent<TextMeshProUGUI>();
            Selectable selectable = go.GetComponent<Selectable>();

            if (args["text"] != null)
            {
                if (args["text"].Type != JTokenType.String || ((string)args["text"]).Length > 8192)
                    throw new ArgumentException("text must contain 0-8192 characters.");
                if (legacyText != null) Add(operations, legacyText, "m_Text", args["text"]);
                else if (tmpText != null) Add(operations, tmpText, "m_text", args["text"]);
                else throw new ArgumentException("The selected UI element has no Text or TextMeshProUGUI.");
            }
            if (args["fontSize"] != null)
            {
                float size = Number(args["fontSize"], "fontSize", 1, 256).Value<float>();
                if (legacyText != null)
                {
                    if (Math.Abs(size - Math.Round(size)) > 0.0001)
                        throw new ArgumentException("Legacy Text fontSize must be a whole number.");
                    Add(operations, legacyText, "m_FontData.m_FontSize", new JValue((int)size));
                }
                else if (tmpText != null) Add(operations, tmpText, "m_fontSize", new JValue(size));
                else throw new ArgumentException("The selected UI element has no text component.");
            }
            if (args["color"] != null)
            {
                if (graphic == null) throw new ArgumentException("The selected element has no Graphic color.");
                JObject color = ExactColor(args["color"]);
                Add(operations, graphic, "m_Color", color);
            }
            if (args["interactable"] != null)
            {
                if (selectable == null) throw new ArgumentException("The selected element has no Selectable component.");
                Add(operations, selectable, "m_Interactable", Boolean(args["interactable"], "interactable"));
            }
            if (args["raycastTarget"] != null)
            {
                if (graphic == null) throw new ArgumentException("The selected element has no Graphic component.");
                Add(operations, graphic, "m_RaycastTarget", Boolean(args["raycastTarget"], "raycastTarget"));
            }
            if (args["fillAmount"] != null)
            {
                if (image == null) throw new ArgumentException("fillAmount requires an Image component.");
                Add(operations, image, "m_FillAmount", Number(args["fillAmount"], "fillAmount", 0, 1));
            }
            if (args["spriteId"] != null)
            {
                if (image == null) throw new ArgumentException("spriteId requires an Image component.");
                if (args["spriteId"].Type != JTokenType.String) throw new ArgumentException("spriteId must be a string.");
                string spriteId = (string)args["spriteId"];
                Object spriteObject = ObjectAccess.Resolve(spriteId);
                if (!(spriteObject is Sprite sprite) || !EditorUtility.IsPersistent(sprite))
                    throw new ArgumentException("spriteId must reference a saved Sprite asset.");
                Add(operations, image, "m_Sprite",
                    new JObject { ["objectId"] = ObjectAccess.Id(sprite) });
            }
            if (operations.Count == 0)
                throw new ArgumentException("Provide at least one UI property to change.");
            return ChangeSets.Propose(new JObject {
                ["summary"] = (string)args["summary"] ?? "Update uGUI styling of " + go.name,
                ["operations"] = operations
            });
        }

        private static void Add(JArray ops, Component target, string path, JToken value)
        {
            // Verify the exact serialized field exists and is editable before
            // publishing a proposal; ChangeSets also validates it on approval.
            using (var serialized = new SerializedObject(target))
            {
                SerializedProperty prop = serialized.FindProperty(path);
                if (prop == null || !prop.editable)
                    throw new ArgumentException("UI property is unavailable: " + target.GetType().Name + "." + path);
            }
            ops.Add(new JObject {
                ["kind"] = "set_property",
                ["targetId"] = ObjectAccess.Id(target),
                ["propertyPath"] = path,
                ["value"] = value.DeepClone()
            });
        }

        private static JToken Boolean(JToken token, string key)
        {
            if (token.Type != JTokenType.Boolean) throw new ArgumentException(key + " must be boolean.");
            return token.DeepClone();
        }

        private static JToken Number(JToken token, string key, double min, double max)
        {
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new ArgumentException(key + " must be numeric.");
            double value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentException(key + " must be finite and in range [" + min + ", " + max + "].");
            return new JValue(value);
        }

        private static JObject ExactColor(JToken token)
        {
            if (!(token is JObject c) || c.Count != 4)
                throw new ArgumentException("color requires exactly r, g, b, a.");
            var result = new JObject();
            foreach (string axis in new[] { "r", "g", "b", "a" })
                result[axis] = Number(c[axis], "color." + axis, 0, 1);
            return result;
        }
    }
}
