using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    public sealed class BridgeIntegrationTests
    {
        private string _folder, _scenePath;
        private GameObject _root;
        private bool _oldPermission, _restoreEmpty;
        private string _originalActive;
        private readonly List<string> _proposals = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _oldPermission = BridgePreferences.EnableChangeSets;
            _folder = null; _scenePath = null; _restoreEmpty = false;
            _originalActive = SceneManager.GetActiveScene().path;
            // The Unity Test Runner can leave a clean, untitled scene open beside a
            // saved scene. NewScene(Additive) refuses to run while it is loaded.
            // Never close dirty or nonempty untitled scenes (they may contain user work).
            var cleanUntitledScenes = new List<Scene>();
            bool hasSavedScene = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.rootCount != 0))
                    Assert.Ignore("Save all modified or non-empty untitled scenes before bridge integration tests.");
                if (string.IsNullOrEmpty(scene.path)) cleanUntitledScenes.Add(scene);
                else hasSavedScene = true;
            }
            _restoreEmpty = !hasSavedScene;
            if (hasSavedScene)
            {
                // Only remove completely blank, unmodified untitled scenes.
                // All named scenes remain loaded and the test scene is additive.
                foreach (Scene untitled in cleanUntitledScenes)
                    if (untitled.IsValid() && untitled.isLoaded)
                        EditorSceneManager.CloseScene(untitled, true);
            }
            BridgePreferences.EnableChangeSets = true;
            _folder = "Assets/WasimMcpTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder));
            Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, _restoreEmpty ? NewSceneMode.Single : NewSceneMode.Additive);
            _root = new GameObject("BridgeTestRoot");
            SceneManager.MoveGameObjectToScene(_root, test);
            _scenePath = _folder + "/Test.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(test, _scenePath));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string id in _proposals)
                try { ChangeSets.Reject(id); } catch (InvalidOperationException) { }
            _proposals.Clear();
            if (!string.IsNullOrEmpty(_scenePath))
            {
                Scene scene = SceneManager.GetSceneByPath(_scenePath);
                if (_restoreEmpty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                if (!string.IsNullOrEmpty(_originalActive))
                {
                    Scene original = SceneManager.GetSceneByPath(_originalActive);
                    if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                }
            }
            if (!string.IsNullOrEmpty(_folder)) AssetDatabase.DeleteAsset(_folder);
            BridgePreferences.EnableChangeSets = _oldPermission;
        }
        private JObject Propose(params JObject[] operations)
        {
            JObject record = ChangeSets.Propose(new JObject { ["summary"] = "Bridge integration test", ["operations"] = new JArray(operations) });
            _proposals.Add((string)record["id"]); return record;
        }

        [Test]
        public void ComponentChangeCanBeAppliedAndRolledBack()
        {
            var body = _root.AddComponent<Rigidbody>(); body.isKinematic = false;
            EditorSceneManager.MarkSceneDirty(_root.scene); EditorSceneManager.SaveScene(_root.scene);
            JObject set = Propose(new JObject { ["kind"] = "set_property", ["targetId"] = ObjectAccess.Id(body),
                ["propertyPath"] = "m_IsKinematic", ["value"] = true });
            Assert.IsFalse(body.isKinematic);
            ChangeSets.Approve((string)set["id"]); Assert.IsTrue(body.isKinematic);
            // Keep the fixture's own scene clean before requesting a guarded rollback.
            // Never save unrelated scenes: they may contain user edits.
            Scene fixture = SceneManager.GetSceneByPath(_scenePath);
            if (fixture.IsValid() && fixture.isDirty)
                Assert.IsTrue(EditorSceneManager.SaveScene(fixture));
            JObject rollback = ChangeSets.ProposeRollback((string)set["id"], "Test rollback");
            _proposals.Add((string)rollback["id"]); ChangeSets.Approve((string)rollback["id"]);
            GameObject restored = SceneManager.GetSceneByPath(_scenePath).GetRootGameObjects().Single(g => g.name == "BridgeTestRoot");
            Assert.IsFalse(restored.GetComponent<Rigidbody>().isKinematic);
        }


        [Test]
        public void TransformToolProposesAndAppliesLocalPositionEulerRotationAndScale()
        {
            JObject catalog = McpToolCatalog.Build().OfType<JObject>()
                .Single(t => (string)t["name"] == "unity_set_transform");
            Assert.IsFalse(catalog["annotations"]["readOnlyHint"].Value<bool>());
            JObject proposal = (JObject)UnityToolExecutor.Execute("unity_set_transform", new JObject
            {
                ["objectId"] = ObjectAccess.Id(_root),
                ["position"] = new JObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 },
                ["rotation"] = new JObject { ["x"] = 0, ["y"] = 90, ["z"] = 0 },
                ["scale"] = new JObject { ["x"] = 2, ["y"] = 3, ["z"] = 4 }
            });
            _proposals.Add((string)proposal["id"]);
            Assert.AreEqual("Pending", (string)proposal["status"]);
            JObject pending = ChangeSets.List(false, true).OfType<JObject>()
                .Single(p => (string)p["id"] == (string)proposal["id"]);
            JArray operations = (JArray)pending["operations"];
            CollectionAssert.AreEqual(
                new[] { "m_LocalPosition", "m_LocalRotation", "m_LocalScale" },
                operations.Select(op => (string)op["propertyPath"]).ToArray());
            Assert.AreEqual(Vector3.zero, _root.transform.localPosition,
                "Proposing must never modify the scene.");
            ChangeSets.Approve((string)proposal["id"]);
            Assert.Less(Vector3.Distance(new Vector3(1, 2, 3), _root.transform.localPosition), 1e-4f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 90, 0), _root.transform.localRotation), 1e-3f);
            Assert.Less(Vector3.Distance(new Vector3(2, 3, 4), _root.transform.localScale), 1e-4f);
        }

        [Test]
        public void TransformToolConvertsWorldCoordinatesAndRelativeScaleUnderUniformParent()
        {
            _root.transform.position = new Vector3(5, 1, -2);
            _root.transform.rotation = Quaternion.Euler(0, 30, 0);
            _root.transform.localScale = new Vector3(2, 2, 2);
            GameObject child = new GameObject("TransformChild");
            SceneManager.MoveGameObjectToScene(child, _root.scene);
            child.transform.SetParent(_root.transform, false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));

            JObject absolute = (JObject)ExtendedTools.Execute("unity_set_transform", new JObject
            {
                ["objectId"] = ObjectAccess.Id(child),
                ["space"] = "world",
                ["position"] = new JObject { ["x"] = 10, ["y"] = 4, ["z"] = 3 },
                ["rotation"] = new JObject { ["x"] = 0, ["y"] = 90, ["z"] = 0 },
                ["scale"] = new JObject { ["x"] = 3, ["y"] = 3, ["z"] = 3 }
            });
            _proposals.Add((string)absolute["id"]);
            ChangeSets.Approve((string)absolute["id"]);
            Assert.Less(Vector3.Distance(new Vector3(10, 4, 3), child.transform.position), 1e-3f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 90, 0), child.transform.rotation), 1e-3f);
            Assert.Less(Vector3.Distance(new Vector3(3, 3, 3), child.transform.lossyScale), 1e-3f);

            // Unity may mark the temporary test scene dirty after a change-set
            // apply. Before proposing a second guarded change, save ONLY this
            // fixture scene; never save unrelated scenes containing user work.
            Scene fixture = SceneManager.GetSceneByPath(_scenePath);
            Assert.IsTrue(fixture.IsValid() && fixture.isLoaded);
            if (fixture.isDirty)
                Assert.IsTrue(EditorSceneManager.SaveScene(fixture));

            JObject relative = (JObject)ExtendedTools.Execute("unity_set_transform", new JObject
            {
                ["objectId"] = ObjectAccess.Id(child),
                ["space"] = "world",
                ["relative"] = true,
                ["position"] = new JObject { ["x"] = 1, ["y"] = 0, ["z"] = -2 },
                ["rotation"] = new JObject { ["x"] = 0, ["y"] = 45, ["z"] = 0 },
                ["scale"] = new JObject { ["x"] = 2, ["y"] = 1, ["z"] = 1 }
            });
            _proposals.Add((string)relative["id"]);
            ChangeSets.Approve((string)relative["id"]);
            Assert.Less(Vector3.Distance(new Vector3(11, 4, 1), child.transform.position), 1e-3f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 135, 0), child.transform.rotation), 1e-3f);
            Assert.Less(Vector3.Distance(new Vector3(6, 3, 3), child.transform.lossyScale), 1e-3f);
        }

        [Test]
        public void TransformToolRejectsUnsafeWorldScaleWithoutChangingScene()
        {
            _root.transform.localScale = new Vector3(2, 1, 1);
            GameObject child = new GameObject("NonuniformChild");
            SceneManager.MoveGameObjectToScene(child, _root.scene);
            child.transform.SetParent(_root.transform, false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            Assert.Throws<InvalidOperationException>(() => ExtendedTools.Execute("unity_set_transform", new JObject
            {
                ["objectId"] = ObjectAccess.Id(child),
                ["space"] = "world",
                ["scale"] = new JObject { ["x"] = 3, ["y"] = 3, ["z"] = 3 }
            }));
            Assert.Less(Vector3.Distance(Vector3.one, child.transform.localScale), 1e-6f);
        }

        [Test]
        public void ASceneChangedAfterProposalBlocksApply()
        {
            JObject set = Propose(new JObject { ["kind"] = "set_property", ["targetId"] = ObjectAccess.Id(_root),
                ["propertyPath"] = "m_Layer", ["value"] = 5 });
            _root.name = "EditedByUser"; EditorSceneManager.MarkSceneDirty(_root.scene); EditorSceneManager.SaveScene(_root.scene);
            Assert.Throws<InvalidOperationException>(() => ChangeSets.Approve((string)set["id"]));
            Assert.AreEqual(0, _root.layer);
        }

        [Test]
        public void AliasesCreateObjectAndComponentTogether()
        {
            JObject set = Propose(
                new JObject { ["kind"] = "create_gameobject", ["name"] = "Created", ["parentId"] = ObjectAccess.Id(_root), ["alias"] = "$child" },
                new JObject { ["kind"] = "add_component", ["targetId"] = "$child", ["componentType"] = "UnityEngine.Rigidbody", ["alias"] = "$body" },
                new JObject { ["kind"] = "set_property", ["targetId"] = "$body", ["propertyPath"] = "m_UseGravity", ["value"] = false });
            ChangeSets.Approve((string)set["id"]);
            var child = _root.transform.Find("Created");
            Assert.NotNull(child); Assert.IsFalse(child.GetComponent<Rigidbody>().useGravity);
        }

        [Test]
        public void FailureAfterCreationRestoresScene()
        {
            JObject set = Propose(
                new JObject { ["kind"] = "create_gameobject", ["name"] = "MustDisappear", ["parentId"] = ObjectAccess.Id(_root), ["alias"] = "$child" },
                new JObject { ["kind"] = "set_property", ["targetId"] = "$child", ["propertyPath"] = "NoSuchProperty", ["value"] = true });
            Assert.Throws<InvalidOperationException>(() => ChangeSets.Approve((string)set["id"]));
            GameObject restored = SceneManager.GetSceneByPath(_scenePath).GetRootGameObjects().Single(g => g.name == "BridgeTestRoot");
            Assert.IsNull(restored.transform.Find("MustDisappear"));
        }

        [Test]
        public void JointInspectorReportsConnectedBodyAndKinematicState()
        {
            _root.AddComponent<Rigidbody>();
            var other = new GameObject("OtherBody"); SceneManager.MoveGameObjectToScene(other, _root.scene);
            Rigidbody connected = other.AddComponent<Rigidbody>(); connected.isKinematic = true;
            FixedJoint joint = _root.AddComponent<FixedJoint>(); joint.connectedBody = connected;
            JObject state = RuntimeInspection.ComponentValues(joint);
            Assert.AreEqual(ObjectAccess.Id(connected), (string)state["connectedBody"]["objectId"]);
            Assert.IsTrue(RuntimeInspection.ComponentValues(connected)["isKinematic"].Value<bool>());
        }

        [Test]
        public void RuntimeAssertionsCompareEquivalentJsonNumbersExactly()
        {
            Assert.IsTrue(ProjectDiagnostics.ExactRuntimeValueEquals(new JValue(1.0f), new JValue(1)));
            Assert.IsTrue(ProjectDiagnostics.ExactRuntimeValueEquals(new JValue(1.0d), new JValue(1L)));
            Assert.IsFalse(ProjectDiagnostics.ExactRuntimeValueEquals(new JValue(1.5f), new JValue(1)));
            Assert.IsFalse(ProjectDiagnostics.ExactRuntimeValueEquals(new JValue(true), new JValue(1)));
            Assert.IsFalse(ProjectDiagnostics.ExactRuntimeValueEquals(new JValue("1"), new JValue(1)));
            Assert.IsTrue(ProjectDiagnostics.ExactRuntimeValueEquals(
                JObject.Parse("{\"position\":{\"x\":1.0,\"y\":2},\"values\":[3.0,false]}"),
                JObject.Parse("{\"position\":{\"x\":1,\"y\":2.0},\"values\":[3,false]}")));
            Assert.IsFalse(ProjectDiagnostics.ExactRuntimeValueEquals(
                JObject.Parse("{\"position\":{\"x\":1,\"y\":2}}"),
                JObject.Parse("{\"position\":{\"x\":1,\"y\":3}}")));
        }

        [Test]
        public void ScriptApprovalRejectsStaleContentWithoutOverwritingExternalEdit()
        {
            // Fixture-only file: do not import or refresh it, and delete it after testing.
            bool previousPermission = BridgePreferences.EnableScriptChangeProposals;
            string assetPath = _folder + "/StaleApprovalProbe.cs";
            string fullPath = ProjectSecurity.ResolveAssetFile(assetPath, true);
            const string original = "// temporary stale approval fixture";
            const string externalEdit = "// externally changed since proposal";
            try
            {
                BridgePreferences.EnableScriptChangeProposals = true;
                File.WriteAllText(fullPath, original);
                string originalSha = ScriptChangeManager.ComputeFileSha256(assetPath);
                JObject proposal = ScriptChangeManager.CreateTextPatchProposal(
                    assetPath, originalSha, "temporary", "proposed", "Stale approval regression fixture");
                Assert.AreEqual("Pending", (string)proposal["status"]);

                File.WriteAllText(fullPath, externalEdit);
                Assert.IsFalse(ScriptChangeManager.Approve((string)proposal["id"], out string message));
                StringAssert.Contains("changed after the proposal", message);
                Assert.AreEqual(externalEdit, File.ReadAllText(fullPath),
                    "Rejected stale approvals must preserve external edits.");
                ScriptChangeProposal stored = ScriptChangeManager.Snapshot()
                    .Single(p => p.Id == (string)proposal["id"]);
                Assert.AreEqual("Failed", stored.Status);
            }
            finally
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
                BridgePreferences.EnableScriptChangeProposals = previousPermission;
            }
        }

        [Test]
        public void InterruptedIpcRecoveryReturnsExplicitErrorWithoutReplayingRequest()
        {
            // Isolated Library paths: never mutate the production IPC queue.
            string root = Path.Combine(ProjectSecurity.ProjectRoot, "Library", "WasimUnityMcpBridge",
                "Tests", "IpcRecovery_" + Guid.NewGuid().ToString("N"));
            string processing = Path.Combine(root, "Processing");
            string responses = Path.Combine(root, "Responses");
            const string interrupted = "interrupted-fixture";
            const string completed = "already-completed-fixture";
            try
            {
                Directory.CreateDirectory(processing);
                Directory.CreateDirectory(responses);
                File.WriteAllText(Path.Combine(processing, interrupted + ".json"), "{}");
                File.WriteAllText(Path.Combine(processing, completed + ".json"), "{}");
                string priorResponse = new JObject
                {
                    ["id"] = completed,
                    ["success"] = true,
                    ["result"] = new JObject()
                }.ToString(Newtonsoft.Json.Formatting.None);
                File.WriteAllText(Path.Combine(responses, completed + ".json"), priorResponse);

                CompanionIpc.RecoverInterruptedRequestsInDirectories(processing, responses);

                Assert.IsFalse(File.Exists(Path.Combine(processing, interrupted + ".json")));
                Assert.IsFalse(File.Exists(Path.Combine(processing, completed + ".json")));
                JObject result = JObject.Parse(File.ReadAllText(Path.Combine(responses, interrupted + ".json")));
                Assert.AreEqual(interrupted, (string)result["id"]);
                Assert.IsFalse(result["success"].Value<bool>());
                StringAssert.Contains("Unity reloaded during this request", (string)result["error"]);
                Assert.AreEqual(priorResponse, File.ReadAllText(Path.Combine(responses, completed + ".json")));

                // Repeating recovery must not rewrite or replay completed requests.
                CompanionIpc.RecoverInterruptedRequestsInDirectories(processing, responses);
                Assert.AreEqual(priorResponse, File.ReadAllText(Path.Combine(responses, completed + ".json")));
                Assert.AreEqual(0, Directory.GetFiles(processing, "*.json").Length);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ReadScriptReturnsSourceTextAndSha256OverMcpToolPath()
        {
            bool old = BridgePreferences.AllowPackageScripts;
            try
            {
                BridgePreferences.AllowPackageScripts = true;
                JObject result = UnityToolExecutor.Execute("unity_read_script", new JObject
                {
                    ["path"] = "Packages/com.musafirex.mcp2unity/Editor/BridgeVersion.cs",
                    ["startLine"] = 1,
                    ["endLine"] = 8
                }) as JObject;
                Assert.NotNull(result);
                Assert.IsNull(result.Property("content"), "Avoid the reserved MCP content field in serialized tool results.");
                StringAssert.StartsWith("Packages/com.musafirex.mcp2unity/", (string)result["path"]);
                StringAssert.Contains("BridgeVersion", (string)result["sourceText"]);
                StringAssert.Contains("1: ", (string)result["sourceText"]);
                Assert.Greater(result["lineCount"].Value<int>(), 0);
                Assert.AreEqual(1, result["startLine"].Value<int>());
                Assert.AreEqual(64, ((string)result["sha256"]).Length);
            }
            finally { BridgePreferences.AllowPackageScripts = old; }
        }

        [Test]
        public void SelectedScriptReturnsSameSourceTextAndSha256()
        {
            bool oldPermission = BridgePreferences.AllowPackageScripts;
            UnityEngine.Object oldSelection = Selection.activeObject;
            try
            {
                BridgePreferences.AllowPackageScripts = true;
                MonoScript selected = AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Packages/com.musafirex.mcp2unity/Editor/BridgeVersion.cs");
                Assert.NotNull(selected);
                Selection.activeObject = selected;
                JObject result = UnityToolExecutor.Execute("unity_get_selected_script", new JObject()) as JObject;
                Assert.NotNull(result);
                Assert.IsNull(result.Property("content"));
                Assert.AreEqual("Packages/com.musafirex.mcp2unity/Editor/BridgeVersion.cs",
                    (string)result["path"]);
                StringAssert.Contains("BridgeVersion", (string)result["sourceText"]);
                Assert.AreEqual(64, ((string)result["sha256"]).Length);
            }
            finally
            {
                Selection.activeObject = oldSelection;
                BridgePreferences.AllowPackageScripts = oldPermission;
            }
        }

        [Test]
        public void PackageScriptReaderResolvesInstalledPackage()
        {
            bool old = BridgePreferences.AllowPackageScripts;
            try
            {
                BridgePreferences.AllowPackageScripts = true;
                Assert.IsTrue(ProjectSecurity.TryResolveReadableScript("Packages/com.musafirex.mcp2unity/Editor/BridgeVersion.cs",
                    out string path, out string error), error);
                StringAssert.Contains("0.5.2", File.ReadAllText(path));
                Assert.IsFalse(ProjectSecurity.TryResolveReadableScript("Assets/../Library/hidden.cs", out _, out _));
            }
            finally { BridgePreferences.AllowPackageScripts = old; }
        }

        // ---- uGUI / Canvas MCP integration regressions ----

        private GameObject UiFixtureCanvas()
        {
            var canvas = new GameObject("UiFixtureCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvas, _root.scene);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            return canvas;
        }

        private JObject UiOp(string type, string name, string parent, string alias = null)
        {
            var op = new JObject { ["kind"] = "create_ui", ["uiType"] = type,
                ["name"] = name };
            if (parent != null) op["parentId"] = parent;
            if (alias != null) op["alias"] = alias;
            return op;
        }

        [Test]
        public void UiCatalogRegistersFourToolsWithCorrectPermissionHints()
        {
            var tools = McpToolCatalog.Build().OfType<JObject>().ToDictionary(t => (string)t["name"]);
            foreach (string tool in new[] { "unity_create_canvas", "unity_create_ui", "unity_set_rect_transform" })
                Assert.IsFalse(tools[tool]["annotations"]["readOnlyHint"].Value<bool>(), tool);
            Assert.IsTrue(tools["unity_inspect_ui"]["annotations"]["readOnlyHint"].Value<bool>());
        }

        [Test]
        public void UiCanvasProposalDoesNotMutateSceneUntilApproval()
        {
            int before = _root.scene.rootCount;
            JObject pending = (JObject)UnityToolExecutor.Execute("unity_create_canvas",
                new JObject { ["scenePath"] = _scenePath, ["name"] = "DeferredCanvas",
                    ["createEventSystem"] = false });
            _proposals.Add((string)pending["id"]);
            Assert.AreEqual("Pending", (string)pending["status"]);
            Assert.AreEqual(before, _root.scene.rootCount);
            Assert.IsNull(_root.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "DeferredCanvas"));
        }

        [Test]
        public void UiCanvasCreatesRectScalerGraphicRaycasterAndEventSystem()
        {
            JObject set = Propose(UiOp("canvas", "FullCanvas", null, "$canvas"));
            ChangeSets.Approve((string)set["id"]);
            GameObject canvas = _root.scene.GetRootGameObjects().Single(g => g.name == "FullCanvas");
            Assert.IsInstanceOf<RectTransform>(canvas.transform);
            Assert.NotNull(canvas.GetComponent<Canvas>());
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.GetComponent<Canvas>().renderMode);
            Assert.NotNull(canvas.GetComponent<CanvasScaler>());
            Assert.NotNull(canvas.GetComponent<GraphicRaycaster>());
            Assert.NotNull(_root.scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<EventSystem>() != null));
        }

        [Test]
        public void UiCanvasCanSkipAutomaticEventSystem()
        {
            JObject op = UiOp("canvas", "NoEventCanvas", null);
            op["createEventSystem"] = false;
            JObject set = Propose(op);
            ChangeSets.Approve((string)set["id"]);
            Assert.NotNull(_root.scene.GetRootGameObjects().Single(g => g.name == "NoEventCanvas"));
            Assert.IsFalse(_root.scene.GetRootGameObjects().Any(g => g.GetComponent<EventSystem>() != null));
        }

        [Test]
        public void UiCanvasReferenceResolutionIsConfigurable()
        {
            JObject set = (JObject)ExtendedTools.Execute("unity_create_canvas", new JObject {
                ["name"] = "ResolutionCanvas", ["scenePath"] = _scenePath,
                ["createEventSystem"] = false,
                ["referenceResolution"] = new JObject { ["x"] = 1280, ["y"] = 720 }
            });
            _proposals.Add((string)set["id"]);
            ChangeSets.Approve((string)set["id"]);
            GameObject canvas = _root.scene.GetRootGameObjects().Single(g => g.name == "ResolutionCanvas");
            Assert.Less(Vector2.Distance(new Vector2(1280, 720), canvas.GetComponent<CanvasScaler>().referenceResolution), 0.01f);
        }

        [Test]
        public void UiCanvasRejectsParentingUnderAnotherObject()
        {
            Assert.Throws<ArgumentException>(() => Propose(UiOp("canvas", "IllegalCanvas", ObjectAccess.Id(_root))));
        }

        [Test]
        public void UiElementRejectsParentWithoutCanvasOrRectTransform()
        {
            Assert.Throws<ArgumentException>(() => Propose(UiOp("button", "OrphanButton", ObjectAccess.Id(_root))));
        }

        [Test]
        public void UiElementRejectsUnknownType()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(UiOp("dangerousCustom", "Bad", ObjectAccess.Id(canvas))));
        }

        [Test]
        public void UiPanelCreatesStretchingRectAndImage()
        {
            GameObject canvas = UiFixtureCanvas();
            JObject set = Propose(UiOp("panel", "BackgroundPanel", ObjectAccess.Id(canvas)));
            ChangeSets.Approve((string)set["id"]);
            Transform panel = canvas.transform.Find("BackgroundPanel");
            Assert.NotNull(panel);
            Assert.NotNull(panel.GetComponent<Image>());
            var rect = (RectTransform)panel;
            Assert.Less(Vector2.Distance(Vector2.zero, rect.anchorMin), 1e-4f);
            Assert.Less(Vector2.Distance(Vector2.one, rect.anchorMax), 1e-4f);
        }

        [Test]
        public void UiImageAcceptsExplicitColorAndPosition()
        {
            GameObject canvas = UiFixtureCanvas();
            var op = UiOp("image", "ColorImage", ObjectAccess.Id(canvas));
            op["color"] = new JObject { ["r"] = 0.2, ["g"] = 0.4, ["b"] = 0.6, ["a"] = 0.8 };
            op["width"] = 128;
            op["height"] = 64;
            op["anchoredPosition"] = new JObject { ["x"] = 12, ["y"] = -34 };
            JObject set = Propose(op);
            ChangeSets.Approve((string)set["id"]);
            var image = canvas.transform.Find("ColorImage").GetComponent<Image>();
            Assert.Less(Mathf.Abs(image.color.r - 0.2f), 0.001f);
            Assert.Less(Mathf.Abs(image.color.a - 0.8f), 0.001f);
            Assert.Less(Vector2.Distance(new Vector2(12,-34), ((RectTransform)image.transform).anchoredPosition), 0.01f);
        }

        [Test]
        public void UiButtonCreatesClickableImageAndReadableLabel()
        {
            GameObject canvas = UiFixtureCanvas();
            var op = UiOp("button", "SaveButton", ObjectAccess.Id(canvas));
            op["text"] = "Save Changes";
            JObject set = Propose(op);
            ChangeSets.Approve((string)set["id"]);
            Transform button = canvas.transform.Find("SaveButton");
            Assert.NotNull(button.GetComponent<Button>());
            Assert.NotNull(button.GetComponent<Image>());
            Text label = button.Find("Text").GetComponent<Text>();
            Assert.AreEqual("Save Changes", label.text);
            Assert.IsFalse(label.raycastTarget);
        }

        [Test]
        public void UiTextCreatesFontAndContent()
        {
            GameObject canvas = UiFixtureCanvas();
            var op = UiOp("text", "TitleText", ObjectAccess.Id(canvas));
            op["text"] = "Main Menu";
            op["fontSize"] = 36;
            JObject set = Propose(op);
            ChangeSets.Approve((string)set["id"]);
            Text text = canvas.transform.Find("TitleText").GetComponent<Text>();
            Assert.NotNull(text.font);
            Assert.AreEqual("Main Menu", text.text);
            Assert.AreEqual(36, text.fontSize);
        }

        [Test]
        public void UiTmpTextCreatesExpectedContent()
        {
            GameObject canvas = UiFixtureCanvas();
            var op = UiOp("tmpText", "Subtitle", ObjectAccess.Id(canvas));
            op["text"] = "MCP2UNITY";
            JObject set = Propose(op);
            ChangeSets.Approve((string)set["id"]);
            TextMeshProUGUI text = canvas.transform.Find("Subtitle").GetComponent<TextMeshProUGUI>();
            Assert.NotNull(text);
            Assert.AreEqual("MCP2UNITY", text.text);
        }

        [Test]
        public void UiAliasesCreateCanvasPanelAndButtonInOneReview()
        {
            JObject set = Propose(
                UiOp("canvas", "AliasCanvas", null, "$canvas"),
                UiOp("panel", "AliasPanel", "$canvas", "$panel"),
                UiOp("button", "AliasButton", "$panel", "$button"));
            Assert.IsNull(_root.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "AliasCanvas"));
            ChangeSets.Approve((string)set["id"]);
            GameObject canvas = _root.scene.GetRootGameObjects().Single(g => g.name == "AliasCanvas");
            Assert.NotNull(canvas.transform.Find("AliasPanel/AliasButton").GetComponent<Button>());
            JObject applied = ChangeSets.List(true).OfType<JObject>().Single(c => (string)c["id"] == (string)set["id"]);
            Assert.NotNull(applied["createdObjects"]["$canvas"]);
            Assert.NotNull(applied["createdObjects"]["$button"]);
        }

        [Test]
        public void UiRectTransformToolProposesAndAppliesAnchorAndPivot()
        {
            GameObject canvas = UiFixtureCanvas();
            var element = new GameObject("LayoutTarget", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(element, _root.scene);
            element.transform.SetParent(canvas.transform, false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            JObject set = (JObject)ExtendedTools.Execute("unity_set_rect_transform", new JObject {
                ["objectId"] = ObjectAccess.Id(element),
                ["anchorMin"] = new JObject { ["x"] = 0, ["y"] = 0 },
                ["anchorMax"] = new JObject { ["x"] = 1, ["y"] = 1 },
                ["pivot"] = new JObject { ["x"] = 0.25, ["y"] = 0.75 }
            });
            _proposals.Add((string)set["id"]);
            Assert.AreEqual("Pending", (string)set["status"]);
            ChangeSets.Approve((string)set["id"]);
            var rect = (RectTransform)element.transform;
            Assert.Less(Vector2.Distance(Vector2.zero, rect.anchorMin), 1e-4f);
            Assert.Less(Vector2.Distance(Vector2.one, rect.anchorMax), 1e-4f);
            Assert.Less(Vector2.Distance(new Vector2(.25f,.75f), rect.pivot), 1e-4f);
        }

        [Test]
        public void UiRectTransformRejectsOrdinaryTransform()
        {
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_rect_transform", new JObject {
                ["objectId"] = ObjectAccess.Id(_root),
                ["sizeDelta"] = new JObject { ["x"] = 100, ["y"] = 50 }
            }));
        }

        [Test]
        public void UiInspectReadsCanvasAndButtonHierarchy()
        {
            GameObject canvas = UiFixtureCanvas();
            var op = UiOp("button", "InspectMe", ObjectAccess.Id(canvas));
            JObject set = Propose(op);
            ChangeSets.Approve((string)set["id"]);
            JObject tree = (JObject)ExtendedTools.Execute("unity_inspect_ui", new JObject {
                ["objectId"] = ObjectAccess.Id(canvas), ["maximumObjects"] = 10
            });
            JArray nodes = (JArray)tree["nodes"];
            Assert.GreaterOrEqual(nodes.Count, 2);
            JObject button = nodes.OfType<JObject>().Single(n => (string)n["name"] == "InspectMe");
            Assert.IsTrue(button["interactable"].Value<bool>());
            Assert.AreEqual(2, ((JObject)button["anchorMin"]).Count);
        }

        [Test]
        public void UiRollbackRestoresSceneAfterCreatingCanvas()
        {
            int before = _root.scene.rootCount;
            JObject set = Propose(UiOp("canvas", "RollbackCanvas", null, "$rollback"));
            ChangeSets.Approve((string)set["id"]);
            Assert.Greater(_root.scene.rootCount, before);
            if (_root.scene.isDirty) Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            JObject rollback = ChangeSets.ProposeRollback((string)set["id"], "Clean temporary Canvas");
            _proposals.Add((string)rollback["id"]);
            ChangeSets.Approve((string)rollback["id"]);
            Scene restored = SceneManager.GetSceneByPath(_scenePath);
            Assert.AreEqual(before, restored.rootCount);
            Assert.IsNull(restored.GetRootGameObjects().FirstOrDefault(g => g.name == "RollbackCanvas"));
        }

        [Test]
        public void UiStaleSceneGuardRejectsCanvasCreation()
        {
            JObject set = Propose(UiOp("canvas", "StaleCanvas", null));
            _root.name = "ChangedSinceProposal";
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            Assert.Throws<InvalidOperationException>(() => ChangeSets.Approve((string)set["id"]));
            Assert.IsNull(_root.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "StaleCanvas"));
        }


        // ---- uGUI Phase 2: Styling & Layout ----

        private GameObject UiStyleFixture(Type componentType)
        {
            GameObject canvas = UiFixtureCanvas();
            var element = new GameObject("UiPhase2Element", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(element, _root.scene);
            element.transform.SetParent(canvas.transform, false);
            element.AddComponent(componentType);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            return element;
        }

        private JObject UiStyleProposal(GameObject go, string property, JToken value)
        {
            var args = new JObject { ["objectId"] = ObjectAccess.Id(go), [property] = value };
            JObject result = (JObject)ExtendedTools.Execute("unity_set_ui_property", args);
            _proposals.Add((string)result["id"]);
            return result;
        }

        private JObject UiLayoutProposal(GameObject go, JObject options)
        {
            options["objectId"] = ObjectAccess.Id(go);
            JObject result = (JObject)ExtendedTools.Execute("unity_set_ui_layout", options);
            _proposals.Add((string)result["id"]);
            return result;
        }

        [Test]
        public void UiPhase2CatalogRegistersBothWriteTools()
        {
            var tools = McpToolCatalog.Build().OfType<JObject>()
                .ToDictionary(x => (string)x["name"]);
            foreach (string name in new[] { "unity_set_ui_property", "unity_set_ui_layout" })
            {
                Assert.IsTrue(tools.ContainsKey(name));
                Assert.IsFalse(tools[name]["annotations"]["readOnlyHint"].Value<bool>());
            }
        }

        [Test]
        public void UiStyleLegacyTextAndFontSizeUseReviewedChangeSets()
        {
            GameObject go = UiStyleFixture(typeof(Text));
            JObject proposal = (JObject)ExtendedTools.Execute("unity_set_ui_property",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["text"] = "Updated through MCP", ["fontSize"] = 32 });
            _proposals.Add((string)proposal["id"]);
            Assert.AreEqual("Pending", (string)proposal["status"]);
            Assert.AreNotEqual("Updated through MCP", go.GetComponent<Text>().text);
            ChangeSets.Approve((string)proposal["id"]);
            Assert.AreEqual("Updated through MCP", go.GetComponent<Text>().text);
            Assert.AreEqual(32, go.GetComponent<Text>().fontSize);
        }

        [Test]
        public void UiStyleTmpTextAndFontSizeAreWritable()
        {
            GameObject go = UiStyleFixture(typeof(TextMeshProUGUI));
            JObject set = (JObject)ExtendedTools.Execute("unity_set_ui_property",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["text"] = "TMP label", ["fontSize"] = 27 });
            _proposals.Add((string)set["id"]);
            ChangeSets.Approve((string)set["id"]);
            Assert.AreEqual("TMP label", go.GetComponent<TextMeshProUGUI>().text);
            Assert.AreEqual(27, go.GetComponent<TextMeshProUGUI>().fontSize, 0.01f);
        }

        [Test]
        public void UiStyleImageColorIsAppliedAfterApproval()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiStyleProposal(go, "color", new JObject {
                ["r"] = .15, ["g"] = .30, ["b"] = .45, ["a"] = .60 });
            Assert.AreEqual(Color.white, go.GetComponent<Image>().color);
            ChangeSets.Approve((string)set["id"]);
            Color color = go.GetComponent<Image>().color;
            Assert.AreEqual(.15f, color.r, .002f);
            Assert.AreEqual(.60f, color.a, .002f);
        }

        [Test]
        public void UiStyleButtonInteractableIsEditable()
        {
            GameObject go = UiStyleFixture(typeof(Button));
            Assert.IsTrue(go.GetComponent<Button>().interactable);
            JObject set = UiStyleProposal(go, "interactable", new JValue(false));
            ChangeSets.Approve((string)set["id"]);
            Assert.IsFalse(go.GetComponent<Button>().interactable);
        }

        [Test]
        public void UiStyleGraphicRaycastTargetIsEditable()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiStyleProposal(go, "raycastTarget", new JValue(false));
            ChangeSets.Approve((string)set["id"]);
            Assert.IsFalse(go.GetComponent<Image>().raycastTarget);
        }

        [Test]
        public void UiStyleFillAmountIsEditable()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiStyleProposal(go, "fillAmount", new JValue(.42));
            ChangeSets.Approve((string)set["id"]);
            Assert.AreEqual(.42f, go.GetComponent<Image>().fillAmount, .002f);
        }

        [Test]
        public void UiStyleRejectsNonUguiTransform()
        {
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_ui_property",
                new JObject { ["objectId"] = ObjectAccess.Id(_root), ["text"] = "wrong" }));
        }

        [Test]
        public void UiStyleRejectsNonGraphicColorWithoutSceneMutation()
        {
            GameObject go = UiStyleFixture(typeof(Button));
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_ui_property",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["color"] = new JObject { ["r"] = 1, ["g"] = .5, ["b"] = 0, ["a"] = 1 } }));
            Assert.IsFalse(_root.scene.isDirty);
        }

        [Test]
        public void UiStyleRejectsOutOfRangeColor()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_ui_property",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["color"] = new JObject { ["r"] = 5, ["g"] = 0, ["b"] = 0, ["a"] = 1 } }));
            Assert.IsFalse(_root.scene.isDirty);
        }

        [Test]
        public void UiLayoutVerticalAddsGroupAndSpacingAndPadding()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiLayoutProposal(go, new JObject {
                ["layoutType"] = "vertical", ["spacing"] = 12,
                ["padding"] = new JObject { ["left"] = 5, ["right"] = 6,
                    ["top"] = 7, ["bottom"] = 8 },
                ["childControlWidth"] = true, ["childForceExpandHeight"] = false
            });
            Assert.IsNull(go.GetComponent<VerticalLayoutGroup>());
            ChangeSets.Approve((string)set["id"]);
            VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
            Assert.NotNull(layout);
            Assert.AreEqual(12f, layout.spacing);
            Assert.AreEqual(5, layout.padding.left);
            Assert.AreEqual(8, layout.padding.bottom);
            Assert.IsTrue(layout.childControlWidth);
            Assert.IsFalse(layout.childForceExpandHeight);
        }

        [Test]
        public void UiLayoutHorizontalAddsGroup()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiLayoutProposal(go, new JObject {
                ["layoutType"] = "horizontal", ["spacing"] = 9
            });
            ChangeSets.Approve((string)set["id"]);
            Assert.AreEqual(9f, go.GetComponent<HorizontalLayoutGroup>().spacing);
        }

        [Test]
        public void UiLayoutGridSizeAndConstraintsAreApplied()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiLayoutProposal(go, new JObject {
                ["layoutType"] = "grid", ["spacing"] = 4,
                ["cellSize"] = new JObject { ["x"] = 130, ["y"] = 72 },
                ["constraint"] = "fixedColumnCount", ["constraintCount"] = 3
            });
            ChangeSets.Approve((string)set["id"]);
            GridLayoutGroup grid = go.GetComponent<GridLayoutGroup>();
            Assert.NotNull(grid);
            Assert.AreEqual(new Vector2(130, 72), grid.cellSize);
            Assert.AreEqual(new Vector2(4, 4), grid.spacing);
            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
            Assert.AreEqual(3, grid.constraintCount);
        }

        [Test]
        public void UiLayoutRejectsDifferentExistingLayoutType()
        {
            GameObject go = UiStyleFixture(typeof(VerticalLayoutGroup));
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_ui_layout",
                new JObject { ["objectId"] = ObjectAccess.Id(go), ["layoutType"] = "horizontal" }));
        }

        [Test]
        public void UiLayoutRejectsNonCanvasGameObject()
        {
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_ui_layout",
                new JObject { ["objectId"] = ObjectAccess.Id(_root), ["layoutType"] = "vertical" }));
        }

        [Test]
        public void UiLayoutRejectsInvalidGridSize()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_set_ui_layout",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["layoutType"] = "grid",
                    ["cellSize"] = new JObject { ["x"] = 0, ["y"] = 72 } }));
        }

        [Test]
        public void UiInspectIncludesLayoutSettings()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiLayoutProposal(go, new JObject {
                ["layoutType"] = "vertical", ["spacing"] = 15
            });
            ChangeSets.Approve((string)set["id"]);
            JObject inspection = (JObject)ExtendedTools.Execute("unity_inspect_ui",
                new JObject { ["objectId"] = ObjectAccess.Id(go) });
            JObject node = (JObject)((JArray)inspection["nodes"])[0];
            Assert.AreEqual("vertical", (string)node["layoutType"]);
            Assert.AreEqual(15f, node["spacing"].Value<float>());
        }

        [Test]
        public void UiLayoutChangeSetRollbackRemovesGroup()
        {
            GameObject go = UiStyleFixture(typeof(Image));
            JObject set = UiLayoutProposal(go, new JObject {
                ["layoutType"] = "vertical", ["spacing"] = 5
            });
            ChangeSets.Approve((string)set["id"]);
            Assert.NotNull(go.GetComponent<VerticalLayoutGroup>());
            if (_root.scene.isDirty) Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            JObject rollback = ChangeSets.ProposeRollback((string)set["id"], "Restore layout fixture");
            _proposals.Add((string)rollback["id"]);
            ChangeSets.Approve((string)rollback["id"]);
            GameObject original = SceneManager.GetSceneByPath(_scenePath).GetRootGameObjects()
                .SelectMany(x => ObjectAccess.Walk(x, 20)).Single(x => x.name == "UiPhase2Element");
            Assert.IsNull(original.GetComponent<VerticalLayoutGroup>());
        }


        // ---- Phase 3: advanced uGUI controls, safe deletion, and Button events ----

        private GameObject UiPhase3Child(string name, GameObject parent, Type componentType = null)
        {
            var child = new GameObject(name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(child, _root.scene);
            child.transform.SetParent(parent.transform, false);
            if (componentType != null) child.AddComponent(componentType);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            return child;
        }

        private JObject UiPhase3Propose(string kind, string name, JObject options = null)
        {
            GameObject canvas = UiFixtureCanvas();
            JObject operation = UiOp(kind, name, ObjectAccess.Id(canvas));
            if (options != null)
                foreach (JProperty property in options.Properties())
                    operation[property.Name] = property.Value.DeepClone();
            return Propose(operation);
        }

        private GameObject UiPhase3Find(string name)
        {
            Scene scene = SceneManager.GetSceneByPath(_scenePath);
            return scene.GetRootGameObjects()
                .SelectMany(x => ObjectAccess.Walk(x, 150))
                .Single(x => x.name == name);
        }

        [Test]
        public void UiPhase3CatalogAdvertisesAdvancedControlsAndSafeActions()
        {
            var tools = McpToolCatalog.Build().OfType<JObject>().ToDictionary(t => (string)t["name"]);
            Assert.AreEqual(47, tools.Count);
            foreach (string name in new[] { "unity_bind_button_event", "unity_delete_gameobject" })
            {
                Assert.IsFalse(tools[name]["annotations"]["readOnlyHint"].Value<bool>());
            }
            Assert.IsTrue(tools["unity_delete_gameobject"]["annotations"]["destructiveHint"].Value<bool>());
            JArray allowed = (JArray)tools["unity_create_ui"]["inputSchema"]["properties"]["uiType"]["enum"];
            foreach (string kind in new[] { "slider", "toggle", "inputField", "scrollView" })
                Assert.IsTrue(allowed.Any(x => (string)x == kind), kind);
        }

        [Test]
        public void UiSliderProposalDefersCreationAndLinksFillAndHandle()
        {
            JObject set = UiPhase3Propose("slider", "Phase3Slider");
            Assert.AreEqual("Pending", (string)set["status"]);
            Assert.IsNull(_root.scene.GetRootGameObjects()
                .SelectMany(x => ObjectAccess.Walk(x, 100)).FirstOrDefault(x => x.name == "Phase3Slider"));
            ChangeSets.Approve((string)set["id"]);
            GameObject go = UiPhase3Find("Phase3Slider");
            Slider slider = go.GetComponent<Slider>();
            Assert.NotNull(slider);
            Assert.NotNull(slider.fillRect);
            Assert.NotNull(slider.handleRect);
            Assert.NotNull(slider.targetGraphic);
            Assert.AreEqual(0.5f, slider.value, 0.001f);
        }

        [Test]
        public void UiSliderCustomRangeAndWholeNumbers()
        {
            JObject set = UiPhase3Propose("slider", "Phase3Slider", new JObject {
                ["minValue"] = 10, ["maxValue"] = 90, ["value"] = 42, ["wholeNumbers"] = true
            });
            ChangeSets.Approve((string)set["id"]);
            Slider slider = UiPhase3Find("Phase3Slider").GetComponent<Slider>();
            Assert.IsTrue(slider.wholeNumbers);
            Assert.AreEqual(10, slider.minValue);
            Assert.AreEqual(90, slider.maxValue);
            Assert.AreEqual(42, slider.value);
        }

        [Test]
        public void UiSliderRejectsInvertedRange()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(new JObject {
                ["kind"] = "create_ui", ["uiType"] = "slider", ["name"] = "BadSlider",
                ["parentId"] = ObjectAccess.Id(canvas), ["minValue"] = 10, ["maxValue"] = 2
            }));
            Assert.IsNull(canvas.transform.Find("BadSlider"));
        }

        [Test]
        public void UiSliderRejectsValueOutsideRange()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(new JObject {
                ["kind"] = "create_ui", ["uiType"] = "slider", ["name"] = "BadSlider",
                ["parentId"] = ObjectAccess.Id(canvas),
                ["minValue"] = 0, ["maxValue"] = 1, ["value"] = 1.5
            }));
        }

        [Test]
        public void UiToggleCreatesBackgroundCheckmarkAndLabel()
        {
            JObject set = UiPhase3Propose("toggle", "Phase3Toggle", new JObject {
                ["text"] = "Enable feature", ["isOn"] = true
            });
            ChangeSets.Approve((string)set["id"]);
            GameObject go = UiPhase3Find("Phase3Toggle");
            Toggle toggle = go.GetComponent<Toggle>();
            Assert.NotNull(toggle);
            Assert.IsTrue(toggle.isOn);
            Assert.NotNull(toggle.targetGraphic);
            Assert.NotNull(toggle.graphic);
            Assert.AreEqual("Enable feature", go.transform.Find("Label").GetComponent<Text>().text);
            Assert.NotNull(go.transform.Find("Background/Checkmark"));
        }

        [Test]
        public void UiToggleRejectsNonBoolean()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(new JObject {
                ["kind"] = "create_ui", ["uiType"] = "toggle", ["name"] = "BadToggle",
                ["parentId"] = ObjectAccess.Id(canvas), ["isOn"] = "yes"
            }));
        }

        [Test]
        public void UiInputFieldCreatesPlaceholderAndWorkingTextReference()
        {
            JObject set = UiPhase3Propose("inputField", "Phase3Input", new JObject {
                ["text"] = "Initial value", ["placeholder"] = "Type something...",
                ["characterLimit"] = 30
            });
            ChangeSets.Approve((string)set["id"]);
            InputField input = UiPhase3Find("Phase3Input").GetComponent<InputField>();
            Assert.NotNull(input);
            Assert.NotNull(input.textComponent);
            Assert.NotNull(input.placeholder);
            Assert.AreEqual("Initial value", input.text);
            Assert.AreEqual(30, input.characterLimit);
            Assert.AreEqual("Type something...", ((Text)input.placeholder).text);
        }

        [Test]
        public void UiInputFieldRejectsExcessiveCharacterLimit()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(new JObject {
                ["kind"] = "create_ui", ["uiType"] = "inputField", ["name"] = "BadInput",
                ["parentId"] = ObjectAccess.Id(canvas), ["characterLimit"] = 9999
            }));
        }

        [Test]
        public void UiInputFieldRejectsLongPlaceholder()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(new JObject {
                ["kind"] = "create_ui", ["uiType"] = "inputField", ["name"] = "BadInput",
                ["parentId"] = ObjectAccess.Id(canvas), ["placeholder"] = new string('x', 513)
            }));
        }

        [Test]
        public void UiScrollViewCreatesViewportMaskAndContent()
        {
            JObject set = UiPhase3Propose("scrollView", "Phase3Scroll", new JObject {
                ["vertical"] = true, ["horizontal"] = false, ["contentHeight"] = 720
            });
            ChangeSets.Approve((string)set["id"]);
            GameObject go = UiPhase3Find("Phase3Scroll");
            ScrollRect scroll = go.GetComponent<ScrollRect>();
            Assert.NotNull(scroll);
            Assert.NotNull(scroll.viewport);
            Assert.NotNull(scroll.content);
            Assert.NotNull(scroll.viewport.GetComponent<RectMask2D>());
            Assert.AreEqual(720, scroll.content.sizeDelta.y, 0.001f);
            Assert.IsTrue(scroll.vertical);
            Assert.IsFalse(scroll.horizontal);
            Assert.AreEqual("Viewport", scroll.viewport.name);
            Assert.AreEqual("Content", scroll.content.name);
        }

        [Test]
        public void UiScrollViewCanCreateChildUnderSavedContent()
        {
            JObject set = UiPhase3Propose("scrollView", "Phase3Scroll");
            ChangeSets.Approve((string)set["id"]);
            GameObject content = UiPhase3Find("Phase3Scroll").transform.Find("Viewport/Content").gameObject;
            JObject child = Propose(UiOp("text", "ScrollItem", ObjectAccess.Id(content)));
            ChangeSets.Approve((string)child["id"]);
            Assert.AreEqual("ScrollItem", content.transform.GetChild(0).name);
        }

        [Test]
        public void UiScrollViewRejectsBothDirectionsDisabled()
        {
            GameObject canvas = UiFixtureCanvas();
            Assert.Throws<ArgumentException>(() => Propose(new JObject {
                ["kind"] = "create_ui", ["uiType"] = "scrollView", ["name"] = "BadScroll",
                ["parentId"] = ObjectAccess.Id(canvas),
                ["vertical"] = false, ["horizontal"] = false
            }));
        }

        [Test]
        public void UiInspectReportsAdvancedControlSettings()
        {
            JObject set = UiPhase3Propose("slider", "Phase3Slider", new JObject {
                ["value"] = .8, ["minValue"] = 0, ["maxValue"] = 1
            });
            ChangeSets.Approve((string)set["id"]);
            JObject view = (JObject)ExtendedTools.Execute("unity_inspect_ui",
                new JObject { ["objectId"] = ObjectAccess.Id(UiPhase3Find("Phase3Slider")) });
            JObject node = (JObject)((JArray)view["nodes"])[0];
            Assert.AreEqual(.8f, node["sliderValue"].Value<float>(), .001f);
            Assert.NotNull(node["fillRect"]);
            Assert.NotNull(node["handleRect"]);
        }

        [Test]
        public void UiDeleteRequiresExactName()
        {
            var go = new GameObject("DoNotDelete");
            SceneManager.MoveGameObjectToScene(go, _root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_delete_gameobject",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["confirmedName"] = "Different" }));
            Assert.NotNull(UiPhase3Find("DoNotDelete"));
        }

        [Test]
        public void UiDeleteRejectsSubtreeBeyondLimit()
        {
            var go = new GameObject("LargeTree");
            SceneManager.MoveGameObjectToScene(go, _root.scene);
            for (int i = 0; i < 3; i++)
            {
                var node = new GameObject("Node" + i);
                SceneManager.MoveGameObjectToScene(node, _root.scene);
                node.transform.SetParent(go.transform);
            }
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_delete_gameobject",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["confirmedName"] = go.name, ["maximumObjects"] = 2 }));
            Assert.NotNull(UiPhase3Find("LargeTree"));
        }

        [Test]
        public void UiDeleteOnlyRemovesTargetAfterApproval()
        {
            var go = new GameObject("TemporaryObject");
            SceneManager.MoveGameObjectToScene(go, _root.scene);
            var childObject = new GameObject("TemporaryChild");
            SceneManager.MoveGameObjectToScene(childObject, _root.scene);
            childObject.transform.SetParent(go.transform);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            JObject set = (JObject)ExtendedTools.Execute("unity_delete_gameobject",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["confirmedName"] = "TemporaryObject", ["maximumObjects"] = 3 });
            _proposals.Add((string)set["id"]);
            Assert.NotNull(UiPhase3Find("TemporaryObject"));
            ChangeSets.Approve((string)set["id"]);
            Assert.IsNull(_root.scene.GetRootGameObjects()
                .FirstOrDefault(x => x.name == "TemporaryObject"));
            Assert.AreEqual("BridgeTestRoot", _root.name);
        }

        [Test]
        public void UiDeleteGuardedRollbackRestoresTarget()
        {
            var go = new GameObject("TemporaryObject");
            SceneManager.MoveGameObjectToScene(go, _root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            JObject set = (JObject)ExtendedTools.Execute("unity_delete_gameobject",
                new JObject { ["objectId"] = ObjectAccess.Id(go),
                    ["confirmedName"] = "TemporaryObject", ["maximumObjects"] = 2 });
            _proposals.Add((string)set["id"]);
            ChangeSets.Approve((string)set["id"]);
            Scene scene = SceneManager.GetSceneByPath(_scenePath);
            if (scene.isDirty) Assert.IsTrue(EditorSceneManager.SaveScene(scene));
            JObject rollback = ChangeSets.ProposeRollback((string)set["id"], "Test delete rollback");
            _proposals.Add((string)rollback["id"]);
            ChangeSets.Approve((string)rollback["id"]);
            Scene loaded = SceneManager.GetSceneByPath(_scenePath);
            Assert.NotNull(loaded.GetRootGameObjects()
                .FirstOrDefault(x => x.name == "TemporaryObject"));
        }

        private GameObject[] UiPhase3ButtonFixture()
        {
            GameObject canvas = UiFixtureCanvas();
            GameObject button = UiPhase3Child("EventButton", canvas, typeof(Button));
            GameObject target = UiPhase3Child("EventTarget", canvas, typeof(Image));
            return new[] { button, target };
        }

        [Test]
        public void UiButtonEventIsDeferredUntilReviewAndBindsSetActive()
        {
            GameObject[] objects = UiPhase3ButtonFixture();
            Button button = objects[0].GetComponent<Button>();
            Assert.AreEqual(0, button.onClick.GetPersistentEventCount());
            JObject set = (JObject)ExtendedTools.Execute("unity_bind_button_event", new JObject {
                ["buttonId"] = ObjectAccess.Id(objects[0]),
                ["targetId"] = ObjectAccess.Id(objects[1]),
                ["action"] = "setActive", ["value"] = false
            });
            _proposals.Add((string)set["id"]);
            Assert.AreEqual("Pending", (string)set["status"]);
            Assert.AreEqual(0, button.onClick.GetPersistentEventCount());
            ChangeSets.Approve((string)set["id"]);
            Assert.AreEqual(1, button.onClick.GetPersistentEventCount());
            Assert.AreEqual("SetActive", button.onClick.GetPersistentMethodName(0));
            Assert.AreSame(objects[1], button.onClick.GetPersistentTarget(0));
            Assert.IsTrue(objects[1].activeSelf);
        }

        [Test]
        public void UiButtonEventRejectsArbitraryMethod()
        {
            GameObject[] objects = UiPhase3ButtonFixture();
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_bind_button_event",
                new JObject { ["buttonId"] = ObjectAccess.Id(objects[0]),
                    ["targetId"] = ObjectAccess.Id(objects[1]),
                    ["action"] = "InvokeAnything", ["value"] = true }));
        }

        [Test]
        public void UiButtonEventRejectsNonUiTarget()
        {
            GameObject[] objects = UiPhase3ButtonFixture();
            Assert.Throws<ArgumentException>(() => ExtendedTools.Execute("unity_bind_button_event",
                new JObject { ["buttonId"] = ObjectAccess.Id(objects[0]),
                    ["targetId"] = ObjectAccess.Id(_root),
                    ["action"] = "setActive", ["value"] = false }));
        }

        [Test]
        public void UiButtonEventInspectShowsPersistentListener()
        {
            GameObject[] objects = UiPhase3ButtonFixture();
            JObject set = (JObject)ExtendedTools.Execute("unity_bind_button_event", new JObject {
                ["buttonId"] = ObjectAccess.Id(objects[0]), ["targetId"] = ObjectAccess.Id(objects[1]),
                ["action"] = "setActive", ["value"] = false
            });
            _proposals.Add((string)set["id"]);
            ChangeSets.Approve((string)set["id"]);
            JObject details = (JObject)ExtendedTools.Execute("unity_inspect_ui",
                new JObject { ["objectId"] = ObjectAccess.Id(objects[0]) });
            JArray listeners = (JArray)((JArray)details["nodes"])[0]["onClickListeners"];
            Assert.AreEqual(1, listeners.Count);
            Assert.AreEqual("SetActive", (string)listeners[0]["method"]);
        }
    }
}
