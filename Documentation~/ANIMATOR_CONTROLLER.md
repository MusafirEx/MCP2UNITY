# Animator Controller analysis and reviewed editing

**Status:** implementation committed to `main` for Unity 2022.3 Editor compilation and EditMode verification. Not yet verified in the user's Unity installation.

## MCP tools

- `unity_inspect_animator_controller` (and compatibility alias `unity_get_animator_info`): select a GameObject with Animator / Controller, **or pass `controllerPath`** such as `Assets/Characters/Hero.controller`. The read-only result includes `sha256`, controller asset path, parameters (types/defaults), layers, nested state machines, state names/paths, motions, blend trees, transition timing/conditions, Any State and Entry transitions, plus override mappings. The existing state/motion/transition count limits still apply.
- `unity_propose_animator_controller_edit`: accept `path`, the current `expectedSha256` from inspection, `summary` and 1–50 ordered `edits`. Produces a **Pending Change Set**; **no mutation occurs until the user explicitly approves in the Unity Changes Sets and Actions window**.

This increases the advertised MCP catalog from **47 to 48 tools**. `Assets/**/*.controller` standalone assets are the only writable targets. Package files, imported model embedded controllers, AnimatorOverrideController assets and runtime-only controllers are **not edited** by this tool.

## Commands

| Action | Main fields | Function |
| --- | --- | --- |
| `addParameter` | `name`, `parameterType` (`Bool`, `Float`, `Int`, `Trigger`), optional typed defaults | Add parameter |
| `setParameterDefault` | `name`, `defaultBool` / `defaultFloat` / `defaultInt` | Update parameter default (not a Trigger) |
| `addState` | `layer`, `name`, optional `machine`, `motionPath`, `clipName`, state settings | Create state |
| `setState` | `layer`, `state`, optional clip/motion and settings | Change existing state |
| `setDefaultState` | `layer`, `state`, optional `machine` | Choose default state of containing machine |
| `addTransition` | `layer`, `source`, `destination`, timing and `conditions` | Create transition |
| `setTransition` | `layer`, `source`, `index`, `expectedDestination`, timing and conditions | Edit existing transition |
| `removeTransition` | `layer`, `source`, `index`, `expectedDestination` | Delete explicitly identified transition |
| `setLayer` | `layer`, optional `defaultWeight`, `iKPass` | Set layer properties |

`source` = `"*"` denotes the **root Any State** transition list. Otherwise `source`, `destination`, and `state` are paths *relative to a layer*, e.g. `Locomotion/Idle`. `machine` identifies a nested state machine such as `Locomotion`, or is omitted for the layer root. `index` is the **zero-based array position** in the inspected state's transition list. Editing/removing an existing transition always requires an `expectedDestination` name to guard against choosing the wrong entry.

Valid condition modes are `If`, `IfNot`, `Greater`, `Less`, `Equals`, `NotEqual`, with `parameter` and optional numeric `threshold`. Passing `conditions` on `setTransition` replaces the entire condition list; omitting it preserves conditions. `hasExitTime: false` with `duration` can implement direct returns to Locomotion without relying on the clip's exit time. `hasFixedDuration` chooses seconds instead of normalized time.

To assign a motion, use `motionPath` to an **Assets/** animation clip, including clips imported from FBX. When a path contains several clips, `clipName` identifies the exact clip. An empty `motionPath` clears the state motion. Editing clip loop time, imported FBX clip settings, Avatar Masks, blend-tree nodes, or AnimatorOverrideController overrides is not included in this first editing phase.

## Example workflow

Inspect:

```json
{"controllerPath":"Assets/Characters/Hero.controller","maximumStates":250}
```

The returned `sha256` is required for editing. Submit a reviewed batch (values shown here are examples):

```json
{
  "path": "Assets/Characters/Hero.controller",
  "expectedSha256": "<SHA256 returned by inspect>",
  "summary": "Add controlled Hit -> Locomotion return",
  "edits": [
    {"action":"addParameter","name":"InHit","parameterType":"Bool","defaultBool":false},
    {"action":"setState","layer":"Base Layer","state":"Hit","writeDefaultValues":false},
    {"action":"addTransition","layer":"Base Layer","source":"Hit","destination":"Locomotion",
     "hasExitTime":false,"duration":0.15,"hasFixedDuration":true,
     "conditions":[{"parameter":"InHit","mode":"IfNot"}]}
  ]
}
```

Then review and approve the Pending Change Set in **Window → Wasim Development → Change Sets and Actions**, and re-run inspection to verify. The existing Change Sets pipeline enforces stable **Edit Mode**, matching file SHA, guarded byte backups of the `.controller` and `.meta`, explicit local approval, and **reviewed rollback** if needed. Failed applications attempt Undo/file restoration.

## EditMode verification

`Tests/Editor/AnimatorControllerIntegrationTests.cs` adds targeted tests in the existing disposable fixture folder for: inspector by path without changing selection, pending edit vs approval, multi-step parameter/state/transition editing, stale-hash rejection, protected paths and fields, transition timing and rollback.

After a Git UPM update from `main`, verify Unity compilation and run **all EditMode tests** via reviewed `unity_propose_editor_action` / `runTests`. Existing Phase 7 contract expectations and PowerShell stability tooling have been updated for **48 tools**. Do **not** assume earlier 160/160 results include these new changes.

## Limitations

- Unity's Animator transition behavior must still be validated in Play Mode with real clips, events and Blend Trees. This tool edits controller *configuration* only.
- Bulk edits are ordered: each command sees earlier modifications in the same approved batch.
- File hash protection prevents approval against a changed controller; refresh the read and create a new proposal.
- Verify input parameter names, condition modes and clip paths in the inspected graph before proposing.
