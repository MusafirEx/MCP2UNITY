using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WasimDevelopment.UnityMcpBridge
{
    // Initial safe onClick binding: allow only GameObject.SetActive(bool)
    // targeting UI in the same Canvas. Arbitrary script/method calls are denied.
    internal static class UiButtonEventTool
    {
        public static JObject Propose(JObject args)
        {
            string buttonId = (string)args["buttonId"];
            string targetId = (string)args["targetId"];
            if (string.IsNullOrEmpty(buttonId) || string.IsNullOrEmpty(targetId))
                throw new ArgumentException("buttonId and targetId are required.");
            GameObject buttonGo = ObjectAccess.GameObjectOf(ObjectAccess.Resolve(buttonId));
            GameObject targetGo = ObjectAccess.GameObjectOf(ObjectAccess.Resolve(targetId));
            var op = new JObject {
                ["kind"] = "bind_ui_button_event",
                ["targetId"] = ObjectAccess.Id(buttonGo),
                ["eventTargetId"] = ObjectAccess.Id(targetGo),
                ["action"] = args["action"]?.DeepClone(),
                ["value"] = args["value"]?.DeepClone()
            };
            Validate(buttonGo, targetGo, op);
            return ChangeSets.Propose(new JObject {
                ["summary"] = (string)args["summary"] ??
                    "Bind Button onClick to UI SetActive(" + (bool)op["value"] + ")",
                ["operations"] = new JArray(op)
            });
        }

        public static void Validate(GameObject buttonGo, GameObject targetGo, JObject op)
        {
            if (buttonGo == null || targetGo == null ||
                buttonGo.scene != targetGo.scene ||
                !buttonGo.scene.IsValid() || !buttonGo.scene.isLoaded)
                throw new ArgumentException("Button and event target must be saved scene objects in the same scene.");
            if (EditorUtility.IsPersistent(buttonGo) || EditorUtility.IsPersistent(targetGo))
                throw new ArgumentException("Prefab/asset event targets are unsupported.");
            Button button = buttonGo.GetComponent<Button>();
            Canvas buttonCanvas = buttonGo.GetComponentInParent<Canvas>();
            if (button == null || buttonCanvas == null ||
                !(targetGo.transform is RectTransform) ||
                targetGo.GetComponentInParent<Canvas>() != buttonCanvas)
                throw new ArgumentException("Button and UI target must be in the same Canvas hierarchy.");
            if ((string)op["action"] != "setActive" || op["value"]?.Type != JTokenType.Boolean)
                throw new ArgumentException("Only action=setActive with a boolean value is supported.");
            if (button.onClick.GetPersistentEventCount() >= 20)
                throw new ArgumentException("Button onClick is limited to 20 persistent listeners.");
        }

        public static void Apply(GameObject buttonGo, JObject op)
        {
            GameObject targetGo = ObjectAccess.GameObjectOf(ObjectAccess.Resolve((string)op["eventTargetId"]));
            Validate(buttonGo, targetGo, op);
            Button button = buttonGo.GetComponent<Button>();
            Undo.RecordObject(button, "Bind reviewed UI Button event");
            UnityEventTools.AddBoolPersistentListener(button.onClick,
                new UnityAction<bool>(targetGo.SetActive), op["value"].Value<bool>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            EditorSceneManager.MarkSceneDirty(buttonGo.scene);
        }
    }
}
