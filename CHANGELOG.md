# Changelog

## Phase 7 — final 47-tool contract and read-only regressions (verified 2026-11-10)

- **First live Phase 7 run (2026-11-10):** action `59deae170419`, run `a2ee98ef-f91d-4460-bd09-4f7296517e28`: **11 passed / 1 failed** (4.1278s, 0 skipped/inconclusive). The sole failure was a test-fixture assumption: `SceneManager.SetActiveScene(_root.scene)` returned false inside EditMode Test Runner. Corrected `Phase7SceneHierarchyOutputHonorsObjectAndDepthLimits` to inspect the already active scene read-only and assert object/depth bounds without scene switching or modifying gameplay; fix commit `95aee55d388f8c2ca58b9950c4d2bb7cfaa7f186`. Unity verification of the corrected suite remains pending; cumulative verified baseline stays **108/108**.

- Added `Tests/Editor/FinalContractIntegrationTests.cs` plus Unity `.meta`, providing **12 EditMode tests** covering exact/unique 47-tool catalog, strict JSON object schemas, declared required fields, numeric bound consistency, legacy/backport/uGUI names, read-only versus review-required safety annotations, live `unity_get_status` tool enumeration, read-only `unity_get_project_info`, scene hierarchy caps, bounded runtime inspection, package script reader response/hash/line bounds, and invalid tool-input rejection.
- The tests reuse the existing isolated additive scene fixture. They are designed not to modify the user's gameplay scene or approve any remote project changes.
- **Verified Phase 7 final result: 12/12 EditMode PASSED** in Unity 2022.3.62f3 on 2026-11-10, reviewed action `5be3bb8c8c20`, run `58d6361f-d8ec-4086-a42c-fd66d9bd12ea`: 0 failed, 0 skipped, 0 inconclusive, duration 3.4396238 seconds. The repaired hierarchy test was executed after UPM refresh. **Cumulative automated result: 120/120** (107 EditMode + 13 PlayMode). Unity compilation and Console were clean, bridge reported 47 tools, and `MuOnBaArPrototype` remained saved, clean, with five original roots. This validates the Unity integration/test scope, not external ngrok lifecycle, standalone Windows companion reliability over time, player builds, or VRChat clients.


## Phase 6 — safety guards and recovery (verified 2026-11-10)

- Added `Tests/Editor/SafetyRecoveryIntegrationTests.cs` with 12 isolated EditMode tests for path traversal and write-root rejection, transient instance IDs, protected script identity, duplicate aliases, 50-operation limits, unsaved scene guard, inactive descendant deletion cap, parent/subtree drift, rollback protection when scene files change, and interrupted MCP IPC request recovery without replay.
- All tests reuse the disposable Unity test fixture and never manipulate the user's gameplay scene. The recovery fixture writes only inside a new isolated `Library/McpPhase6Recovery_*` directory and deletes it afterward.
- **Verified 12/12 Phase 6 EditMode tests passed** in Unity 2022.3.62f3 on 2026-11-10, reviewed Editor action `abef18ce2b4b`, test run `4bfd294e-6c1e-48a6-9a34-839891d3c8bd`: 0 failed/skipped/inconclusive, 4.2824843 seconds. Cumulative measured result: **108/108 automated tests** (95 EditMode + 13 PlayMode). Unity compilation and Console were clean; the gameplay scene was saved, clean, and had its original 5 root GameObjects. Phase 5 EventSystem cleanup (`329662d370e0`) was approved and verified applied.



## Phase 5 — MCP UI serialization and end-to-end verification (test candidate)

- Added **10 EditMode integration tests** in `Tests/Editor/UiPersistenceIntegrationTests.cs` (as a partial of `BridgeIntegrationTests`). They exercise MCP dispatcher Canvas/Button creation, atomic alias hierarchy creation, persistent Button SetActive listener preservation in saved prefabs and reopened disposable scenes, post-reload inspector output, and persisted layout/text/Slider/Toggle/InputField/ScrollRect settings.
- The tests create and remove their **own temporary additive scene** and project assets. They do not require changes to the user's gameplay scene.
- **Verified 10/10 Phase 5 EditMode tests passed** in Unity 2022.3.62f3 on 2026-11-10, action `ccf630ce1fb8`, run `52a229fb-652d-45eb-99c6-2303e66e01e7` (0 failed/skipped/inconclusive, runtime 7.12 seconds). Combined measured result: **96/96** automated tests (73 earlier EditMode + 13 PlayMode + 10 Phase 5 EditMode).
- **Live MCP UI Hide/Show check PASSED (2026-11-10):** user approved a five-operation Canvas/Panel/Target/Hide/Show creation change set (`a69100449806`), approved persistent `GameObject.SetActive(false)` (`afa26d636ce7`) and `SetActive(true)` (`515090cdf409`) button bindings, and manually clicked the buttons in Game View. Independent MCP runtime reads verified `F5_TargetImage.activeSelf=false` after Hide and `true` after Show (`activeInHierarchy` matched). Unity Console remained clean. PlayMode exit was approved and confirmed. Temporary Canvas (`2cf1b1c7a623`) and EventSystem (`329662d370e0`) deletion were approved and verified applied, restoring the clean saved scene to its five original root GameObjects.

## Phase 4 — Unity uGUI PlayMode tests (verified 2026-11-10)

- Added a separate PlayMode test assembly with 13 runtime interaction tests covering Button, Slider, Toggle, InputField, ScrollRect, and GraphicRaycaster input.
- PlayMode test fixtures use disposable Canvas/EventSystem objects and avoid saving gameplay scene changes.
- Made generated Toggle root UI image transparently raycastable across its entire row, including label area, and extended an existing EditMode assertion.
- Baseline before Phase 4: **73/73 EditMode tests passed** in live Unity 2022.3.62f3, with 47 MCP tools advertised.
- **Verified 13/13 PlayMode tests passed** in Unity 2022.3.62f3 on 2026-11-10 (Unity MCP Editor action `f197a999ff82`, run `9fd24057-5d0d-4cc2-9faf-78b657501b4f`). 0 failed/skipped/inconclusive; compilation and Console reported no errors/warnings. Combined automated count: **86/86** (73 EditMode + 13 PlayMode). These fixture tests are not a substitute for end-to-end PlayMode testing of persistent events on MCP-created UI.

## 0.5.2 (MCP2UNITY experimental rebuild)

- Restored all 50 source files from upstream v0.5.1 without changing the baseline IPC/companion lifecycle.
- Added 13 modular v0.6.0 Editor source files with their corresponding original .meta files (17 new tools).
- Wired new catalog entries and dispatcher fallback while retaining original implementations for 21 legacy tools.
- Adopted v0.6.0 reviewed change-set permissions, Editor-action settings, path checks, and Test Framework assembly references.
- Added native MCP image envelope forwarding, JSON array/null preservation and a real Unity IPC self-test.
- Rejected expired requests and prevented automatic replay of interrupted in-flight operations after domain reload.
- Added six adapted Unity integration tests and Windows PowerShell companion regression tests.
- Assigned independent UPM package ID `com.musafirex.mcp2unity` and version 0.5.2.
- **Status:** Compiling in Unity and passing real Windows/Unity tests are still required; v0.6.0 defects are not yet root-caused.

## Original upstream changelog

See the original v0.5.1 release at https://github.com/wasim-development/wasim-unity-mcp-bridge/releases/tag/v0.5.1.
