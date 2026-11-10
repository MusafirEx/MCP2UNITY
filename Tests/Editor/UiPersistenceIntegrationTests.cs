using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    // Phase 5: verified UI writes through the same MCP executor/ChangeSets path,
    // then persistence in Unity's serialized scene and prefab assets. All
    // fixtures are owned by BridgeIntegrationTests' disposable additive scene.
    public sealed partial class BridgeIntegrationTests
    {
        private GameObject Phase5Menu()
        {
            JObject root = UiOp("canvas", "Phase5Canvas", null, "$canvas");
            root["scenePath"] = _scenePath;
            root["createEventSystem"] = false;
            JObject set = Propose(root,
                UiOp("panel", "Phase5Panel", "$canvas", "$panel"),
                UiOp("button", "HideButton", "$panel", "$hide"),
                UiOp("button", "ShowButton", "$panel", "$show"),
                UiOp("image", "TargetPanel", "$panel", "$target"));
            ChangeSets.Approve((string)set["id"]);
            return UiPhase3Find("Phase5Canvas");
        }

        private void Phase5Save()
        {
            Scene s = SceneManager.GetSceneByPath(_scenePath);
            Assert.IsTrue(s.IsValid() && s.isLoaded);
            if (s.isDirty) Assert.IsTrue(EditorSceneManager.SaveScene(s));
        }

        private void Phase5Bind(GameObject button, GameObject target, bool value)
        {
            Phase5Save();
            JObject set = (JObject)ExtendedTools.Execute("unity_bind_button_event",
                new JObject { ["buttonId"] = ObjectAccess.Id(button),
                    ["targetId"] = ObjectAccess.Id(target),
                    ["action"] = "setActive", ["value"] = value });
            _proposals.Add((string)set["id"]);
            Assert.AreEqual("Pending", (string)set["status"]);
            ChangeSets.Approve((string)set["id"]);
        }

        private void Phase5Reload()
        {
            Phase5Save();
            Scene original = SceneManager.GetSceneByPath(_scenePath);
            // Keep a harmless scene loaded if the fixture was Unity's only scene.
            bool needPlaceholder = SceneManager.sceneCount == 1;
            Scene placeholder = default(Scene);
            if (needPlaceholder)
                placeholder = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Assert.IsTrue(EditorSceneManager.CloseScene(original, true));
            Scene reopened = EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Additive);
            _root = reopened.GetRootGameObjects().Single(g => g.name == "BridgeTestRoot");
            if (needPlaceholder && placeholder.IsValid() && placeholder.isLoaded)
                Assert.IsTrue(EditorSceneManager.CloseScene(placeholder, true));
        }

        [Test]
        public void UiPhase5McpDispatcherCreatesCanvasAndButton()
        {
            JObject canvasSet = (JObject)UnityToolExecutor.Execute("unity_create_canvas",
                new JObject { ["scenePath"] = _scenePath, ["name"] = "Phase5Dispatch",
                    ["createEventSystem"] = false });
            _proposals.Add((string)canvasSet["id"]);
            Assert.AreEqual("Pending", (string)canvasSet["status"]);
            Assert.IsFalse(_root.scene.GetRootGameObjects().Any(g => g.name == "Phase5Dispatch"));
            ChangeSets.Approve((string)canvasSet["id"]);
            GameObject canvas = UiPhase3Find("Phase5Dispatch");
            Phase5Save();
            JObject buttonSet = (JObject)UnityToolExecutor.Execute("unity_create_ui",
                new JObject { ["parentId"] = ObjectAccess.Id(canvas), ["uiType"] = "button",
                    ["name"] = "Phase5DispatchButton", ["text"] = "Open" });
            _proposals.Add((string)buttonSet["id"]);
            Assert.IsNull(canvas.transform.Find("Phase5DispatchButton"));
            ChangeSets.Approve((string)buttonSet["id"]);
            Assert.NotNull(canvas.transform.Find("Phase5DispatchButton").GetComponent<Button>());
            Assert.AreEqual("Open", canvas.transform.Find("Phase5DispatchButton/Text").GetComponent<Text>().text);
        }

        [Test]
        public void UiPhase5McpMenuAliasesCreateExpectedHierarchy()
        {
            GameObject canvas = Phase5Menu();
            Assert.NotNull(canvas.GetComponent<Canvas>());
            Assert.NotNull(canvas.transform.Find("Phase5Panel/HideButton").GetComponent<Button>());
            Assert.NotNull(canvas.transform.Find("Phase5Panel/ShowButton").GetComponent<Button>());
            Assert.NotNull(canvas.transform.Find("Phase5Panel/TargetPanel").GetComponent<Image>());
            JObject result = (JObject)ExtendedTools.Execute("unity_inspect_ui",
                new JObject { ["objectId"] = ObjectAccess.Id(canvas), ["maximumObjects"] = 12 });
            Assert.IsTrue(((JArray)result["nodes"]).OfType<JObject>()
                .Any(x => (string)x["name"] == "TargetPanel"));
        }

        [Test]
        public void UiPhase5PersistentHideButtonSurvivesPrefabSerialization()
        {
            GameObject canvas = Phase5Menu();
            Phase5Bind(canvas.transform.Find("Phase5Panel/HideButton").gameObject,
                canvas.transform.Find("Phase5Panel/TargetPanel").gameObject, false);
            Phase5Save();
            string prefabPath = _folder + "/Phase5Menu.prefab";
            Assert.NotNull(PrefabUtility.SaveAsPrefabAsset(canvas, prefabPath));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.NotNull(prefab);
            Button button = prefab.transform.Find("Phase5Panel/HideButton").GetComponent<Button>();
            GameObject target = prefab.transform.Find("Phase5Panel/TargetPanel").gameObject;
            Assert.AreEqual(1, button.onClick.GetPersistentEventCount());
            Assert.AreEqual("SetActive", button.onClick.GetPersistentMethodName(0));
            Assert.AreSame(target, button.onClick.GetPersistentTarget(0));
        }

        [Test]
        public void UiPhase5PersistentHideButtonSurvivesSceneReload()
        {
            GameObject canvas = Phase5Menu();
            Phase5Bind(canvas.transform.Find("Phase5Panel/HideButton").gameObject,
                canvas.transform.Find("Phase5Panel/TargetPanel").gameObject, false);
            Phase5Reload();
            GameObject reloaded = UiPhase3Find("Phase5Canvas");
            Button button = reloaded.transform.Find("Phase5Panel/HideButton").GetComponent<Button>();
            GameObject target = reloaded.transform.Find("Phase5Panel/TargetPanel").gameObject;
            Assert.AreEqual(1, button.onClick.GetPersistentEventCount());
            Assert.AreSame(target, button.onClick.GetPersistentTarget(0));
            Assert.AreEqual("SetActive", button.onClick.GetPersistentMethodName(0));
        }

        [Test]
        public void UiPhase5PersistentShowButtonSurvivesSceneReload()
        {
            GameObject canvas = Phase5Menu();
            GameObject button = canvas.transform.Find("Phase5Panel/ShowButton").gameObject;
            GameObject target = canvas.transform.Find("Phase5Panel/TargetPanel").gameObject;
            target.SetActive(false);
            EditorSceneManager.MarkSceneDirty(_root.scene);
            Phase5Bind(button, target, true);
            Phase5Reload();
            GameObject reloaded = UiPhase3Find("Phase5Canvas");
            Button savedButton = reloaded.transform.Find("Phase5Panel/ShowButton").GetComponent<Button>();
            GameObject savedTarget = reloaded.transform.Find("Phase5Panel/TargetPanel").gameObject;
            Assert.IsFalse(savedTarget.activeSelf);
            Assert.AreEqual(1, savedButton.onClick.GetPersistentEventCount());
            Assert.AreSame(savedTarget, savedButton.onClick.GetPersistentTarget(0));
        }

        [Test]
        public void UiPhase5InspectorReportsSavedListenerAfterReload()
        {
            GameObject canvas = Phase5Menu();
            Phase5Bind(canvas.transform.Find("Phase5Panel/HideButton").gameObject,
                canvas.transform.Find("Phase5Panel/TargetPanel").gameObject, false);
            Phase5Reload();
            GameObject button = UiPhase3Find("Phase5Canvas")
                .transform.Find("Phase5Panel/HideButton").gameObject;
            JObject inspection = (JObject)ExtendedTools.Execute("unity_inspect_ui",
                new JObject { ["objectId"] = ObjectAccess.Id(button) });
            JArray listeners = (JArray)((JArray)inspection["nodes"])[0]["onClickListeners"];
            Assert.AreEqual(1, listeners.Count);
            Assert.AreEqual("SetActive", (string)listeners[0]["method"]);
        }

        [Test]
        public void UiPhase5TextStyleAndLayoutPersistAfterSceneReload()
        {
            GameObject canvas = UiFixtureCanvas();
            JObject created = Propose(
                UiOp("panel", "Phase5Layout", ObjectAccess.Id(canvas)),
                UiOp("text", "Phase5Status", ObjectAccess.Id(canvas)));
            ChangeSets.Approve((string)created["id"]);
            Phase5Save();
            JObject styled = (JObject)ExtendedTools.Execute("unity_set_ui_property",
                new JObject { ["objectId"] = ObjectAccess.Id(UiPhase3Find("Phase5Status")),
                    ["text"] = "MCP2UNITY Ready", ["fontSize"] = 28 });
            _proposals.Add((string)styled["id"]);
            ChangeSets.Approve((string)styled["id"]);
            Phase5Save();
            JObject layout = (JObject)ExtendedTools.Execute("unity_set_ui_layout",
                new JObject { ["objectId"] = ObjectAccess.Id(UiPhase3Find("Phase5Layout")),
                    ["layoutType"] = "vertical", ["spacing"] = 11 });
            _proposals.Add((string)layout["id"]);
            ChangeSets.Approve((string)layout["id"]);
            Phase5Reload();
            Assert.AreEqual("MCP2UNITY Ready", UiPhase3Find("Phase5Status").GetComponent<Text>().text);
            Assert.AreEqual(28, UiPhase3Find("Phase5Status").GetComponent<Text>().fontSize);
            Assert.AreEqual(11f, UiPhase3Find("Phase5Layout")
                .GetComponent<VerticalLayoutGroup>().spacing);
        }

        [Test]
        public void UiPhase5SliderRangeAndReferencesPersistAfterSceneReload()
        {
            GameObject canvas = UiFixtureCanvas();
            JObject slider = UiOp("slider", "Phase5Slider", ObjectAccess.Id(canvas));
            slider["minValue"] = 5;
            slider["maxValue"] = 95;
            slider["value"] = 35;
            slider["wholeNumbers"] = true;
            JObject set = Propose(slider);
            ChangeSets.Approve((string)set["id"]);
            Phase5Reload();
            Slider saved = UiPhase3Find("Phase5Slider").GetComponent<Slider>();
            Assert.AreEqual(5f, saved.minValue);
            Assert.AreEqual(95f, saved.maxValue);
            Assert.AreEqual(35f, saved.value);
            Assert.IsTrue(saved.wholeNumbers);
            Assert.NotNull(saved.fillRect);
            Assert.NotNull(saved.handleRect);
        }

        [Test]
        public void UiPhase5ToggleFullHitAreaPersistsAfterSceneReload()
        {
            GameObject canvas = UiFixtureCanvas();
            JObject toggle = UiOp("toggle", "Phase5Toggle", ObjectAccess.Id(canvas));
            toggle["text"] = "Enabled";
            toggle["isOn"] = true;
            JObject set = Propose(toggle);
            ChangeSets.Approve((string)set["id"]);
            Phase5Reload();
            GameObject saved = UiPhase3Find("Phase5Toggle");
            Assert.IsTrue(saved.GetComponent<Toggle>().isOn);
            Assert.IsTrue(saved.GetComponent<Image>().raycastTarget);
            Assert.AreEqual("Enabled", saved.transform.Find("Label").GetComponent<Text>().text);
        }

        [Test]
        public void UiPhase5InputAndScrollReferencesPersistAfterSceneReload()
        {
            GameObject canvas = UiFixtureCanvas();
            JObject input = UiOp("inputField", "Phase5Input", ObjectAccess.Id(canvas));
            input["text"] = "Saved";
            input["placeholder"] = "Enter...";
            input["characterLimit"] = 24;
            JObject scroll = UiOp("scrollView", "Phase5Scroll", ObjectAccess.Id(canvas));
            scroll["contentHeight"] = 900;
            JObject set = Propose(input, scroll);
            ChangeSets.Approve((string)set["id"]);
            Phase5Reload();
            InputField field = UiPhase3Find("Phase5Input").GetComponent<InputField>();
            ScrollRect list = UiPhase3Find("Phase5Scroll").GetComponent<ScrollRect>();
            Assert.AreEqual("Saved", field.text);
            Assert.AreEqual(24, field.characterLimit);
            Assert.NotNull(field.textComponent);
            Assert.NotNull(field.placeholder);
            Assert.NotNull(list.viewport);
            Assert.NotNull(list.content);
            Assert.NotNull(list.viewport.GetComponent<RectMask2D>());
            Assert.AreEqual(900f, list.content.sizeDelta.y, .01f);
        }
    }
}
