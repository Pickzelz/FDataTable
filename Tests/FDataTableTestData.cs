using System;
using System.Collections.Generic;
using FDataTable.Runtime;
using UnityEngine;

namespace FDataTable.Tests
{
    /// <summary>
    /// Test ScriptableObject that exercises every field type supported by FDataTable.
    /// Create instances via: Assets → Create → FDataTable → Test Data
    /// </summary>
    [CreateAssetMenu(fileName = "TestData", menuName = "FDataTable/Test Data", order = 99)]
    public class FDataTableTestData : ScriptableObject
    {
        // ── Integer-family ────────────────────────────────────────────────────
        public int    fieldInt;
        public uint   fieldUInt;
        public long   fieldLong;
        public short  fieldShort;
        public byte   fieldByte;

        // ── Floating-point ───────────────────────────────────────────────────
        public float  fieldFloat;
        public double fieldDouble;

        // ── Boolean ──────────────────────────────────────────────────────────
        public bool fieldBool;

        // ── String ───────────────────────────────────────────────────────────
        public string fieldString;

        // ── Enum ─────────────────────────────────────────────────────────────
        public enum ETestRarity { Common, Uncommon, Rare, Epic, Legendary }
        public ETestRarity fieldEnum;

        // ── Unity vector types ────────────────────────────────────────────────
        public Vector2    fieldVector2;
        public Vector3    fieldVector3;
        public Vector4    fieldVector4;
        public Vector2Int fieldVector2Int;
        public Vector3Int fieldVector3Int;
        public Quaternion fieldQuaternion;

        // ── Color ─────────────────────────────────────────────────────────────
        public Color   fieldColor;
        public Color32 fieldColor32;

        // ── Geometry ──────────────────────────────────────────────────────────
        public Rect      fieldRect;
        public RectInt   fieldRectInt;
        public Bounds    fieldBounds;
        public BoundsInt fieldBoundsInt;

        // ── Misc Unity types ──────────────────────────────────────────────────
        public LayerMask      fieldLayerMask;
        public AnimationCurve fieldAnimationCurve;
        public Gradient       fieldGradient;

        // ── Object references ─────────────────────────────────────────────────
        public Sprite    fieldSprite;
        public Texture2D fieldTexture2D;
        public AudioClip fieldAudioClip;

        // ── Serializable struct ───────────────────────────────────────────────
        [Serializable]
        public struct SampleStats
        {
            public string label;
            public int    value;
            public Color  tint;
        }
        public SampleStats fieldStruct;

        // ── Serializable nested struct ────────────────────────────────────────
        [Serializable]
        public struct DropEntry
        {
            public string    itemName;
            public float     chance;
            public ETestRarity rarity;
        }

        // ── Array ─────────────────────────────────────────────────────────────
        public int[]     fieldIntArray;
        public string[]  fieldStringArray;

        // ── List<T> ───────────────────────────────────────────────────────────
        public List<float>     fieldFloatList;
        public List<SampleStats> fieldStructList;

        // ── Struct array ──────────────────────────────────────────────────────
        public DropEntry[] fieldDropTable;

        // ── FDataTableIgnore ──────────────────────────────────────────────────
        [FDataTableIgnore]
        public string hiddenField = "This field should NOT appear in FDataTable.";
    }
}
