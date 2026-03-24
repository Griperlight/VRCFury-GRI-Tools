using System.Collections.Generic;
using UnityEngine;

namespace VRCFuryMenuBuilder
{
    // ── Per-object mode (mirrors VRCFury ObjectToggle.Mode enum) ─────────────────
    public enum ObjectToggleMode
    {
        TurnOn  = 0,
        TurnOff = 1,
    }

    [System.Serializable]
    public class ObjectToggleEntry
    {
        public GameObject      Object = null;
        public ObjectToggleMode Mode  = ObjectToggleMode.TurnOn;

        public ObjectToggleEntry() { }
        public ObjectToggleEntry(GameObject go, ObjectToggleMode mode = ObjectToggleMode.TurnOn)
        { Object = go; Mode = mode; }

        public ObjectToggleEntry Clone() => new ObjectToggleEntry(Object, Mode);
    }

    [System.Serializable]
    public class ToggleData
    {
        /// <summary>Position in VRCFury features list. Only valid when IsNew is false.</summary>
        public int  Index    = -1;
        /// <summary>
        /// Explicit flag — true = new draft, false = editing existing.
        /// Stored as bool so Unity serialization can't corrupt it (int defaults to 0 after recompile).
        /// </summary>
        public bool IsNew    = true;

        public string Name      = "";
        public string MenuPath  = "";

        public List<ObjectToggleEntry> ObjectEntries = new List<ObjectToggleEntry>();

        public Texture2D Icon      = null;

        public bool DefaultOn  = false;
        public bool Inverted   = false;
        public bool SavedParam = false;
        public bool UseInt     = false;
        public bool SliderMode = false;

        public string ParamName = "";

        public ToggleData Clone() => new ToggleData
        {
            Index        = Index,
            IsNew        = IsNew,
            Name         = Name,
            MenuPath     = MenuPath,
            ObjectEntries= ObjectEntries.ConvertAll(e => e.Clone()),
            Icon         = Icon,
            DefaultOn    = DefaultOn,
            Inverted     = Inverted,
            SavedParam   = SavedParam,
            UseInt       = UseInt,
            SliderMode   = SliderMode,
            ParamName    = ParamName,
        };
    }

    [System.Serializable]
    public class SubmenuData
    {
        public string Name       = "";
        public string ParentPath = "";
        public bool   IsExpanded = true;

        public string FullPath => string.IsNullOrEmpty(ParentPath) ? Name : $"{ParentPath}/{Name}";
    }
}
