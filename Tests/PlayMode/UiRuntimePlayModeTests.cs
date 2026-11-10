using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    // Real Unity PlayMode callback/interaction checks.
    // Independent temporary Canvas and EventSystem: never edits gameplay scenes.
    // Editor-only MCP ChangeSet creation/serialized binding is covered separately
    // by 73 EditMode tests, including persistent Button onClick bindings.
    public sealed class UiRuntimePlayModeTests
    {
        private GameObject _canvasObject;
        private GameObject _ownedEventSystem;
        private EventSystem _events;
        private GraphicRaycaster _raycaster;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _canvasObject = new GameObject("MCP2UNITY Phase4 Runtime Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasObject.GetComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            _raycaster = _canvasObject.GetComponent<GraphicRaycaster>();

            _events = EventSystem.current;
            if (_events == null)
            {
                _ownedEventSystem = new GameObject("MCP2UNITY Phase4 EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                _events = _ownedEventSystem.GetComponent<EventSystem>();
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_canvasObject != null) UnityEngine.Object.Destroy(_canvasObject);
            if (_ownedEventSystem != null) UnityEngine.Object.Destroy(_ownedEventSystem);
            yield return null;
        }

        private GameObject Element(string name, Vector2 size, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_canvasObject.transform, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            foreach (Type type in components) go.AddComponent(type);
            return go;
        }

        private RectTransform Child(GameObject parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private PointerEventData Pointer(Vector2 position)
        {
            return new PointerEventData(_events)
            {
                button = PointerEventData.InputButton.Left,
                position = position,
                pressPosition = position,
                clickCount = 1
            };
        }

        private static void Click(GameObject go, PointerEventData pointer)
        {
            ExecuteEvents.Execute<IPointerClickHandler>(go, pointer,
                ExecuteEvents.pointerClickHandler);
        }

        private Button Button()
        {
            return Element("Runtime Button", new Vector2(280, 70),
                typeof(Image), typeof(Button)).GetComponent<Button>();
        }

        private Toggle Toggle()
        {
            GameObject go = Element("Runtime Toggle", new Vector2(280, 50),
                typeof(Image), typeof(Toggle));
            Image rootGraphic = go.GetComponent<Image>();
            rootGraphic.color = new Color(1f, 1f, 1f, .04f);
            Toggle toggle = go.GetComponent<Toggle>();
            RectTransform mark = Child(go, "Checkmark", new Vector2(20, 20));
            Image markImage = mark.gameObject.AddComponent<Image>();
            toggle.targetGraphic = rootGraphic;
            toggle.graphic = markImage;
            return toggle;
        }

        private Slider Slider()
        {
            GameObject go = Element("Runtime Slider", new Vector2(280, 46),
                typeof(Image), typeof(Slider));
            Slider slider = go.GetComponent<Slider>();
            RectTransform fill = Child(go, "Fill", new Vector2(240, 20));
            fill.gameObject.AddComponent<Image>();
            RectTransform handle = Child(go, "Handle", new Vector2(24, 32));
            Image graphic = handle.gameObject.AddComponent<Image>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = graphic;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = .5f;
            return slider;
        }

        private InputField Input()
        {
            GameObject go = Element("Runtime InputField", new Vector2(340, 60),
                typeof(Image), typeof(InputField));
            InputField input = go.GetComponent<InputField>();
            RectTransform textRect = Child(go, "Text", new Vector2(320, 52));
            Text inputText = textRect.gameObject.AddComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            inputText.raycastTarget = false;
            RectTransform placeholderRect = Child(go, "Placeholder", new Vector2(320, 52));
            Text placeholderText = placeholderRect.gameObject.AddComponent<Text>();
            placeholderText.font = inputText.font;
            placeholderText.text = "Enter text";
            placeholderText.raycastTarget = false;
            input.textComponent = inputText;
            input.placeholder = placeholderText;
            input.targetGraphic = go.GetComponent<Image>();
            return input;
        }

        private ScrollRect Scroll()
        {
            GameObject go = Element("Runtime ScrollView", new Vector2(260, 180),
                typeof(Image), typeof(ScrollRect));
            ScrollRect scroll = go.GetComponent<ScrollRect>();
            RectTransform viewport = Child(go, "Viewport", new Vector2(260, 180));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Child(viewport.gameObject, "Content",
                new Vector2(260, 600));
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return scroll;
        }

        [UnityTest]
        public IEnumerator ButtonPointerClickFiresOnClickExactlyOnce()
        {
            Button button = Button();
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            yield return null;
            Click(button.gameObject, Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f)));
            Assert.AreEqual(1, clicks);
        }

        [UnityTest]
        public IEnumerator ButtonOnClickCanHideAnotherUiElement()
        {
            Button button = Button();
            GameObject target = Element("Runtime Target", new Vector2(100, 100), typeof(Image));
            button.onClick.AddListener(() => target.SetActive(false));
            yield return null;
            Click(button.gameObject, Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f)));
            Assert.IsFalse(target.activeSelf);
        }

        [UnityTest]
        public IEnumerator DisabledButtonIgnoresPointerClick()
        {
            Button button = Button();
            int calls = 0;
            button.onClick.AddListener(() => calls++);
            button.interactable = false;
            yield return null;
            Click(button.gameObject, Pointer(Vector2.zero));
            Assert.AreEqual(0, calls);
        }

        [UnityTest]
        public IEnumerator ButtonIsHitByActualGraphicRaycaster()
        {
            Button button = Button();
            yield return null;
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            _raycaster.Raycast(Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f)), hits);
            Assert.IsTrue(hits.Exists(h => h.gameObject == button.gameObject),
                "The Button image must be targetable through GraphicRaycaster.");
        }

        [UnityTest]
        public IEnumerator SliderValueChangesDispatchFloatCallbacks()
        {
            Slider slider = Slider();
            float observed = -1;
            int calls = 0;
            slider.onValueChanged.AddListener(v => { observed = v; calls++; });
            yield return null;
            slider.value = .75f;
            Assert.AreEqual(.75f, slider.value, .001f);
            Assert.AreEqual(.75f, observed, .001f);
            Assert.AreEqual(1, calls);
        }

        [UnityTest]
        public IEnumerator SliderWholeNumbersAndRangeAreRespected()
        {
            Slider slider = Slider();
            slider.minValue = 10;
            slider.maxValue = 20;
            slider.wholeNumbers = true;
            yield return null;
            slider.value = 12.7f;
            Assert.AreEqual(13f, slider.value, .001f);
            slider.value = 999;
            Assert.AreEqual(20f, slider.value, .001f);
        }

        [UnityTest]
        public IEnumerator SliderSetValueWithoutNotifyDoesNotInvokeListener()
        {
            Slider slider = Slider();
            int calls = 0;
            slider.onValueChanged.AddListener(_ => calls++);
            yield return null;
            slider.SetValueWithoutNotify(.9f);
            Assert.AreEqual(.9f, slider.value, .001f);
            Assert.AreEqual(0, calls);
        }

        [UnityTest]
        public IEnumerator TogglePointerClickChangesStateAndDispatchesCallback()
        {
            Toggle toggle = Toggle();
            toggle.isOn = false;
            bool latest = false;
            int calls = 0;
            toggle.onValueChanged.AddListener(v => { latest = v; calls++; });
            yield return null;
            Click(toggle.gameObject, Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f)));
            Assert.IsTrue(toggle.isOn);
            Assert.IsTrue(latest);
            Assert.AreEqual(1, calls);
        }

        [UnityTest]
        public IEnumerator DisabledToggleIgnoresPointerClick()
        {
            Toggle toggle = Toggle();
            toggle.isOn = false;
            toggle.interactable = false;
            yield return null;
            Click(toggle.gameObject, Pointer(Vector2.zero));
            Assert.IsFalse(toggle.isOn);
        }

        [UnityTest]
        public IEnumerator InputFieldTextRaisesCallbackAndRespectsCharacterLimit()
        {
            InputField input = Input();
            input.characterLimit = 5;
            int calls = 0;
            string last = null;
            input.onValueChanged.AddListener(s => { calls++; last = s; });
            yield return null;
            input.text = "abcdefghij";
            Assert.AreEqual("abcde", input.text);
            Assert.AreEqual("abcde", last);
            Assert.AreEqual(1, calls);
        }

        [UnityTest]
        public IEnumerator InputFieldSetTextWithoutNotifyDoesNotInvokeCallback()
        {
            InputField input = Input();
            int calls = 0;
            input.onValueChanged.AddListener(_ => calls++);
            yield return null;
            input.SetTextWithoutNotify("silent");
            Assert.AreEqual("silent", input.text);
            Assert.AreEqual(0, calls);
        }

        [UnityTest]
        public IEnumerator ScrollViewNormalizedPositionMovesContent()
        {
            ScrollRect scroll = Scroll();
            yield return null;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
            Vector2 top = scroll.content.anchoredPosition;
            scroll.verticalNormalizedPosition = 0f;
            Vector2 bottom = scroll.content.anchoredPosition;
            Assert.Greater(Mathf.Abs(top.y - bottom.y), 5f,
                "ScrollRect must move larger content inside its viewport.");
        }

        [UnityTest]
        public IEnumerator ScrollViewPointerDragMovesContent()
        {
            ScrollRect scroll = Scroll();
            yield return null;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = .5f;
            Vector2 before = scroll.content.anchoredPosition;
            var pointer = Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f));
            scroll.OnBeginDrag(pointer);
            pointer.position += new Vector2(0, 60);
            scroll.OnDrag(pointer);
            scroll.OnEndDrag(pointer);
            Assert.Greater(Mathf.Abs(scroll.content.anchoredPosition.y - before.y), 1f);
        }
    }
}
