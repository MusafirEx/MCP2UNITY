# MCP2UNITY — Unity uGUI tools (Phase 1)

> Target: Unity 2022.3 with com.unity.ugui 1.0.0 and com.unity.textmeshpro 3.0.7.
> Four new tools and 19 additional EditMode tests. Live Unity and MCP compilation tests are required after updating the installed UPM package; source publication alone is not a test pass.

## Workflow

- Save the target scene before proposing a change.
- Enable "Allow reviewed change sets" in Window > Wasim Development > Unity MCP Bridge.
- Use unity_create_canvas or unity_create_ui to submit a proposal. They DO NOT mutate the scene immediately.
- Review all operations and approve locally in "Change Sets and Actions".
- Read the applied change set's createdObjects map for the saved GlobalObjectId (default aliases: $createdCanvas / $createdUi).
- For later changes, pass that ID; no raw instance IDs can be used for writing.
- Rollback is a separate reviewed action. Save dirty scenes first and do not roll back after unrelated asset changes.

## Four MCP tools

### unity_create_canvas

Optional arguments:

- name: defaults to "Canvas".
- scenePath: target saved loaded scene; defaults to active scene.
- referenceResolution: {x, y}, defaults to {1920,1080}.
- createEventSystem: true by default; creates EventSystem + StandaloneInputModule only if one does not already exist in loaded scenes.
- summary: change-set review description.

Creates a root Canvas with RectTransform, CanvasScaler (Scale With Screen Size), GraphicRaycaster and optional input EventSystem. Render mode is ScreenSpaceOverlay in this phase.

### unity_create_ui

Required: uiType (panel/image/button/text/tmpText), name, parentId (saved Canvas or RectTransform descendant GlobalObjectId). Optional: scenePath, text (max 8192 characters), fontSize (1–256), color {r,g,b,a} in [0,1], width/height (1–8192), anchoredPosition {x,y}, summary.

Panel defaults to stretch over its parent; Button has Image + Button + a Text label. Text uses legacy Text and Arial; tmpText creates TextMeshProUGUI. All elements have RectTransform. This phase does not auto-wire onClick handlers or bind sprites.

### unity_set_rect_transform

Requires a saved target RectTransform via objectId (may omit when selected) and at least one of:

- anchorMin {x,y} and anchorMax {x,y} in [0,1].
- pivot {x,y} in [0,1].
- sizeDelta {x,y} and anchoredPosition {x,y}, finite coordinates.

Optional summary. Generates guarded set_property operations for the selected RectTransform. All changes require local approval.

### unity_inspect_ui

Optional objectId (or selection) and maximumObjects (1–200). Returns the bounded hierarchy's RectTransform anchor/pivot/size/position, component types, text (truncated to 2048 characters), graphic color, Canvas mode and Button interactability. Read-only and does not modify the scene.

## Advanced: one approval for a hierarchy

Use unity_propose_change_set with operations:

~~~json
{
  "summary": "New menu canvas and button",
  "operations": [
    {"kind":"create_ui","uiType":"canvas","name":"MenuCanvas","alias":"$canvas"},
    {"kind":"create_ui","uiType":"panel","name":"MenuPanel","parentId":"$canvas","alias":"$panel"},
    {"kind":"create_ui","uiType":"button","name":"StartButton","parentId":"$panel","text":"Start","alias":"$start"}
  ]
}
~~~

Every alias must refer to an earlier creation within the same proposal; a non-Canvas parent cannot be used to create a uGUI element. No additional event/system callbacks are invoked remotely.

## Guardrails

- The new create_ui operation uses the existing ChangeSets pipeline, including stable IDs, clean-scene checks, stale-file hashes, explicit in-Editor approval, Undo, backups and separate locally-approved rollback.
- The bridge allows a deliberately small uGUI type list to avoid instantiating arbitrary scripts.
- Script callbacks can execute during component creation; review each change and do not assume rollback restores unrelated external side effects.
- No change to runtime/player builds. This is Editor-only.
- No UI Toolkit (UXML/USS), Layout Groups, Scroll View, Slider, Toggle or InputField creator in Phase 1.

