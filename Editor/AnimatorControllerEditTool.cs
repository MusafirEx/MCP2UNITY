using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WasimDevelopment.UnityMcpBridge
{
    // Only project-owned .controller assets may be edited. All calls here are
    // validation or approved ChangeSets.ApplyOperation; never mutate in a
    // public MCP request. Supports Unity 2022.3 AnimatorController APIs.
    internal static class AnimatorControllerEditTool
    {
        private static readonly HashSet<string> Commands = new HashSet<string>(StringComparer.Ordinal)
        {
            "addParameter", "setParameterDefault", "addState", "setState",
            "setDefaultState", "addTransition", "setTransition",
            "removeTransition", "setLayer"
        };

        internal static AnimatorController Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\\') >= 0 ||
                !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only existing Assets/**/*.controller files are editable.");
            string full = ProjectSecurity.ResolveAssetFile(path, true);
            if (!File.Exists(full) || !AssetDatabase.IsOpenForEdit(path))
                throw new InvalidOperationException("Animator Controller is missing or not writable: " + path);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null || AssetDatabase.GetAssetPath(controller) != path)
                throw new ArgumentException("Path is not a standalone AnimatorController asset: " + path);
            return controller;
        }

        internal static JObject Propose(JObject args)
        {
            string path = (string)args["path"];
            string expected = (string)args["expectedSha256"];
            string summary = (string)args["summary"];
            JArray edits = args["edits"] as JArray;
            if (string.IsNullOrWhiteSpace(summary) || summary.Length > 2000)
                throw new ArgumentException("Provide a summary (1-2000 characters).");
            if (edits == null || edits.Count < 1 || edits.Count > 50)
                throw new ArgumentException("Provide 1-50 Animator edits.");
            AnimatorController controller = Load(path);
            string actual = ChangeSets.HashFile(ProjectSecurity.ResolveAssetFile(path, true));
            if (string.IsNullOrEmpty(expected) || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Animator Controller SHA-256 is stale; inspect again before editing.");
            foreach (JToken e in edits)
            {
                if (!(e is JObject edit)) throw new ArgumentException("Each Animator edit must be a JSON object.");
                ValidateEdit(controller, edit);
            }
            // Existing review/approval, byte backups and rollback are reused.
            return ChangeSets.Propose(new JObject {
                ["summary"] = summary,
                ["operations"] = new JArray(new JObject {
                    ["kind"] = "edit_animator_controller",
                    ["path"] = path,
                    ["expectedSha256"] = actual,
                    ["edits"] = edits.DeepClone()
                })
            });
        }

        internal static void ValidateOperation(JObject operation)
        {
            string path = (string)operation["path"];
            AnimatorController controller = Load(path);
            string hash = ChangeSets.HashFile(ProjectSecurity.ResolveAssetFile(path, true));
            if (!string.Equals(hash, (string)operation["expectedSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Animator Controller changed since inspection: " + path);
            JArray edits = operation["edits"] as JArray;
            if (edits == null || edits.Count < 1 || edits.Count > 50)
                throw new ArgumentException("Animator edit requires 1-50 steps.");
            foreach (JToken value in edits)
            {
                if (!(value is JObject edit)) throw new ArgumentException("Animator edit must be an object.");
                ValidateEdit(controller, edit);
            }
        }

        internal static void Apply(JObject operation)
        {
            AnimatorController controller = Load((string)operation["path"]);
            Undo.RegisterCompleteObjectUndo(controller, "Edit Animator Controller");
            foreach (JObject edit in (JArray)operation["edits"])
                ApplyOne(controller, edit);
            EditorUtility.SetDirty(controller);
            // Write .controller and child subassets before ChangeSets hashes it.
            AssetDatabase.SaveAssets();
        }

        private static void ValidateEdit(AnimatorController controller, JObject edit)
        {
            string action = (string)edit["action"];
            if (string.IsNullOrEmpty(action) || !Commands.Contains(action))
                throw new ArgumentException("Unsupported Animator edit action: " + action);
            string[] common = { "action", "layer", "name", "parameterType", "defaultBool",
                "defaultInt", "defaultFloat", "machine", "state", "source", "destination",
                "index", "expectedDestination", "motionPath", "clipName", "speed", "tag",
                "writeDefaultValues", "mirror", "iKOnFeet", "cycleOffset",
                "duration", "hasExitTime", "exitTime", "hasFixedDuration",
                "offset", "canTransitionToSelf", "conditions", "defaultWeight", "iKPass" };
            foreach (JProperty prop in edit.Properties())
                if (!common.Contains(prop.Name, StringComparer.Ordinal))
                    throw new ArgumentException("Unsupported Animator edit field: " + prop.Name);
            if (action == "addParameter" || action == "setParameterDefault")
            {
                Name(edit, "name");
                if (action == "addParameter") ParameterType(Name(edit, "parameterType"));
                else if (!controller.parameters.Any(p => p.name == (string)edit["name"]))
                    throw new ArgumentException("Unknown Animator parameter: " + (string)edit["name"]);
            }
            else
            {
                AnimatorControllerLayer layer = Layer(controller, Name(edit, "layer"));
                if (action == "addState") Name(edit, "name");
                if (action == "setState" || action == "setDefaultState") Name(edit, "state");
                if (action == "addTransition")
                {
                    Name(edit, "source"); Name(edit, "destination");
                }
                if (action == "setTransition" || action == "removeTransition")
                {
                    Name(edit, "source");
                    if (edit["index"] == null || edit["index"].Type != JTokenType.Integer ||
                        edit["index"].Value<int>() < 0)
                        throw new ArgumentException("Transition requires a zero-based nonnegative index.");
                }
                if (action == "setLayer" && layer.stateMachine == null)
                    throw new ArgumentException("Animator layer has no state machine.");
            }
            foreach (string field in new[] { "speed", "cycleOffset", "duration",
                "exitTime", "offset", "defaultFloat", "defaultWeight" })
                if (edit[field] != null) Number(edit, field);
            if (edit["duration"] != null && Number(edit, "duration") < 0)
                throw new ArgumentException("Transition duration cannot be negative.");
            if (edit["defaultWeight"] != null && (Number(edit, "defaultWeight") < 0 ||
                Number(edit, "defaultWeight") > 1))
                throw new ArgumentException("Layer weight must be between 0 and 1.");
            if (edit["conditions"] != null)
            {
                if (!(edit["conditions"] is JArray conditions) || conditions.Count > 20)
                    throw new ArgumentException("conditions must be an array of up to 20 items.");
                foreach (JToken value in conditions)
                {
                    if (!(value is JObject condition)) throw new ArgumentException("Invalid transition condition.");
                    string parameter = Name(condition, "parameter");
                    string mode = Name(condition, "mode");
                    if (!Enum.TryParse(mode, out AnimatorConditionMode parsed) ||
                        !Enum.IsDefined(typeof(AnimatorConditionMode), parsed))
                        throw new ArgumentException("Unsupported transition condition mode: " + mode);
                    // References are checked again at apply time; a parameter
                    // may be added in an earlier edit in the same reviewed batch.
                    if (condition["threshold"] != null) Number(condition, "threshold");
                }
            }
        }

        private static void ApplyOne(AnimatorController controller, JObject e)
        {
            string action = (string)e["action"];
            if (action == "addParameter")
            {
                string name = Name(e, "name");
                if (controller.parameters.Any(p => p.name == name))
                    throw new InvalidOperationException("Duplicate Animator parameter: " + name);
                AnimatorControllerParameterType type = ParameterType(Name(e, "parameterType"));
                controller.AddParameter(new AnimatorControllerParameter {
                    name = name, type = type,
                    defaultBool = OptionalBool(e, "defaultBool", false),
                    defaultInt = OptionalInt(e, "defaultInt", 0),
                    defaultFloat = OptionalFloat(e, "defaultFloat", 0)
                });
                return;
            }
            if (action == "setParameterDefault")
            {
                string name = Name(e, "name");
                AnimatorControllerParameter[] parameters = controller.parameters;
                int i = Array.FindIndex(parameters, p => p.name == name);
                if (i < 0) throw new InvalidOperationException("Unknown Animator parameter: " + name);
                AnimatorControllerParameter p = parameters[i];
                if (p.type == AnimatorControllerParameterType.Bool)
                    p.defaultBool = OptionalBool(e, "defaultBool", p.defaultBool);
                else if (p.type == AnimatorControllerParameterType.Int)
                    p.defaultInt = OptionalInt(e, "defaultInt", p.defaultInt);
                else if (p.type == AnimatorControllerParameterType.Float)
                    p.defaultFloat = OptionalFloat(e, "defaultFloat", p.defaultFloat);
                else throw new InvalidOperationException("Trigger has no default value.");
                parameters[i] = p;
                Undo.RecordObject(controller, "Animator parameter default");
                controller.parameters = parameters;
                return;
            }

            AnimatorControllerLayer layer = Layer(controller, Name(e, "layer"));
            if (action == "setLayer")
            {
                AnimatorControllerLayer[] layers = controller.layers;
                int i = Array.FindIndex(layers, x => x.name == layer.name);
                if (e["defaultWeight"] != null) layers[i].defaultWeight = Number(e, "defaultWeight");
                if (e["iKPass"] != null) layers[i].iKPass = OptionalBool(e, "iKPass", layers[i].iKPass);
                Undo.RecordObject(controller, "Animator layer");
                controller.layers = layers;
                return;
            }
            if (action == "addState")
            {
                AnimatorStateMachine machine = Machine(layer, (string)e["machine"]);
                string name = Name(e, "name");
                if (machine.states.Any(s => s.state.name == name))
                    throw new InvalidOperationException("Duplicate state: " + name);
                Undo.RecordObject(machine, "Add Animator state");
                AnimatorState state = machine.AddState(name);
                UpdateState(state, e);
                return;
            }
            if (action == "setState")
            {
                UpdateState(State(layer, Name(e, "state")), e);
                return;
            }
            if (action == "setDefaultState")
            {
                Undo.RecordObject(layer.stateMachine, "Set default Animator state");
                layer.stateMachine.defaultState = State(layer, Name(e, "state"));
                return;
            }
            if (action == "addTransition")
            {
                AnimatorState target = State(layer, Name(e, "destination"));
                string source = Name(e, "source");
                AnimatorStateTransition transition;
                if (source == "*")
                {
                    Undo.RecordObject(layer.stateMachine, "Add Any State transition");
                    transition = layer.stateMachine.AddAnyStateTransition(target);
                }
                else
                {
                    AnimatorState from = State(layer, source);
                    Undo.RecordObject(from, "Add Animator transition");
                    transition = from.AddTransition(target);
                }
                UpdateTransition(controller, transition, e);
                return;
            }
            if (action == "setTransition" || action == "removeTransition")
            {
                AnimatorStateTransition transition = Transition(layer, e, out AnimatorState sourceState);
                if (action == "removeTransition")
                {
                    if (sourceState == null)
                    {
                        Undo.RecordObject(layer.stateMachine, "Remove Any State transition");
                        layer.stateMachine.RemoveAnyStateTransition(transition);
                    }
                    else
                    {
                        Undo.RecordObject(sourceState, "Remove Animator transition");
                        sourceState.RemoveTransition(transition);
                    }
                    return;
                }
                UpdateTransition(controller, transition, e);
            }
        }

        private static void UpdateState(AnimatorState state, JObject e)
        {
            Undo.RecordObject(state, "Edit Animator state");
            if (e["speed"] != null) state.speed = Number(e, "speed");
            if (e["cycleOffset"] != null) state.cycleOffset = Number(e, "cycleOffset");
            if (e["writeDefaultValues"] != null) state.writeDefaultValues =
                OptionalBool(e, "writeDefaultValues", state.writeDefaultValues);
            if (e["mirror"] != null) state.mirror = OptionalBool(e, "mirror", state.mirror);
            if (e["iKOnFeet"] != null) state.iKOnFeet = OptionalBool(e, "iKOnFeet", state.iKOnFeet);
            if (e["tag"] != null) state.tag = (string)e["tag"];
            if (e.Property("motionPath") != null)
            {
                string path = (string)e["motionPath"];
                if (string.IsNullOrWhiteSpace(path)) state.motion = null;
                else
                {
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                        throw new ArgumentException("Motion must be an Assets/ animation clip.");
                    AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
                    string clipName = (string)e["clipName"];
                    AnimationClip[] matches = string.IsNullOrEmpty(clipName) ? clips :
                        clips.Where(c => c.name == clipName).ToArray();
                    if (matches.Length != 1)
                        throw new ArgumentException("Motion clip is missing or ambiguous; supply clipName for " + path);
                    state.motion = matches[0];
                }
            }
            EditorUtility.SetDirty(state);
        }

        private static void UpdateTransition(AnimatorController controller, AnimatorStateTransition t, JObject e)
        {
            Undo.RecordObject(t, "Edit Animator transition");
            if (e["duration"] != null) t.duration = Number(e, "duration");
            if (e["hasExitTime"] != null) t.hasExitTime = OptionalBool(e, "hasExitTime", t.hasExitTime);
            if (e["exitTime"] != null) t.exitTime = Number(e, "exitTime");
            if (e["hasFixedDuration"] != null) t.hasFixedDuration =
                OptionalBool(e, "hasFixedDuration", t.hasFixedDuration);
            if (e["offset"] != null) t.offset = Number(e, "offset");
            if (e["canTransitionToSelf"] != null) t.canTransitionToSelf =
                OptionalBool(e, "canTransitionToSelf", t.canTransitionToSelf);
            if (e["conditions"] is JArray conditions)
            {
                foreach (AnimatorCondition condition in t.conditions) t.RemoveCondition(condition);
                foreach (JObject c in conditions)
                {
                    string name = Name(c, "parameter");
                    AnimatorControllerParameter parameter = controller.parameters.FirstOrDefault(p => p.name == name);
                    if (parameter == null) throw new ArgumentException("Unknown condition parameter: " + name);
                    AnimatorConditionMode mode = (AnimatorConditionMode)Enum.Parse(
                        typeof(AnimatorConditionMode), Name(c, "mode"));
                    t.AddCondition(mode, c["threshold"] == null ? 0 : Number(c, "threshold"), name);
                }
            }
            EditorUtility.SetDirty(t);
        }

        private static AnimatorStateTransition Transition(
            AnimatorControllerLayer layer, JObject edit, out AnimatorState sourceState)
        {
            string source = Name(edit, "source");
            sourceState = source == "*" ? null : State(layer, source);
            AnimatorStateTransition[] transitions = sourceState == null
                ? layer.stateMachine.anyStateTransitions : sourceState.transitions;
            int index = OptionalInt(edit, "index", -1);
            if (index < 0 || index >= transitions.Length)
                throw new ArgumentException("Transition index is out of range for source: " + source);
            AnimatorStateTransition transition = transitions[index];
            string expected = (string)edit["expectedDestination"];
            if (!string.IsNullOrEmpty(expected) &&
                (transition.destinationState == null ? "" : transition.destinationState.name) != expected)
                throw new InvalidOperationException("Transition destination changed since inspection.");
            return transition;
        }

        private static AnimatorControllerLayer Layer(AnimatorController controller, string name)
        {
            AnimatorControllerLayer layer = controller.layers.FirstOrDefault(l => l.name == name);
            if (layer == null || layer.stateMachine == null)
                throw new ArgumentException("Animator layer not found: " + name);
            return layer;
        }

        private static AnimatorStateMachine Machine(AnimatorControllerLayer layer, string path)
        {
            AnimatorStateMachine current = layer.stateMachine;
            if (string.IsNullOrEmpty(path)) return current;
            foreach (string name in path.Split('/'))
            {
                ChildAnimatorStateMachine[] candidates = current.stateMachines
                    .Where(child => child.stateMachine != null && child.stateMachine.name == name).ToArray();
                if (candidates.Length != 1)
                    throw new ArgumentException("Nested state machine not found or ambiguous: " + path);
                current = candidates[0].stateMachine;
            }
            return current;
        }

        private static AnimatorState State(AnimatorControllerLayer layer, string path)
        {
            string[] pieces = path.Split('/');
            AnimatorStateMachine machine = Machine(layer,
                string.Join("/", pieces.Take(pieces.Length - 1).ToArray()));
            ChildAnimatorState[] states = machine.states.Where(s => s.state != null &&
                s.state.name == pieces[pieces.Length - 1]).ToArray();
            if (states.Length != 1)
                throw new ArgumentException("Animator state not found or ambiguous: " + path);
            return states[0].state;
        }

        private static AnimatorControllerParameterType ParameterType(string name)
        {
            if (!Enum.TryParse(name, out AnimatorControllerParameterType type) ||
                !Enum.IsDefined(typeof(AnimatorControllerParameterType), type))
                throw new ArgumentException("Invalid Animator parameter type: " + name);
            return type;
        }
        private static string Name(JObject e, string field)
        {
            string value = (string)e[field];
            if (string.IsNullOrWhiteSpace(value) || value.Length > 150)
                throw new ArgumentException("Missing or invalid Animator " + field);
            return value;
        }
        private static float Number(JObject e, string field)
        {
            if (e[field] == null || (e[field].Type != JTokenType.Float &&
                e[field].Type != JTokenType.Integer))
                throw new ArgumentException("Expected numeric Animator field: " + field);
            float n = e[field].Value<float>();
            if (float.IsNaN(n) || float.IsInfinity(n)) throw new ArgumentException("Non-finite " + field);
            return n;
        }
        private static bool OptionalBool(JObject e, string field, bool fallback)
        {
            if (e[field] == null) return fallback;
            if (e[field].Type != JTokenType.Boolean)
                throw new ArgumentException("Expected boolean " + field);
            return e[field].Value<bool>();
        }
        private static int OptionalInt(JObject e, string field, int fallback)
        {
            if (e[field] == null) return fallback;
            if (e[field].Type != JTokenType.Integer)
                throw new ArgumentException("Expected integer " + field);
            return e[field].Value<int>();
        }
        private static float OptionalFloat(JObject e, string field, float fallback)
        {
            return e[field] == null ? fallback : Number(e, field);
        }
    }
}
