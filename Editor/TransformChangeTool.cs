using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WasimDevelopment.UnityMcpBridge
{
    /// <summary>
    /// Converts user-friendly Transform coordinates to guarded, locally approved
    /// serialized-property change sets. Never changes the scene during proposal.
    /// </summary>
    internal static class TransformChangeTool
    {
        public static JObject Propose(JObject args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            string space = args["space"]?.Value<string>() ?? "local";
            if (space != "local" && space != "world")
                throw new ArgumentException("space must be 'local' or 'world'.");
            if (args["relative"] != null && args["relative"].Type != JTokenType.Boolean)
                throw new ArgumentException("relative must be a boolean.");
            bool relative = args["relative"]?.Value<bool>() ?? false;
            Vector3? position = ReadOptionalVector(args, "position");
            Vector3? rotation = ReadOptionalVector(args, "rotation");
            Vector3? scale = ReadOptionalVector(args, "scale");
            if (!position.HasValue && !rotation.HasValue && !scale.HasValue)
                throw new ArgumentException("Provide at least one of position, rotation or scale.");

            // Resolving first supports the selected GameObject or an explicit
            // GameObject/component ID; ChangeSets.Propose validates stable scene IDs.
            GameObject go = ObjectAccess.SelectedOrId(args);
            Transform target = go.transform;
            string targetId = ObjectAccess.Id(target);
            var operations = new JArray();

            if (position.HasValue)
            {
                Vector3 desired = position.Value;
                if (space == "world")
                {
                    if (relative) desired += target.position;
                    if (target.parent != null) desired = target.parent.InverseTransformPoint(desired);
                }
                else if (relative) desired += target.localPosition;
                EnsureFinite(desired, "position");
                operations.Add(Property(targetId, "m_LocalPosition", ObjectAccess.Value(desired)));
            }

            if (rotation.HasValue)
            {
                Quaternion requested = Quaternion.Euler(rotation.Value);
                Quaternion desired;
                if (space == "world")
                {
                    desired = relative ? requested * target.rotation : requested;
                    if (target.parent != null) desired = Quaternion.Inverse(target.parent.rotation) * desired;
                }
                else desired = relative ? target.localRotation * requested : requested;
                desired.Normalize();
                EnsureFinite(desired, "rotation");
                operations.Add(Property(targetId, "m_LocalRotation", ObjectAccess.Value(desired)));
            }

            if (scale.HasValue)
            {
                Vector3 desired;
                if (space == "world")
                {
                    // General world scale is not representable as a simple local
                    // scale under nonuniform ancestors (shear/rotation). Reject.
                    float factor = UniformParentScale(target.parent);
                    desired = relative ? Vector3.Scale(target.localScale, scale.Value) : scale.Value / factor;
                }
                else desired = relative ? Vector3.Scale(target.localScale, scale.Value) : scale.Value;
                EnsureFinite(desired, "scale");
                operations.Add(Property(targetId, "m_LocalScale", ObjectAccess.Value(desired)));
            }

            string summary = args["summary"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(summary))
                summary = "Set " + go.name + " Transform (" + space + ", "
                    + (relative ? "relative" : "absolute") + ")";
            return ChangeSets.Propose(new JObject { ["summary"] = summary, ["operations"] = operations });
        }

        private static JObject Property(string id, string path, JToken value) =>
            new JObject { ["kind"] = "set_property", ["targetId"] = id,
                ["propertyPath"] = path, ["value"] = value };

        private static Vector3? ReadOptionalVector(JObject args, string name)
        {
            JToken token = args[name];
            if (token == null) return null;
            if (!(token is JObject obj) || obj.Count != 3)
                throw new ArgumentException(name + " must contain exactly numeric x, y, z.");
            return new Vector3(Number(obj, "x", name), Number(obj, "y", name), Number(obj, "z", name));
        }

        private static float Number(JObject obj, string axis, string field)
        {
            JToken value = obj[axis];
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                throw new ArgumentException(field + "." + axis + " must be a number.");
            double number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || number > float.MaxValue || number < -float.MaxValue)
                throw new ArgumentException(field + "." + axis + " must be a finite float.");
            return (float)number;
        }

        private static float UniformParentScale(Transform parent)
        {
            float factor = 1f;
            for (Transform ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                Vector3 scale = ancestor.localScale;
                EnsureFinite(scale, "ancestor scale");
                float tolerance = 1e-4f * Mathf.Max(1f, Mathf.Abs(scale.x));
                if (scale.x <= 1e-5f ||
                    Mathf.Abs(scale.x - scale.y) > tolerance ||
                    Mathf.Abs(scale.x - scale.z) > tolerance)
                    throw new InvalidOperationException(
                        "World scale requires every parent to have a positive uniform scale. "
                        + "Use space='local' for nonuniform or mirrored hierarchies.");
                factor *= scale.x;
                if (float.IsInfinity(factor) || factor < 1e-10f)
                    throw new InvalidOperationException("Parent world scale is not safely invertible.");
            }
            return factor;
        }

        private static void EnsureFinite(Vector3 value, string field)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z))
                throw new ArgumentException(field + " must contain finite coordinates.");
        }

        private static void EnsureFinite(Quaternion value, string field)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z) ||
                float.IsNaN(value.w) || float.IsInfinity(value.w))
                throw new ArgumentException(field + " must produce a finite Quaternion.");
        }
    }
}
