using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    // Fixture uses only disposable Assets/WasimMcpTest_*/ controller assets.
    // No gameplay animator, scene objects or packages are modified.
    public sealed partial class BridgeIntegrationTests
    {
        private string CreateAnimatorFixture()
        {
            string path = _folder + "/McpAnimatorFixture.controller";
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            Assert.NotNull(controller);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("Idle");
            machine.defaultState = idle;
            AssetDatabase.SaveAssets();
            return path;
        }

        private JObject InspectAnimator(string path)
        {
            return (JObject)UnityToolExecutor.Execute("unity_inspect_animator_controller",
                new JObject { ["controllerPath"] = path });
        }

        private JObject ProposeAnimator(string path, JArray edits, string hash = null)
        {
            string expected = hash ?? (string)InspectAnimator(path)["sha256"];
            JObject proposal = (JObject)UnityToolExecutor.Execute("unity_propose_animator_controller_edit",
                new JObject {
                    ["path"] = path, ["expectedSha256"] = expected,
                    ["summary"] = "Animator integration test", ["edits"] = edits
                });
            _proposals.Add((string)proposal["id"]);
            return proposal;
        }

        [Test]
        public void AnimatorInspectorReadsControllerByAssetPathWithoutChangingSelection()
        {
            string path = CreateAnimatorFixture();
            UnityEngine.Object selection = Selection.activeObject;
            JObject snapshot = InspectAnimator(path);
            Assert.AreEqual("AssetPath", (string)snapshot["selectionKind"]);
            Assert.AreEqual(path, (string)snapshot["animatorControllerAssetPath"]);
            Assert.AreEqual(64, ((string)snapshot["sha256"]).Length);
            Assert.IsTrue(((JArray)snapshot["layers"]).Count >= 1);
            Assert.AreEqual("Idle",
                (string)snapshot["layers"][0]["stateMachine"]["defaultState"]);
            Assert.AreSame(selection, Selection.activeObject);
        }

        [Test]
        public void AnimatorEditsArePendingBeforeApprovalThenApplyInOrder()
        {
            string path = CreateAnimatorFixture();
            string layer = AnimatorControllerEditTool.Load(path).layers[0].name;
            var edits = new JArray(
                new JObject { ["action"] = "addParameter", ["name"] = "IsRunning",
                    ["parameterType"] = "Bool", ["defaultBool"] = false },
                new JObject { ["action"] = "setParameterDefault", ["name"] = "IsRunning",
                    ["defaultBool"] = true },
                new JObject { ["action"] = "addState", ["layer"] = layer,
                    ["name"] = "Run", ["speed"] = 1.5, ["writeDefaultValues"] = false },
                new JObject { ["action"] = "setDefaultState", ["layer"] = layer,
                    ["state"] = layer + "/Run" },
                new JObject { ["action"] = "addTransition", ["layer"] = layer,
                    ["source"] = layer + "/Idle", ["destination"] = layer + "/Run",
                    ["hasExitTime"] = false, ["duration"] = 0.1,
                    ["conditions"] = new JArray(new JObject {
                        ["parameter"] = "IsRunning", ["mode"] = "If"
                    }) }
            );
            JObject proposal = ProposeAnimator(path, edits);
            Assert.AreEqual("Pending", (string)proposal["status"]);
            AnimatorController before = AnimatorControllerEditTool.Load(path);
            Assert.IsFalse(before.parameters.Any(p => p.name == "IsRunning"));
            Assert.IsFalse(before.layers[0].stateMachine.states
                .Any(x => x.state.name == "Run"));
            ChangeSets.Approve((string)proposal["id"]);
            Assert.IsTrue(AnimatorControllerEditTool.Load(path).parameters
                .Any(p => p.name == "IsRunning" && p.defaultBool));
            AnimatorStateMachine graph = AnimatorControllerEditTool.Load(path).layers[0].stateMachine;
            AnimatorState run = graph.states.Single(x => x.state.name == "Run").state;
            AnimatorState idle = graph.states.Single(x => x.state.name == "Idle").state;
            Assert.AreSame(run, graph.defaultState);
            Assert.AreEqual(1.5f, run.speed, 0.0001f);
            Assert.IsFalse(run.writeDefaultValues);
            Assert.AreEqual(1, idle.transitions.Length);
            Assert.AreSame(run, idle.transitions[0].destinationState);
            Assert.IsFalse(idle.transitions[0].hasExitTime);
            Assert.AreEqual("IsRunning", idle.transitions[0].conditions[0].parameter);
        }

        [Test]
        public void AnimatorEditRejectsStaleHashWithoutChangingAsset()
        {
            string path = CreateAnimatorFixture();
            string oldHash = (string)InspectAnimator(path)["sha256"];
            AnimatorController controller = AnimatorControllerEditTool.Load(path);
            controller.AddParameter("ExternalChange", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();
            string newHash = (string)InspectAnimator(path)["sha256"];
            Assert.AreNotEqual(oldHash, newHash);
            Assert.Throws<InvalidOperationException>(() => ProposeAnimator(path,
                new JArray(new JObject { ["action"] = "addParameter",
                    ["name"] = "ShouldNotExist", ["parameterType"] = "Bool" }), oldHash));
            Assert.IsFalse(controller.parameters.Any(p => p.name == "ShouldNotExist"));
        }

        [Test]
        public void AnimatorEditsRequireAssetsControllerAndValidFields()
        {
            string path = CreateAnimatorFixture();
            Assert.Throws<ArgumentException>(() => AnimatorControllerEditTool.Load(
                "Packages/com.musafirex.mcp2unity/NotEditable.controller"));
            Assert.Throws<ArgumentException>(() => AnimatorControllerEditTool.Load(
                "Assets/InvalidFile.cs"));
            Assert.Throws<ArgumentException>(() => ProposeAnimator(path,
                new JArray(new JObject { ["action"] = "addParameter",
                    ["name"] = "Bad", ["parameterType"] = "NotAType" })));
            Assert.IsFalse(AnimatorControllerEditTool.Load(path).parameters
                .Any(p => p.name == "Bad"));
        }

        [Test]
        public void AnimatorTransitionTimingCanBeChangedAndRollbackRestoresController()
        {
            string path = CreateAnimatorFixture();
            AnimatorController c = AnimatorControllerEditTool.Load(path);
            AnimatorStateMachine machine = c.layers[0].stateMachine;
            AnimatorState idle = machine.states.Single(x => x.state.name == "Idle").state;
            AnimatorState jump = machine.AddState("Jump");
            AnimatorStateTransition transition = idle.AddTransition(jump);
            transition.hasExitTime = true;
            AssetDatabase.SaveAssets();
            string layer = c.layers[0].name;
            JObject proposal = ProposeAnimator(path, new JArray(
                new JObject { ["action"] = "setTransition", ["layer"] = layer,
                    ["source"] = "Idle", ["index"] = 0,
                    ["expectedDestination"] = "Jump",
                    ["hasExitTime"] = false, ["duration"] = 0.25,
                    ["hasFixedDuration"] = true }));
            ChangeSets.Approve((string)proposal["id"]);
            Assert.IsFalse(idle.transitions[0].hasExitTime);
            Assert.IsTrue(idle.transitions[0].hasFixedDuration);
            Assert.AreEqual(0.25f, idle.transitions[0].duration, 0.0001f);
            JObject rollback = ChangeSets.ProposeRollback((string)proposal["id"],
                "Restore Animator test");
            _proposals.Add((string)rollback["id"]);
            ChangeSets.Approve((string)rollback["id"]);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AnimatorController restored = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            AnimatorState restoredIdle = restored.layers[0].stateMachine.states
                .Single(x => x.state.name == "Idle").state;
            Assert.IsTrue(restoredIdle.transitions[0].hasExitTime);
        }
    }
}
