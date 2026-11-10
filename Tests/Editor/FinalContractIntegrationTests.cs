using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    // Phase 7: final advertised MCP contract and bounded read-only regressions.
    // Uses the same disposable, saved additive scene as earlier Editor tests.
    public sealed partial class BridgeIntegrationTests
    {
        private static JObject Phase7CatalogTool(string name)
        {
            return McpToolCatalog.Build().OfType<JObject>()
                .Single(x => (string)x["name"] == name);
        }

        private static IEnumerable<JObject> Phase7WalkJsonObjects(JToken root)
        {
            if (root is JObject objectValue)
            {
                yield return objectValue;
                foreach (JProperty property in objectValue.Properties())
                    foreach (JObject nested in Phase7WalkJsonObjects(property.Value))
                        yield return nested;
            }
            else if (root is JArray array)
                foreach (JToken child in array)
                    foreach (JObject nested in Phase7WalkJsonObjects(child))
                        yield return nested;
        }

        [Test]
        public void Phase7CatalogContainsExactly47UniqueTools()
        {
            JArray tools = McpToolCatalog.Build();
            string[] names = tools.Select(x => (string)x["name"]).ToArray();
            Assert.AreEqual(47, names.Length, "The advertised MCP surface changed.");
            Assert.AreEqual(47, names.Distinct(StringComparer.Ordinal).Count());
            Assert.IsTrue(names.All(x => x.StartsWith("unity_", StringComparison.Ordinal)));
        }

        [Test]
        public void Phase7EveryCatalogEntryDeclaresStrictSchemaAndHints()
        {
            foreach (JObject tool in McpToolCatalog.Build().OfType<JObject>())
            {
                string name = (string)tool["name"];
                Assert.IsFalse(string.IsNullOrWhiteSpace((string)tool["title"]), name);
                Assert.IsFalse(string.IsNullOrWhiteSpace((string)tool["description"]), name);
                JObject input = tool["inputSchema"] as JObject;
                Assert.NotNull(input, name);
                Assert.AreEqual("object", (string)input["type"], name);
                Assert.IsInstanceOf<JObject>(input["properties"], name);
                Assert.AreEqual(false, (bool?)input["additionalProperties"], name);
                JObject hints = tool["annotations"] as JObject;
                Assert.NotNull(hints, name);
                foreach (string key in new[] {
                    "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint"
                })
                    Assert.AreEqual(JTokenType.Boolean, hints[key]?.Type,
                        name + " missing " + key);
                Assert.IsFalse(hints["openWorldHint"].Value<bool>(), name);
            }
        }

        [Test]
        public void Phase7RequiredInputFieldsAreAllDeclaredInSchema()
        {
            int requiredCount = 0;
            foreach (JObject tool in McpToolCatalog.Build().OfType<JObject>())
            {
                JObject schema = (JObject)tool["inputSchema"];
                if (!(schema["required"] is JArray required)) continue;
                foreach (JToken field in required)
                {
                    Assert.AreEqual(JTokenType.String, field.Type);
                    Assert.NotNull(schema["properties"][(string)field],
                        (string)tool["name"] + ": required but undeclared " + field);
                    requiredCount++;
                }
            }
            Assert.Greater(requiredCount, 10, "Expected meaningful required-argument coverage.");
        }

        [Test]
        public void Phase7NumericalSchemaLimitsContainTheirDefaults()
        {
            int checkedBounds = 0;
            foreach (JObject tool in McpToolCatalog.Build().OfType<JObject>())
                foreach (JObject schema in Phase7WalkJsonObjects(tool["inputSchema"]))
                {
                    if (schema["minimum"] == null || schema["maximum"] == null) continue;
                    double minimum = schema["minimum"].Value<double>();
                    double maximum = schema["maximum"].Value<double>();
                    Assert.LessOrEqual(minimum, maximum, (string)tool["name"]);
                    if (schema["default"] != null)
                    {
                        double fallback = schema["default"].Value<double>();
                        Assert.GreaterOrEqual(fallback, minimum, (string)tool["name"]);
                        Assert.LessOrEqual(fallback, maximum, (string)tool["name"]);
                    }
                    checkedBounds++;
                }
            Assert.GreaterOrEqual(checkedBounds, 15);
        }

        [Test]
        public void Phase7LegacyBackportAndUiToolsRemainAdvertised()
        {
            string[] required = {
                "unity_get_status", "unity_read_script", "unity_get_active_scene",
                "unity_read_console", "unity_get_packages", "unity_inspect_runtime",
                "unity_propose_change_set", "unity_get_test_results",
                "unity_create_canvas", "unity_create_ui", "unity_set_rect_transform",
                "unity_inspect_ui", "unity_set_ui_property", "unity_set_ui_layout",
                "unity_bind_button_event", "unity_delete_gameobject"
            };
            HashSet<string> available = new HashSet<string>(
                McpToolCatalog.Build().Select(x => (string)x["name"]),
                StringComparer.Ordinal);
            foreach (string name in required) Assert.IsTrue(available.Contains(name), name);
        }

        [Test]
        public void Phase7ReadOnlyAndReviewedMutationPermissionHintsRemainCorrect()
        {
            foreach (string name in new[] {
                "unity_get_status", "unity_read_script", "unity_get_active_scene",
                "unity_read_console", "unity_inspect_ui", "unity_inspect_runtime",
                "unity_get_change_sets", "unity_get_test_results"
            })
            {
                JObject hints = (JObject)Phase7CatalogTool(name)["annotations"];
                Assert.IsTrue(hints["readOnlyHint"].Value<bool>(), name);
                Assert.IsFalse(hints["destructiveHint"].Value<bool>(), name);
                Assert.IsTrue(hints["idempotentHint"].Value<bool>(), name);
            }
            foreach (string name in new[] {
                "unity_create_canvas", "unity_create_ui",
                "unity_bind_button_event", "unity_delete_gameobject",
                "unity_propose_change_set", "unity_propose_editor_action",
                "unity_propose_script_text_patch"
            })
                Assert.IsFalse(Phase7CatalogTool(name)["annotations"]["readOnlyHint"]
                    .Value<bool>(), name);
            Assert.IsTrue(Phase7CatalogTool("unity_delete_gameobject")
                ["annotations"]["destructiveHint"].Value<bool>());
        }

        [Test]
        public void Phase7StatusAdvertisesSame47ToolsAsCatalog()
        {
            JObject status = (JObject)UnityToolExecutor.Execute(
                "unity_get_status", new JObject());
            Assert.AreEqual("0.5.2", (string)status["bridgeVersion"]);
            Assert.AreEqual(47, (int)status["advertisedToolCount"]);
            CollectionAssert.AreEquivalent(
                McpToolCatalog.Build().Select(x => (string)x["name"]).ToArray(),
                ((JArray)status["advertisedTools"]).Values<string>().ToArray());
            Assert.AreEqual(JTokenType.Boolean, status["isPlaying"].Type);
            Assert.IsTrue(status["readOnly"].Value<bool>());
        }

        [Test]
        public void Phase7ProjectInfoMatchesCurrentActiveSceneWithoutEdits()
        {
            Scene before = SceneManager.GetActiveScene();
            bool wasDirty = before.isDirty;
            JObject info = (JObject)UnityToolExecutor.Execute(
                "unity_get_project_info", new JObject());
            Assert.AreEqual(Application.unityVersion, (string)info["unityVersion"]);
            Assert.AreEqual(Application.productName, (string)info["projectName"]);
            Assert.AreEqual(before.path, (string)info["activeScene"]["path"]);
            Assert.AreEqual(before.rootCount, (int)info["activeScene"]["rootCount"]);
            Assert.AreEqual(wasDirty, before.isDirty);
        }

        [Test]
        public void Phase7SceneHierarchyOutputHonorsObjectAndDepthLimits()
        {
            Scene original = SceneManager.GetActiveScene();
            GameObject extra = new GameObject("Phase7AnotherRoot");
            SceneManager.MoveGameObjectToScene(extra, _root.scene);
            GameObject child = new GameObject("Phase7NestedChild");
            SceneManager.MoveGameObjectToScene(child, _root.scene);
            child.transform.SetParent(_root.transform, false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            try
            {
                Assert.IsTrue(SceneManager.SetActiveScene(_root.scene));
                JObject result = (JObject)UnityToolExecutor.Execute(
                    "unity_get_active_scene", new JObject {
                        ["maximumObjects"] = 1, ["maximumDepth"] = 0
                    });
                Assert.AreEqual(_scenePath, (string)result["path"]);
                Assert.AreEqual(2, (int)result["rootCount"]);
                Assert.AreEqual(1, (int)result["returnedObjectCount"]);
                Assert.IsTrue(result["truncated"].Value<bool>());
                Assert.AreEqual(1, ((JArray)result["hierarchy"]).Count);
                Assert.AreEqual(0,
                    ((JArray)result["hierarchy"][0]["children"]).Count);
                Assert.IsFalse(_root.scene.isDirty);
            }
            finally
            {
                if (original.IsValid() && original.isLoaded)
                    SceneManager.SetActiveScene(original);
            }
        }

        [Test]
        public void Phase7RuntimeInspectorBoundsOutputWithoutEditingObjects()
        {
            GameObject child = new GameObject("Phase7RuntimeChild");
            SceneManager.MoveGameObjectToScene(child, _root.scene);
            child.transform.SetParent(_root.transform, false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            bool wasActive = _root.activeSelf;
            JObject inspected = (JObject)UnityToolExecutor.Execute(
                "unity_inspect_runtime", new JObject {
                    ["objectId"] = ObjectAccess.Id(_root),
                    ["maximumObjects"] = 1, ["maximumProperties"] = 3,
                    ["includeSerialized"] = false
                });
            JArray objects = (JArray)inspected["objects"];
            Assert.AreEqual(1, objects.Count);
            Assert.AreEqual(ObjectAccess.Id(_root), (string)objects[0]["objectId"]);
            Assert.AreEqual(_root.name, (string)objects[0]["name"]);
            Assert.IsTrue(inspected["limitReached"].Value<bool>());
            Assert.IsNull(objects[0]["serialized"]);
            Assert.AreEqual(wasActive, _root.activeSelf);
            Assert.IsFalse(_root.scene.isDirty);
        }

        [Test]
        public void Phase7ScriptReaderReturnsBoundedLinesHashAndSafeSourceText()
        {
            bool wasAllowed = BridgePreferences.AllowPackageScripts;
            try
            {
                BridgePreferences.AllowPackageScripts = true;
                JObject read = (JObject)UnityToolExecutor.Execute(
                    "unity_read_script", new JObject {
                        ["path"] = "Packages/com.musafirex.mcp2unity/Editor/BridgeVersion.cs",
                        ["startLine"] = 1, ["endLine"] = 3
                    });
                Assert.Greater((int)read["lineCount"], 3);
                Assert.AreEqual(1, (int)read["startLine"]);
                Assert.AreEqual(3, (int)read["endLine"]);
                Assert.IsTrue(read["truncated"].Value<bool>());
                Assert.IsTrue(((string)read["sourceText"]).StartsWith("1: "));
                Assert.IsTrue(((string)read["sourceText"]).Contains("3: "));
                Assert.AreEqual(64, ((string)read["sha256"]).Length);
                Assert.IsNull(read["content"],
                    "Avoid the top-level MCP content property collision.");
            }
            finally { BridgePreferences.AllowPackageScripts = wasAllowed; }
        }

        [Test]
        public void Phase7InvalidDispatcherArgumentsFailWithoutSceneMutation()
        {
            bool wasDirty = _root.scene.isDirty;
            int roots = _root.scene.rootCount;
            Assert.Throws<ArgumentException>(() => UnityToolExecutor.Execute(
                "unity_search_scripts", new JObject { ["query"] = " \t " }));
            Assert.Throws<ArgumentException>(() => UnityToolExecutor.Execute(
                "unity_create_ui", new JObject {
                    ["uiType"] = "nonexistent", ["name"] = "Unsafe",
                    ["parentId"] = ObjectAccess.Id(_root)
                }));
            Assert.Throws<ArgumentException>(() => UnityToolExecutor.Execute(
                "unity_unrecognized_phase7", new JObject()));
            Assert.AreEqual(roots, _root.scene.rootCount);
            Assert.AreEqual(wasDirty, _root.scene.isDirty);
        }
    }
}
