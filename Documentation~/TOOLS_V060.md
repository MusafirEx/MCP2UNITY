# MCP2UNITY 0.5.2 — Tools backported from 0.6.0

This rebuild preserves all 21 v0.5.1 tool names and ports these 17 v0.6.0 features without replacing the v0.5.1 companion. This code has not yet passed a full target Unity compilation. The following 17 names extend the catalog. The bridge remains model-independent: it exposes MCP tools and does not embed an OpenAI API client or require a model API key.

| Tool | Purpose |
| --- | --- |
| unity_inspect_runtime | Selected hierarchy or explicit objectId; live physics, animation, VRChat pickup/owner, requested Udon variables and nested serialized property paths. |
| unity_capture_view | MCP JPEG image: Scene camera render or focused Game View. |
| unity_propose_change_set | Queue up to 50 reviewed operations; no immediate project edit. |
| unity_get_change_sets | Pending/recent sets, status, optional full operations and created-object IDs. |
| unity_propose_change_set_rollback | Queue restoration of an applied set, guarded against subsequent edits. |
| unity_start_trace | Poll Play Mode state and collect explicit diagnostic event logs for up to 120 seconds. |
| unity_read_trace | Incremental trace reading with sequence numbers and overflow visibility. |
| unity_stop_trace | Stop a trace and keep its data available until the next trace/domain reload. |
| unity_validate_vrchat | Bounded hierarchy checks: references, pickup physics, joints, Animator configuration, used-layer collision matrix and compile state. |
| unity_find_asset_references | Paged reverse dependencies under Assets and target direct dependencies. |
| unity_check_runtime | Exact assertions against current runtime component values. |
| unity_start_profiler | Nonblocking 1–30 second Editor counter sampling. |
| unity_read_profiler | Sample count, mean, maximum, p95 and unavailable counters. |
| unity_stop_profiler | Dispose active recorders and return captured statistics. |
| unity_propose_editor_action | Queue Play/Stop/Pause/Resume/Step/Compile or Unity Test Framework execution for local review. |
| unity_get_editor_actions | Read pending/accepted/test action status. |
| unity_get_test_results | Latest bridge-started test run counts and bounded failure details. |

## Inspection and IDs

Call unity_inspect_runtime on the selected GameObject. Each object/component has an objectId. Saved scene objects use GlobalObjectId; dynamic/unsaved objects may use instance:<number>. Change sets require saved IDs. Instance IDs are only useful in the current Editor session.

For actual Play Mode values set includeSerialized=false when serialized configuration is unnecessary. Udon variables are an explicit list of up to 32 names; complex values are summarized, not recursively reflected. The allowlist covers documented Rigidbody/Joint/Collider/Animator and selected VRChat API members. It is not arbitrary property/method invocation.

## Change-set operation shapes

Each object below is one item of operations. Supply summary alongside the array.

- set_property: targetId, propertyPath, value; optional expectedValue. The bridge captures the existing value when omitted and checks it again at approval.
- add_component: targetId, fully qualified componentType; optional alias.
- create_gameobject: name; optional parentId, scenePath, primitive, alias. The active saved scene is used when there is no parent/scenePath. Child and parent must use the same scene.
- create_script: new Assets path and full newContent.
- replace_script: existing Assets path, expectedSha256 from unity_read_script, full newContent.
- save_prefab: targetId of a scene GameObject/component and a new Assets .prefab path.

Aliases begin with $ and may only reference earlier create/add operations in the same set. Review JSON stores scene/script guards alongside the prepared operations. No apply/approve endpoint is remotely advertised.

Example: create an object, attach Rigidbody and disable gravity:

~~~json
{
  "summary": "Create a physics placeholder in the active saved scene",
  "operations": [
    {"kind": "create_gameobject", "name": "ToolPlaceholder", "primitive": "Cube", "alias": "$tool"},
    {"kind": "add_component", "targetId": "$tool", "componentType": "UnityEngine.Rigidbody", "alias": "$body"},
    {"kind": "set_property", "targetId": "$body", "propertyPath": "m_UseGravity", "value": false}
  ]
}
~~~

For an object reference, use value: {"objectId":"<real ID from inspection>"} or explicit null. Vectors use x/y/z/w; colors use r/g/b/a. Enum strings use serialized enum names; enum integers are serialized enum indices. Whole-array resizing, managed references and m_Script replacement are not supported.

Custom components may execute Unity/SDK callbacks during addition/validation. Backups cover declared Assets files and affected scene files; they cannot undo arbitrary external side effects performed by project scripts.

## Runtime traces

Start Play Mode/ClientSim, select the tool root and call unity_start_trace. Read using sessionId and afterSequence; use nextAfterSequence for the next page. The 2,000-event ring buffer reports firstAvailableSequence so clients can detect overwritten events.

