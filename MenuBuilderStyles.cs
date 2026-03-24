using UnityEditor;
using UnityEngine;

namespace VRCFuryMenuBuilder
{
    /// <summary>
    /// All GUIStyle definitions and palette for the Menu Builder window.
    /// Must be created inside OnGUI (after EditorStyles are initialised).
    /// </summary>
    public class MenuBuilderStyles
    {
        // ── Styles ───────────────────────────────────────────────────────────────
        public GUIStyle Header;
        public GUIStyle SubHeader;
        public GUIStyle Tab;
        public GUIStyle Card;
        public GUIStyle SectionLabel;
        public GUIStyle Hint;
        public GUIStyle Tag;
        public GUIStyle BtnCreate;
        public GUIStyle BtnDanger;

        // ── Palette ──────────────────────────────────────────────────────────────
        public static readonly Color ColAccent    = new Color(0.45f, 0.65f, 1.00f);
        public static readonly Color ColSuccess   = new Color(0.35f, 0.85f, 0.50f);
        public static readonly Color ColWarning   = new Color(1.00f, 0.75f, 0.25f);
        public static readonly Color ColDanger    = new Color(0.95f, 0.35f, 0.35f);
        public static readonly Color ColMuted     = new Color(0.50f, 0.50f, 0.55f);
        public static readonly Color ColCardBg    = new Color(0.20f, 0.20f, 0.23f);
        public static readonly Color ColSeparator = new Color(0.28f, 0.28f, 0.32f);
        public static readonly Color ColTagBg     = new Color(0.22f, 0.28f, 0.42f);

        public MenuBuilderStyles()
        {
            // ── Header ─────────────────────────────────────────────────────────
            Header = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 15,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = new Color(0.85f, 0.88f, 1f) }
            };

            SubHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.75f, 0.80f, 1f) }
            };

            // ── Toolbar tabs ───────────────────────────────────────────────────
            Tab = new GUIStyle(EditorStyles.toolbarButton)
            {
                fontSize    = 11,
                fixedHeight = 26
            };

            // ── Section label (ALL CAPS small) ─────────────────────────────────
            SectionLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.55f, 0.68f, 0.95f) }
            };

            // ── Muted hint text ────────────────────────────────────────────────
            Hint = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal   = { textColor = ColMuted }
            };

            // ── Inline tag / badge ─────────────────────────────────────────────
            Tag = new GUIStyle(EditorStyles.miniLabel)
            {
                padding = new RectOffset(5, 5, 1, 1),
                normal  = { background = MakeTex(ColTagBg), textColor = ColAccent }
            };

            // ── Card background ────────────────────────────────────────────────
            Card = new GUIStyle
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin  = new RectOffset(4, 4, 3, 3),
                normal  = { background = MakeTex(ColCardBg) }
            };

            // ── Create button (green-ish) ──────────────────────────────────────
            BtnCreate = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                normal  = { background = MakeTex(new Color(0.18f, 0.42f, 0.22f)), textColor = Color.white },
                hover   = { background = MakeTex(new Color(0.22f, 0.52f, 0.28f)), textColor = Color.white },
                active  = { background = MakeTex(new Color(0.14f, 0.35f, 0.18f)), textColor = Color.white }
            };

            // ── Danger button (red) ────────────────────────────────────────────
            BtnDanger = new GUIStyle(GUI.skin.button)
            {
                normal  = { background = MakeTex(new Color(0.45f, 0.12f, 0.12f)), textColor = Color.white },
                hover   = { background = MakeTex(new Color(0.58f, 0.18f, 0.18f)), textColor = Color.white },
                active  = { background = MakeTex(new Color(0.35f, 0.09f, 0.09f)), textColor = Color.white }
            };
        }

        // ── Texture helper ───────────────────────────────────────────────────────
        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }
    }
}