## Regression coverage (19 new tests)

Catalog and permission hints; proposal-only safety; Canvas components; EventSystem optional behavior; scaling resolution; parent and invalid type validation; Panel/Image/Button/Text/TMP creation; same-set aliases; RectTransform change proposals; non-RectTransform rejection; bounded inspection; rollback; and stale-scene guards. Run these alongside the original 14 regression tests. A source-side static audit is not equivalent to passing Unity EditMode tests.

## Unity 2022.3.62f3 EditMode findings (2026-11-09)

Initial 33-test run: 27 passed, 6 failed. Two failures exposed a null-alias scene lookup in ChangeSets.Propose and four occurred because Unity rejects `Arial.ttf` as a built-in font. These were patched by separating creation-alias handling from component-alias handling and using `LegacyRuntime.ttf` for uGUI Text/Button labels. Re-run all 33 tests after updating the package; this note is not a claim of a passing test run.


## Phase 2 — reviewed UI styling and layout

This is an unverified-source release until installed in Unity and its 51 EditMode regression tests are run. The Phase 1 baseline (33/33 passed) remains the previous measured result.

### unity_set_ui_property

Pass a saved uGUI element objectId (or select it) and at least one of:

- \`text\` (0–8192 characters): Text or TextMeshProUGUI
- \`fontSize\` (1–256): Text or TextMeshProUGUI; legacy Text requires a whole number
- \`color\` {r,g,b,a}, each 0–1: the element's Graphic component
- \`interactable\` boolean: a Selectable-derived component (e.g. Button)
- \`raycastTarget\` boolean: Graphic
- \`fillAmount\` 0–1: Image (set Image Type=Filled for visible filling)
- \`spriteId\`: a saved imported Sprite asset GlobalObjectId, for Image
- \`summary\`: optional review description

The tool verifies the selected component and serialized property before queuing an ordinary \`set_property\` Change Set. It does not directly update the scene. There is no arbitrary reflection or arbitrary callback invocation.

Example:

~~~json
{"objectId":"<saved UI GameObject ID>","text":"Play Now","fontSize":30,"color":{"r":1,"g":0.8,"b":0.1,"a":1}}
~~~

### unity_set_ui_layout

Pass a saved RectTransform under Canvas and:

- \`layoutType\`: \`horizontal\`, \`vertical\`, or \`grid\` (required)
- \`spacing\`: number 0–1024
- \`padding\`: {left,right,top,bottom}, integer 0–1024
- \`childControlWidth\`, \`childControlHeight\`, \`childForceExpandWidth\`, \`childForceExpandHeight\`: booleans (Horizontal/Vertical only)
- \`cellSize\`: {x,y}, each 1–8192 (Grid only)
- \`constraint\`: \`flexible\`, \`fixedColumnCount\`, \`fixedRowCount\` (Grid only)
- \`constraintCount\`: integer 1–1000 (Grid only)
- \`summary\`: optional review description

Proposals use a new allowlisted \`set_ui_layout\` Change Set operation. Approval can add a missing LayoutGroup, but will refuse to replace a different existing LayoutGroup. A guarded rollback can remove a newly-added group. All operations require a saved, clean scene and local Unity approval.

Example:

~~~json
{"objectId":"<saved Panel GameObject ID>","layoutType":"grid","cellSize":{"x":120,"y":60},"spacing":8,"constraint":"fixedColumnCount","constraintCount":3}
~~~

### Added automated coverage

18 Phase 2 regression tests: tool catalog, deferred proposals, legacy Text/TMP text and font editing, Image color/fill/raycast, Button interactability, invalid style input rejections, Horizontal/Vertical/Grid layout configuration, conflict and invalid-dimension rejection, layout inspection and rollback. Total expected EditMode count: 51 (33 existing + 18 Phase 2). Passing these tests does not yet establish direct newly advertised tool availability through every ChatGPT connector configuration.

