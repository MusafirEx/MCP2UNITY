using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    // Phase 6 security/recovery tests share the original disposable additive-scene
    // setup and teardown, without touching the user's gameplay scenes.
    public sealed partial class BridgeIntegrationTests
    {
        private GameObject Phase6Create(string name, GameObject parent = null)
        {
            GameObject go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _root.scene);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            return go;
        }

        private JObject Phase6DeleteOperation(GameObject go, int maximumObjects = 10)
        {
            return new JObject {
                ["kind"] = "delete_gameobject", ["targetId"] = ObjectAccess.Id(go),
                ["confirmedName"] = go.name, ["maximumObjects"] = maximumObjects
            };
        }

        [Test]
        public void Phase6RejectsTraversalAndMalformedAssetPaths()
        {
            foreach (string path in new[] {
                "Assets/../Library/probe.cs", "Assets//Probe.cs",
                "Assets/./Probe.cs", "Assets/MyFile.cs." })
            {
                Assert.Throws<ArgumentException>(() =>
                    ProjectSecurity.ResolveAssetFile(path, true), path);
            }
        }

        [Test]
        public void Phase6ForbidsWritingIntoInstalledPackages()
        {
            Assert.Throws<ArgumentException>(() =>
                ProjectSecurity.ResolveAssetFile(
                    "Packages/com.musafirex.mcp2unity/Editor/Injected.cs", true));
            Assert.Throws<ArgumentException>(() =>
                ProjectSecurity.ResolveAssetFile("Library/SilentEdit.cs", true));
        }

        [Test]
        public void Phase6RejectsTransientInstanceIdsForReviewedWrites()
        {
            JObject op = new JObject {
                ["kind"] = "set_property",
                ["targetId"] = "instance:" + _root.GetInstanceID(),
                ["propertyPath"] = "m_Layer", ["value"] = 4
            };
            Assert.Throws<ArgumentException>(() => ChangeSets.Propose(new JObject {
                ["summary"] = "Reject transient ID", ["operations"] = new JArray(op)
            }));
            Assert.AreEqual(0, _root.layer);
        }

        [Test]
        public void Phase6RejectsScriptIdentityPropertyWrites()
        {
            JObject op = new JObject {
                ["kind"] = "set_property", ["targetId"] = ObjectAccess.Id(_root),
                ["propertyPath"] = "m_Script", ["value"] = JValue.CreateNull()
            };
            Assert.Throws<ArgumentException>(() => ChangeSets.Propose(new JObject {
                ["summary"] = "Reject protected identity", ["operations"] = new JArray(op)
            }));
            Assert.IsFalse(_root.scene.isDirty);
        }

        [Test]
        public void Phase6RejectsDuplicateCreationAliasesBeforeApproval()
        {
            var first = new JObject {
                ["kind"] = "create_gameobject", ["name"] = "First",
                ["scenePath"] = _scenePath, ["alias"] = "$duplicate"
            };
            var second = new JObject {
                ["kind"] = "create_gameobject", ["name"] = "Second",
                ["scenePath"] = _scenePath, ["alias"] = "$duplicate"
            };
            Assert.Throws<ArgumentException>(() => ChangeSets.Propose(new JObject {
                ["summary"] = "Alias collision", ["operations"] = new JArray(first, second)
            }));
            Assert.IsFalse(_root.scene.GetRootGameObjects().Any(x => x.name == "First"));
            Assert.IsFalse(_root.scene.GetRootGameObjects().Any(x => x.name == "Second"));
        }

        [Test]
        public void Phase6RejectsMoreThanFiftyChangeSetOperations()
        {
            var ops = new JArray();
            for (int i = 0; i < 51; i++)
                ops.Add(new JObject {
                    ["kind"] = "create_gameobject", ["name"] = "Overflow" + i,
                    ["scenePath"] = _scenePath
                });
            Assert.Throws<ArgumentException>(() => ChangeSets.Propose(new JObject {
                ["summary"] = "Operation limit probe", ["operations"] = ops
            }));
            Assert.AreEqual(1, _root.scene.rootCount);
        }

        [Test]
        public void Phase6DirtyFixtureSceneBlocksProposalWithoutMutation()
        {
            string previousName = _root.name;
            try
            {
                _root.name = "Phase6UnsavedEdit";
                EditorSceneManager.MarkSceneDirty(_root.scene);
                Assert.IsTrue(_root.scene.isDirty);
                JObject op = new JObject {
                    ["kind"] = "set_property", ["targetId"] = ObjectAccess.Id(_root),
                    ["propertyPath"] = "m_Layer", ["value"] = 6
                };
                Assert.Throws<InvalidOperationException>(() => ChangeSets.Propose(new JObject {
                    ["summary"] = "Dirty scene guard", ["operations"] = new JArray(op)
                }));
                Assert.AreEqual(0, _root.layer);
            }
            finally
            {
                _root.name = previousName;
                EditorSceneManager.MarkSceneDirty(_root.scene);
                Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            }
        }

        [Test]
        public void Phase6DeletionSubtreeLimitCountsInactiveChildren()
        {
            GameObject parent = Phase6Create("Phase6DeletionParent");
            GameObject inactive = Phase6Create("Phase6InactiveChild", parent);
            inactive.SetActive(false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            JObject op = Phase6DeleteOperation(parent, 1);
            Assert.Throws<ArgumentException>(() => GameObjectDeleteTool.Validate(parent, op));
            Assert.NotNull(parent.transform.Find("Phase6InactiveChild"));
        }

        [Test]
        public void Phase6DeletionRejectsReparentedTargetAfterReview()
        {
            GameObject parent = Phase6Create("Phase6OriginalParent");
            GameObject target = Phase6Create("Phase6MovingTarget", parent);
            JObject op = Phase6DeleteOperation(target);
            GameObjectDeleteTool.Validate(target, op);
            target.transform.SetParent(null, false);
            Assert.Throws<InvalidOperationException>(() =>
                GameObjectDeleteTool.Validate(target, op));
            Assert.NotNull(target);
        }

        [Test]
        public void Phase6DeletionRejectsNewDescendantsAfterReview()
        {
            GameObject target = Phase6Create("Phase6ProtectedTree");
            JObject op = Phase6DeleteOperation(target, 10);
            GameObjectDeleteTool.Validate(target, op);
            GameObject child = Phase6Create("Phase6LateChild", target);
            child.SetActive(false);
            Assert.Throws<InvalidOperationException>(() =>
                GameObjectDeleteTool.Validate(target, op));
            Assert.AreSame(target.transform, child.transform.parent);
        }

        [Test]
        public void Phase6RollbackRefusesToOverwriteLaterSceneChanges()
        {
            JObject created = Propose(new JObject {
                ["kind"] = "create_gameobject", ["name"] = "Phase6RollbackProbe",
                ["scenePath"] = _scenePath
            });
            ChangeSets.Approve((string)created["id"]);
            _root.transform.localPosition = new Vector3(0, 0, 3);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(_root.scene));
            Assert.Throws<InvalidOperationException>(() =>
                ChangeSets.ProposeRollback((string)created["id"], "Unsafe rollback probe"));
            Assert.NotNull(_root.scene.GetRootGameObjects()
                .FirstOrDefault(x => x.name == "Phase6RollbackProbe"));
            Assert.AreEqual(new Vector3(0, 0, 3), _root.transform.localPosition);
        }

        [Test]
        public void Phase6InterruptedIpcRequestNeverReplaysAfterRecovery()
        {
            string directory = Path.Combine(ProjectSecurity.ProjectRoot, "Library",
                "McpPhase6Recovery_" + Guid.NewGuid().ToString("N"));
            string processing = Path.Combine(directory, "Processing");
            string responses = Path.Combine(directory, "Responses");
            Directory.CreateDirectory(processing);
            Directory.CreateDirectory(responses);
            const string requestId = "phase6-interrupted";
            string input = Path.Combine(processing, requestId + ".json");
            string output = Path.Combine(responses, requestId + ".json");
            try
            {
                File.WriteAllText(input, "{\"name\":\"unity_delete_gameobject\"}");
                CompanionIpc.RecoverInterruptedRequestsInDirectories(processing, responses);
                Assert.IsFalse(File.Exists(input), "Interrupted request must not be re-queued.");
                Assert.IsTrue(File.Exists(output));
                JObject result = AtomicFile.ReadObject(output);
                Assert.AreEqual(requestId, (string)result["id"]);
                Assert.IsFalse(result["success"].Value<bool>());
                StringAssert.Contains("reloaded", (string)result["error"]);
                // Recovery is idempotent, so a subsequent scan cannot replay work.
                string firstResponse = File.ReadAllText(output);
                CompanionIpc.RecoverInterruptedRequestsInDirectories(processing, responses);
                Assert.AreEqual(firstResponse, File.ReadAllText(output));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