sampled-change means a value changed between observations; it does not prove an exact callback order. For real callback/resource event records, add opt-in logs beginning with [WDMCP_EVENT]. See Examples/UdonTraceExample.cs. The bridge does not inject instrumentation into gameplay scripts.

## Tests and Editor actions

runTests uses Unity Test Framework 1.1.33, with testMode EditMode or PlayMode and optional exact testNames. Local approval is required because tests can execute arbitrary code supplied by the project. Save scenes first. The request returns a proposal immediately; execution and callbacks are asynchronous. Poll unity_get_test_results.

Accepted means an Editor command was dispatched, not that the requested transition or compilation succeeded. Check unity_get_status / unity_get_compilation_status after Play/Compile actions. Test runs have Running/Completed status and separate test pass/fail counts.

unity_check_runtime only checks current exposed values. It does not simulate VRChat input, trigger pickup callbacks or emulate another real player. Add project Play Mode tests for automated gameplay scenarios.

## Capture and profiler limits

Scene capture renders the Scene camera without editor gizmos. Game capture reads the currently focused visible Game View including its toolbar; window visibility, desktop scaling, graphics pipeline and platform affect results. Capture requires interactive Unity and returns an MCP image block.

Profiler data is sampled every 100 ms from supported Editor counters: main-thread time, GC allocation, total used memory and draw calls. Unavailable counters are explicitly listed. This is not Quest/device FPS, every rendered frame, a deep profile or attribution to individual scripts. Recordings and traces stop/reset on domain reload.

## Diagnostics limits

VRChat diagnostics validate available local configuration, not network authority across real clients. Network ID allocation/conflict validation and the final world build remain in the SDK. Asset references are direct AssetDatabase dependencies; use paging for a full reverse scan and unity_find_script_references for textual code symbols. These are not a complete semantic C# graph.

## Source-text response field (read-script interoperability fix)

`unity_read_script` and `unity_get_selected_script` both return `path`, `lineCount`, `startLine`, `endLine`, `truncated`, `sha256` and **`sourceText`** (line-numbered C# text). The former `content` field was renamed to `sourceText` because some MCP connectors normalized nested `content` keys as MCP content blocks and delivered an empty object. Both tools use the same internal reader and path-security checks. Update any consumer previously reading `result.content` to read `result.sourceText` instead. This fix has editor regression tests; an end-to-end check through the connected ChatGPT MCP tool after upgrading UPM is still required.

## Transform tool (MCP2UNITY extension)

`unity_set_transform` creates a reviewed change set for the selected scene GameObject or a provided
`objectId`. It is **not an immediate scene-edit command**; enable reviewed change sets in Unity,
save the scene, send the proposal, then approve it locally. The tool supports:

- `position`: XYZ position; local or world; absolute or relative translation.
- `rotation`: XYZ **Euler angles in degrees**; local or world; absolute or relative rotation.
  Relative local rotation is post-multiplied; relative world rotation is pre-multiplied.
- `scale`: XYZ local scale or world scale; in `relative=true` mode, XYZ values are **multipliers**
  (for example, `{x:2,y:1,z:1}` doubles X).
- `space`: `local` (default) or `world`; `relative`: `false` (default) or `true`.
- `summary` is optional. `objectId` may be omitted when a scene GameObject is selected.

World scale is exact only when **all ancestors have positive uniform scales**; other hierarchies
are rejected (use `space=local`). At least one of position, rotation or scale is required.
Values must be finite. Saved scene / stable GlobalObjectId / stale-scene checks, local approval,
Undo and guarded rollback are inherited from `unity_propose_change_set`.

Example MCP arguments (position world absolute + Euler rotation + world scale):

~~~json
{
  "objectId": "<saved GameObject GlobalObjectId>",
  "space": "world",
  "relative": false,
  "position": {"x": 1, "y": 2, "z": 3},
  "rotation": {"x": 0, "y": 90, "z": 0},
  "scale": {"x": 2, "y": 2, "z": 2},
  "summary": "Place the selected object at the target transform"
}
~~~


## Unity uGUI Phase 1

Four extra tools (`unity_create_canvas`, `unity_create_ui`, `unity_set_rect_transform`, `unity_inspect_ui`) add reviewed Canvas/uGUI support. See [UNITY_UI_TOOLS.md](UNITY_UI_TOOLS.md). The `create_ui` operation is also available in `unity_propose_change_set` for multi-object hierarchies using aliases. After this phase: 43 advertised MCP tools.


## Unity uGUI Phase 2

Two new locally-reviewed tools, `unity_set_ui_property` and `unity_set_ui_layout`, extend the catalog to 45 advertised tools. Detailed types, limits, examples and test coverage: [UNITY_UI_TOOLS.md](UNITY_UI_TOOLS.md).
