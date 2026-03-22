using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FDataTable.Runtime;

namespace FDataTable.Editor
{
    /// <summary>
    /// Resolves which fields/properties of a ScriptableObject should be shown as columns.
    /// Supports: primitives, string, enum, all common Unity value types, LayerMask,
    /// AnimationCurve, Gradient, arrays, List&lt;T&gt;, [Serializable] structs/classes,
    /// and UnityEngine.Object references.
    /// </summary>
    public static class FDataTableFieldResolver
    {
        private static readonly HashSet<Type> SupportedTypes = new HashSet<Type>
        {
            // Primitives
            typeof(int),    typeof(uint),   typeof(long),   typeof(ulong),
            typeof(float),  typeof(double), typeof(bool),
            typeof(byte),   typeof(sbyte),  typeof(short),  typeof(ushort),
            typeof(char),   typeof(string),
            // Unity value types
            typeof(Vector2),    typeof(Vector3),    typeof(Vector4),
            typeof(Vector2Int), typeof(Vector3Int),
            typeof(Color),      typeof(Color32),
            typeof(Quaternion),
            typeof(Rect),       typeof(RectInt),
            typeof(Bounds),     typeof(BoundsInt),
            typeof(LayerMask),
            // Unity reference types handled by PropertyField
            typeof(AnimationCurve),
            typeof(Gradient),
        };

        public static List<FieldInfo> GetDisplayableFields(Type type, FDataTableTypeConfig config)
        {
            var result = new List<FieldInfo>();

            // Walk the entire inheritance chain up to (but not including) ScriptableObject,
            // so that private [SerializeField] fields on parent classes are also included.
            // Parent fields are added first to preserve the natural declaration order.
            var typeChain = new List<Type>();
            var t = type;
            while (t != null && t != typeof(ScriptableObject) && t != typeof(UnityEngine.Object))
            {
                typeChain.Add(t);
                t = t.BaseType;
            }
            typeChain.Reverse(); // base → derived order

            var seenNames = new HashSet<string>(); // guard against duplicate field names

            foreach (var currentType in typeChain)
            {
                // DeclaredOnly so we get private fields on each tier of the hierarchy.
                var fields = currentType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var field in fields)
                {
                    if (!seenNames.Add(field.Name)) continue; // skip shadowed fields

                    // Skip if tagged with ignore attribute
                    if (field.GetCustomAttribute<FDataTableIgnoreAttribute>() != null) continue;

                    // Only show serialized fields
                    bool isPublic        = field.IsPublic;
                    bool hasSerialized   = field.GetCustomAttribute<SerializeField>() != null;
                    bool hasNonSerialized= field.GetCustomAttribute<NonSerializedAttribute>() != null;

                    if ((!isPublic && !hasSerialized) || hasNonSerialized) continue;

                    // Apply exclusion list from config
                    if (config != null && config.excludedFields.Contains(field.Name)) continue;

                    // Apply inclusion list from config (if specified)
                    if (config != null && config.includedFields.Count > 0 &&
                        !config.includedFields.Contains(field.Name)) continue;

                    if (IsSupportedType(field.FieldType))
                        result.Add(field);
                }
            }

            return result;
        }

        public static bool IsSupportedType(Type t)
        {
            if (SupportedTypes.Contains(t)) return true;
            if (t.IsEnum) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true;
            if (IsArrayOrList(t)) return true;
            if (IsSerializableClass(t)) return true;
            return false;
        }

        /// <summary>Returns true for T[] and List&lt;T&gt;.</summary>
        public static bool IsArrayOrList(Type t)
        {
            if (t.IsArray) return true;
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
        }

        /// <summary>Returns true for user-defined [Serializable] structs and classes.</summary>
        public static bool IsSerializableClass(Type t)
        {
            if (t.IsPrimitive || t == typeof(string)) return false;
            if (t.IsAbstract || t.IsInterface) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
            return t.GetCustomAttribute<SerializableAttribute>() != null;
        }
    }
}
