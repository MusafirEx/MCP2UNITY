using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WasimDevelopment.UnityMcpBridge
{
    // Destructive scene edits require an explicit exact-name confirmation and
    // an upper bound on the entire subtree, followed by local Unity approval.
    internal static class GameObjectDeleteTool
    {
        public static JObject Propose(JObject args)
        {
            GameObject target = ObjectAccess.SelectedOrId(args);
            var op = new JObject {
                ["kind"] = "delete_gameobject",
                ["targetId"] = ObjectAccess.Id(target),
                ["confirmedName"] = args["confirmedName"]?.DeepClone(),
                ["maximumObjects"] = args["maximumObjects"]?.DeepClone() ?? new JValue(25)
            };
            Validate(target, op);
            return ChangeSets.Propose(new JObject {
                ["summary"] = (string)args["summary"] ??
                    "Delete reviewed scene GameObject '" + target.name + "' and its " +
                    (int)op["expectedSubtreeCount"] + " hierarchy object(s)",
                ["operations"] = new JArray(op)
            });
        }

        public static void Validate(GameObject go, JObject op)
        {
            if (go == null || !go.scene.IsValid() || !go.scene.isLoaded ||
                string.IsNullOrEmpty(go.scene.path) || EditorUtility.IsPersistent(go))
                throw new ArgumentException("Deletion requires a saved scene GameObject.");
            if (PrefabUtility.IsPartOfPrefabInstance(go) || PrefabUtility.IsPartOfPrefabAsset(go))
                throw new ArgumentException("Deleting parts of a prefab instance/asset is unsupported.");
            if (!(op["confirmedName"] is JValue nameToken) ||
                nameToken.Type != JTokenType.String ||
                (string)op["confirmedName"] != go.name)
                throw new ArgumentException("confirmedName must exactly match the target GameObject name.");
            if (op["maximumObjects"]?.Type != JTokenType.Integer)
                throw new ArgumentException("maximumObjects must be an integer between 1 and 100.");
            int maximum = op["maximumObjects"].Value<int>();
            if (maximum < 1 || maximum > 100)
                throw new ArgumentException("maximumObjects must be between 1 and 100.");

            int subtree = go.GetComponentsInChildren<Transform>(true).Length;
            if (subtree > maximum)
                throw new ArgumentException("Deletion would remove " + subtree + " objects, exceeding maximumObjects=" + maximum + ".");
            string parent = go.transform.parent == null ? null : ObjectAccess.Id(go.transform.parent.gameObject);
            JToken parentToken = parent == null ? JValue.CreateNull() : new JValue(parent);
            if (op.Property("expectedParentId") != null &&
                !JToken.DeepEquals(op["expectedParentId"], parentToken))
                throw new InvalidOperationException("Deletion target parent changed.");
            if (op.Property("expectedSubtreeCount") != null &&
                (op["expectedSubtreeCount"].Type != JTokenType.Integer ||
                 op["expectedSubtreeCount"].Value<int>() != subtree))
                throw new InvalidOperationException("Deletion target subtree changed.");
            op["expectedParentId"] = parentToken;
            op["expectedSubtreeCount"] = subtree;
        }

        public static void Apply(GameObject go, JObject op)
        {
            Validate(go, op);
            Scene scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
