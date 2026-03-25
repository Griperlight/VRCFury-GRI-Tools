using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCFuryMenuBuilder
{
    /// <summary>
    /// Reflection bridge to VRCFury internals.
    ///
    /// Architecture:
    ///   Avatar Root  → VRCFury with FixWriteDefaults (Auto) only
    ///   Avatar Root/Menus → VRCFury with all Toggle features
    ///
    /// Public surface:
    ///   SetupAvatarRoot()        — adds FixWriteDefaults to root (one-time)
    ///   GetOrCreateMenusObject() — finds/creates "Menus" child + its VRCFury
    ///   CreateToggle()           — appends a new toggle to Menus VRCFury
    ///   UpdateToggle()           — overwrites a toggle at a given index
    ///   DeleteToggle()           — removes a toggle at a given index
    ///   GetExistingToggles()     — reads all toggles as ToggleData list
    /// </summary>
    public static class VRCFuryBridge
    {
        // ── Cached Types ─────────────────────────────────────────────────────────
        private static Type _vrcFuryType;
        private static Type _vrcFuryConfigType;
        private static Type _toggleFeatureType;
        private static Type _objectToggleType;
        private static Type _fixWriteDefaultsType;

        // ── Cached Fields ────────────────────────────────────────────────────────
        private static FieldInfo _fi_config;
        private static FieldInfo _fi_features;
        private static FieldInfo _fi_toggle_name;
        private static FieldInfo _fi_toggle_menuPath;
        private static FieldInfo _fi_toggle_defaultOn;
        private static FieldInfo _fi_toggle_invertRest;
        private static FieldInfo _fi_toggle_icon;
        private static FieldInfo _fi_toggle_objects;
        private static FieldInfo _fi_toggle_paramName;
        private static FieldInfo _fi_objToggle_obj;
        private static FieldInfo _fi_objToggle_mode;   // ObjectToggle.mode (TurnOn/TurnOff enum)
        private static Type      _objectToggleModeType; // the Mode enum type
        private static FieldInfo _fi_toggle_savedParam;
        private static FieldInfo _fi_toggle_useInt;
        private static FieldInfo _fi_toggle_slider;
        private static FieldInfo _fi_fwd_fixMode;   // FixWriteDefaults.fixSetting / mode

        // ── Public State ─────────────────────────────────────────────────────────
        public static bool IsAvailable { get; private set; }

        // ── Type / field name candidates ─────────────────────────────────────────
        private static readonly string[] VRCFuryCandidates = {
            "VF.Component.VRCFury", "VF.VRCFury", "VRCFury" };
        private static readonly string[] ConfigCandidates = {
            "VF.Model.VRCFuryConfig", "VF.VRCFuryConfig", "VRCFuryConfig" };
        private static readonly string[] ToggleCandidates = {
            "VF.Model.Feature.Toggle", "VF.Feature.Toggle", "VRCFuryFeatureToggle" };
        private static readonly string[] ObjectToggleCandidates = {
            "VF.Model.Feature.Toggle+ObjectToggle",
            "VF.Model.Feature.ObjectToggle",
            "VF.Feature.Toggle+ObjectToggle" };
        private static readonly string[] FixWriteDefaultsCandidates = {
            "VF.Model.Feature.FixWriteDefaults",
            "VF.Feature.FixWriteDefaults",
            "VRCFuryFeatureFixWriteDefaults" };

        private static readonly string[] ConfigFieldNames    = { "config",   "_config",   "Config"   };
        private static readonly string[] FeaturesFieldNames  = { "features", "_features", "Features" };
        private static readonly string[] NameFieldNames      = { "name",     "Name",      "title"    };
        private static readonly string[] MenuPathFieldNames  = { "menuPath", "MenuPath",  "menu", "path" };
        private static readonly string[] DefaultOnFieldNames = { "defaultOn","DefaultOn", "isDefaultOn" };
        private static readonly string[] InvertFieldNames    = { "invertRestState","InvertRestState","invert","inverted" };
        private static readonly string[] IconFieldNames      = { "icon",     "Icon",      "menuIcon" };
        private static readonly string[] ObjectsFieldNames   = { "objects",  "Objects",   "toggleObjects","objList" };
        private static readonly string[] ParamNameFieldNames = { "paramName","ParamName", "parameter","param" };
        private static readonly string[] ObjFieldNames        = { "obj",       "Obj",        "gameObject","go" };
        private static readonly string[] ObjModeFieldNames    = { "mode",      "Mode",       "action","Action","state","State" };
        private static readonly string[] SavedParamFieldNames = { "savedParam","SavedParam", "save","saved" };
        private static readonly string[] UseIntFieldNames     = { "useInt",    "UseInt",     "intParam","integer" };
        private static readonly string[] SliderFieldNames     = { "slider",    "Slider",     "radial","useSlider","sliderMode" };
        private static readonly string[] FixModeFieldNames    = { "fixSetting","mode","Mode","fixMode","FixMode" };

        // ── Init ─────────────────────────────────────────────────────────────────
        static VRCFuryBridge() => Initialize();

        public static void Initialize()
        {
            IsAvailable = false;
            var log = new System.Text.StringBuilder();

            try
            {
                var allTypes = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .ToArray();

                var vfTypes = allTypes
                    .Where(t => t.FullName != null &&
                               (t.FullName.StartsWith("VF.") || t.FullName == "VRCFury" ||
                                t.FullName.StartsWith("VRCFury.")))
                    .OrderBy(t => t.FullName).ToList();

                if (vfTypes.Count == 0) return;

                const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                _vrcFuryType = TryFindType(VRCFuryCandidates, allTypes)
                            ?? vfTypes.FirstOrDefault(t => t.Name == "VRCFury" && InheritsFrom(t, "MonoBehaviour"));
                if (_vrcFuryType == null) return;

                _fi_config = TryFindField(_vrcFuryType, ConfigFieldNames, BF);

                _vrcFuryConfigType = _fi_config?.FieldType ?? TryFindType(ConfigCandidates, allTypes);

                if (_vrcFuryConfigType != null)
                    _fi_features = TryFindField(_vrcFuryConfigType, FeaturesFieldNames, BF);

                _toggleFeatureType = TryFindType(ToggleCandidates, allTypes)
                                  ?? vfTypes.FirstOrDefault(t => t.Name == "Toggle" && InheritsFrom(t, "ScriptableObject"))
                                  ?? vfTypes.FirstOrDefault(t => t.Name == "Toggle");

                if (_toggleFeatureType != null)
                {
                    _fi_toggle_name       = TryFindField(_toggleFeatureType, NameFieldNames,      BF);
                    _fi_toggle_menuPath   = TryFindField(_toggleFeatureType, MenuPathFieldNames,   BF);
                    _fi_toggle_defaultOn  = TryFindField(_toggleFeatureType, DefaultOnFieldNames,  BF);
                    _fi_toggle_invertRest = TryFindField(_toggleFeatureType, InvertFieldNames,     BF);
                    _fi_toggle_icon       = TryFindField(_toggleFeatureType, IconFieldNames,       BF);
                    _fi_toggle_objects    = TryFindField(_toggleFeatureType, ObjectsFieldNames,    BF);
                    _fi_toggle_paramName  = TryFindField(_toggleFeatureType, ParamNameFieldNames,  BF);
                    _fi_toggle_savedParam = TryFindField(_toggleFeatureType, SavedParamFieldNames, BF);
                    _fi_toggle_useInt     = TryFindField(_toggleFeatureType, UseIntFieldNames,     BF);
                    _fi_toggle_slider     = TryFindField(_toggleFeatureType, SliderFieldNames,     BF);
                }

                _objectToggleType = TryFindType(ObjectToggleCandidates, allTypes);
                if (_objectToggleType == null && _toggleFeatureType != null)
                    _objectToggleType = _toggleFeatureType.GetNestedTypes(BF)
                        .FirstOrDefault(t => t.Name.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0
                                          || t.Name.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0);

                if (_objectToggleType != null)
                {
                    _fi_objToggle_obj  = TryFindField(_objectToggleType, ObjFieldNames,     BF);
                    _fi_objToggle_mode = TryFindField(_objectToggleType, ObjModeFieldNames, BF);
                    if (_fi_objToggle_mode != null && _fi_objToggle_mode.FieldType.IsEnum)
                        _objectToggleModeType = _fi_objToggle_mode.FieldType;
                }

                _fixWriteDefaultsType = TryFindType(FixWriteDefaultsCandidates, allTypes)
                                     ?? vfTypes.FirstOrDefault(t => t.Name.Contains("FixWriteDefaults") || t.Name.Contains("WriteDefaults"));

                if (_fixWriteDefaultsType != null)
                    _fi_fwd_fixMode = TryFindField(_fixWriteDefaultsType, FixModeFieldNames, BF);

                IsAvailable = _vrcFuryType != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gri Tools] VRCFuryBridge.Initialize: {e}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // AVATAR SETUP
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds a VRCFury component to the avatar ROOT containing only
        /// a FixWriteDefaults feature set to Auto.
        /// Safe to call multiple times — won't duplicate if already set up.
        /// Returns true if something was changed.
        /// </summary>
        public static bool SetupAvatarRoot(GameObject avatar)
        {
            if (!IsAvailable || avatar == null) return false;

            var existing = avatar.GetComponent(_vrcFuryType);
            if (existing != null && HasFixWriteDefaults(existing))
            {
                Debug.Log("[Gri Tools] Avatar root already has FixWriteDefaults — skipping.");
                return false;
            }

            Undo.SetCurrentGroupName("Gri Tools: Setup Avatar Root");

            Component comp = existing;
            if (comp == null)
            {
                comp = Undo.AddComponent(avatar, _vrcFuryType);
                InitializeConfigIfNull(comp);
            }

            AddFixWriteDefaults(comp);
            EditorUtility.SetDirty(avatar);
            return true;
        }

        // ── Avatar status ────────────────────────────────────────────────────────

        public class AvatarStatus
        {
            public bool HasRootVRCFury      = false;
            public bool HasFixWriteDefaults = false;
            public bool HasMenusObject      = false;
            public bool HasMenusVRCFury     = false;
            public string DebugLog          = "";
        }

        public static AvatarStatus GetAvatarStatus(GameObject avatar)
        {
            var s   = new AvatarStatus();
            var log = new System.Text.StringBuilder();

            if (avatar == null || _vrcFuryType == null)
            {
                s.DebugLog = "Avatar or VRCFury type is null.";
                return s;
            }

            var rootComp = avatar.GetComponent(_vrcFuryType);
            s.HasRootVRCFury = rootComp != null;
            log.AppendLine($"Root VRCFury: {(s.HasRootVRCFury ? "✓" : "✗")}");

            if (s.HasRootVRCFury)
            {
                s.HasFixWriteDefaults = DetectFixWriteDefaults(rootComp, log);
                log.AppendLine($"FixWriteDefaults: {(s.HasFixWriteDefaults ? "✓" : "✗")}");
            }

            var menusTf = avatar.transform.Find("Menus");
            s.HasMenusObject = menusTf != null;
            log.AppendLine($"Menus GameObject: {(s.HasMenusObject ? "✓" : "✗")}");

            if (s.HasMenusObject)
            {
                var menusComp = menusTf.GetComponent(_vrcFuryType);
                s.HasMenusVRCFury = menusComp != null;
                log.AppendLine($"Menus VRCFury: {(s.HasMenusVRCFury ? "✓" : "✗")}");
            }

            s.DebugLog = log.ToString();
            return s;
        }

        private static bool DetectFixWriteDefaults(Component vrcFury, System.Text.StringBuilder log = null)
        {
            if (vrcFury == null) return false;

            // Strategy 1: via resolved features list
            var features = GetFeaturesList(vrcFury);
            if (features != null)
            {
                foreach (var f in features)
                {
                    if (f == null) continue;
                    if (_fixWriteDefaultsType != null && _fixWriteDefaultsType.IsInstanceOfType(f))
                        return true;
                    var n = f.GetType().Name;
                    if (n.IndexOf("FixWriteDefaults", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("WriteDefaults",    StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            // Strategy 2: walk SerializedObject managed references
            try
            {
                var so   = new SerializedObject(vrcFury);
                var iter = so.GetIterator();
                while (iter.NextVisible(true))
                {
                    if (iter.propertyType != SerializedPropertyType.ManagedReference) continue;
                    var refVal = iter.managedReferenceValue;
                    if (refVal == null) continue;
                    var n = refVal.GetType().Name;
                    if (n.IndexOf("FixWriteDefaults", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("WriteDefaults",    StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception e)
            {
                log?.AppendLine($"  SerializedObject strategy failed: {e.Message}");
            }

            // Strategy 3: check managed reference type name strings
            try
            {
                var so   = new SerializedObject(vrcFury);
                var iter = so.GetIterator();
                while (iter.NextVisible(true))
                {
                    if (iter.propertyType != SerializedPropertyType.ManagedReference) continue;
                    var refTypeName = iter.managedReferenceFullTypename ?? "";
                    if (refTypeName.IndexOf("FixWriteDefaults", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        refTypeName.IndexOf("WriteDefaults",    StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception e)
            {
                log?.AppendLine($"  Type name string strategy failed: {e.Message}");
            }

            return false;
        }

        public static bool HasFixWriteDefaults(Component vrcFury)
            => vrcFury != null && DetectFixWriteDefaults(vrcFury);

        // ═════════════════════════════════════════════════════════════════════════
        // MENUS OBJECT
        // ═════════════════════════════════════════════════════════════════════════

        public static Component GetOrCreateMenusVRCFury(GameObject avatar)
        {
            if (!IsAvailable || avatar == null) return null;

            var menusGO = GetOrCreateMenusGameObject(avatar);
            if (menusGO == null) return null;

            var comp = menusGO.GetComponent(_vrcFuryType);
            if (comp != null) return comp;

            Undo.SetCurrentGroupName("Gri Tools: Create Menus VRCFury");
            comp = Undo.AddComponent(menusGO, _vrcFuryType);
            InitializeConfigIfNull(comp);
            EditorUtility.SetDirty(menusGO);
            return comp;
        }

        public static Component GetMenusVRCFury(GameObject avatar)
        {
            if (avatar == null || _vrcFuryType == null) return null;
            var menusGO = FindMenusGameObject(avatar);
            return menusGO != null ? menusGO.GetComponent(_vrcFuryType) : null;
        }

        private static GameObject FindMenusGameObject(GameObject avatar)
        {
            var t = avatar.transform.Find("Menus");
            return t != null ? t.gameObject : null;
        }

        private static GameObject GetOrCreateMenusGameObject(GameObject avatar)
        {
            var existing = FindMenusGameObject(avatar);
            if (existing != null) return existing;

            Undo.SetCurrentGroupName("Gri Tools: Create Menus GameObject");
            var go = new GameObject("Menus");
            Undo.RegisterCreatedObjectUndo(go, "Create Menus");
            Undo.SetTransformParent(go.transform, avatar.transform, "Parent Menus to Avatar");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;
            return go;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // TOGGLE CRUD
        // ═════════════════════════════════════════════════════════════════════════

        public static bool CreateToggle(Component vrcFury, ToggleData data)
        {
            if (!CanWrite(vrcFury)) return false;
            try
            {
                var features = EnsureFeaturesList(vrcFury);
                if (features == null) return false;

                var toggleInstance = Activator.CreateInstance(_toggleFeatureType);
                WriteFieldsDirect(toggleInstance, data);

                Undo.RecordObject(vrcFury, "Gri Tools: Create Toggle");
                features.Add(toggleInstance);

                EditorUtility.SetDirty(vrcFury);
                var so = new SerializedObject(vrcFury);
                so.ApplyModifiedProperties();
                so.Update();
                so.ApplyModifiedProperties();

                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception e) { Debug.LogError($"[Gri Tools] CreateToggle: {e}"); return false; }
        }

        public static bool UpdateToggle(Component vrcFury, int index, ToggleData data)
        {
            if (!CanWrite(vrcFury)) return false;
            try
            {
                var features = GetFeaturesList(vrcFury);
                if (features == null || index < 0 || index >= features.Count)
                {
                    Debug.LogError($"[Gri Tools] UpdateToggle: index {index} out of range (count={features?.Count}).");
                    return false;
                }

                var existing = features[index];
                if (existing == null || !_toggleFeatureType.IsInstanceOfType(existing))
                {
                    Debug.LogError($"[Gri Tools] UpdateToggle: element at {index} is not a Toggle.");
                    return false;
                }

                WriteFieldsDirect(existing, data);

                Undo.RecordObject(vrcFury, "Gri Tools: Update Toggle");
                EditorUtility.SetDirty(vrcFury);
                var so = new SerializedObject(vrcFury);
                so.ApplyModifiedProperties();
                so.Update();
                so.ApplyModifiedProperties();

                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception e) { Debug.LogError($"[Gri Tools] UpdateToggle: {e}"); return false; }
        }

        public static bool DeleteToggle(Component vrcFury, int index)
        {
            if (!CanWrite(vrcFury)) return false;
            try
            {
                Undo.RecordObject(vrcFury, "Gri Tools: Delete Toggle");
                var so = new SerializedObject(vrcFury);
                so.Update();

                var featuresArr = FindFeaturesArrayProperty(so);
                if (featuresArr == null || index < 0 || index >= featuresArr.arraySize) return false;

                featuresArr.DeleteArrayElementAtIndex(index);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(vrcFury);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception e) { Debug.LogError($"[Gri Tools] DeleteToggle: {e}"); return false; }
        }

        public static List<ToggleData> GetExistingToggles(Component vrcFury)
        {
            var result = new List<ToggleData>();
            if (vrcFury == null) return result;
            try
            {
                var so = new SerializedObject(vrcFury);
                so.Update();
                var featuresArr = FindFeaturesArrayProperty(so);
                if (featuresArr == null) return result;

                for (int i = 0; i < featuresArr.arraySize; i++)
                {
                    var elem     = featuresArr.GetArrayElementAtIndex(i);
                    var typeName = elem.managedReferenceFullTypename ?? "";

                    if (typeName.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string ReadStr(string[] names)
                    {
                        foreach (var n in names)
                        {
                            var p = elem.FindPropertyRelative(n);
                            if (p != null && p.propertyType == SerializedPropertyType.String)
                                return p.stringValue;
                        }
                        return "";
                    }
                    bool ReadBool(string[] names)
                    {
                        foreach (var n in names)
                        {
                            var p = elem.FindPropertyRelative(n);
                            if (p != null && p.propertyType == SerializedPropertyType.Boolean)
                                return p.boolValue;
                        }
                        return false;
                    }
                    Texture2D ReadTex(string[] names)
                    {
                        foreach (var n in names)
                        {
                            var p = elem.FindPropertyRelative(n);
                            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
                                return p.objectReferenceValue as Texture2D;
                        }
                        return null;
                    }

                    var entries = new List<ObjectToggleEntry>();
                    SerializedProperty objsArr = null;
                    foreach (var n in ObjectsFieldNames)
                    {
                        objsArr = elem.FindPropertyRelative(n);
                        if (objsArr != null && objsArr.isArray) break;
                    }
                    if (objsArr != null)
                    {
                        for (int j = 0; j < objsArr.arraySize; j++)
                        {
                            var oe = objsArr.GetArrayElementAtIndex(j);
                            GameObject go = null;
                            foreach (var n in ObjFieldNames)
                            {
                                var p = oe.FindPropertyRelative(n);
                                if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
                                { go = p.objectReferenceValue as GameObject; break; }
                            }
                            if (go == null) continue;

                            var mode = ObjectToggleMode.TurnOn;
                            foreach (var n in ObjModeFieldNames)
                            {
                                var p = oe.FindPropertyRelative(n);
                                if (p != null && p.propertyType == SerializedPropertyType.Enum)
                                { mode = p.enumValueIndex == 1 ? ObjectToggleMode.TurnOff : ObjectToggleMode.TurnOn; break; }
                            }
                            entries.Add(new ObjectToggleEntry(go, mode));
                        }
                    }

                    string rawName  = ReadStr(NameFieldNames);
                    string readName = rawName;
                    string readMenu = "";
                    if (!string.IsNullOrEmpty(rawName))
                    {
                        int slash = rawName.LastIndexOf('/');
                        if (slash >= 0) { readMenu = rawName.Substring(0, slash); readName = rawName.Substring(slash + 1); }
                    }

                    result.Add(new ToggleData
                    {
                        Index         = i,
                        Name          = readName,
                        MenuPath      = readMenu,
                        ParamName     = ReadStr(ParamNameFieldNames),
                        DefaultOn     = ReadBool(DefaultOnFieldNames),
                        Inverted      = ReadBool(InvertFieldNames),
                        SavedParam    = ReadBool(SavedParamFieldNames),
                        UseInt        = ReadBool(UseIntFieldNames),
                        SliderMode    = ReadBool(SliderFieldNames),
                        Icon          = ReadTex(IconFieldNames),
                        ObjectEntries = entries
                    });
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Gri Tools] GetExistingToggles: {e.Message}"); }
            return result;
        }

        public static Component GetVRCFury(GameObject avatar)
        {
            if (_vrcFuryType == null || avatar == null) return null;
            return avatar.GetComponent(_vrcFuryType);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // PRIVATE — HELPERS
        // ═════════════════════════════════════════════════════════════════════════

        private static bool CanWrite(Component vrcFury)
        {
            if (!IsAvailable || vrcFury == null) return false;
            if (_fi_config == null || _fi_features == null || _toggleFeatureType == null || _fi_toggle_name == null)
            {
                Debug.LogError("[Gri Tools] Bridge not fully initialized. Re-open the VRCFury Menu Builder window.");
                return false;
            }
            return true;
        }

        private static System.Collections.IList GetFeaturesList(Component vrcFury)
        {
            object config = _fi_config.GetValue(vrcFury);
            if (config == null) return null;
            return _fi_features.GetValue(config) as System.Collections.IList;
        }

        private static System.Collections.IList EnsureFeaturesList(Component vrcFury)
        {
            object config = _fi_config.GetValue(vrcFury);
            if (config == null)
            {
                InitializeConfigIfNull(vrcFury);
                config = _fi_config.GetValue(vrcFury);
            }
            if (config == null) { Debug.LogError("[Gri Tools] config is null."); return null; }

            var features = _fi_features.GetValue(config) as System.Collections.IList;
            if (features == null)
            {
                var baseType = _toggleFeatureType.BaseType ?? _toggleFeatureType;
                var listType = typeof(List<>).MakeGenericType(baseType);
                try   { _fi_features.SetValue(config, Activator.CreateInstance(listType)); }
                catch { _fi_features.SetValue(config, new List<object>()); }
                features = _fi_features.GetValue(config) as System.Collections.IList;
            }
            return features;
        }

        private static void WriteFieldsDirect(object toggle, ToggleData data)
        {
            if (toggle == null) return;

            var type = toggle.GetType();
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            void SetStr(string[] candidates, string value)
            {
                foreach (var n in candidates)
                {
                    var f = type.GetField(n, BF);
                    if (f != null && f.FieldType == typeof(string))
                    { f.SetValue(toggle, value); return; }
                }
                foreach (var f in type.GetFields(BF))
                {
                    if (f.FieldType != typeof(string)) continue;
                    foreach (var n in candidates)
                        if (f.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                        { f.SetValue(toggle, value); return; }
                }
            }

            void SetBool(string[] candidates, bool value)
            {
                foreach (var n in candidates)
                {
                    var f = type.GetField(n, BF);
                    if (f != null && f.FieldType == typeof(bool))
                    { f.SetValue(toggle, value); return; }
                }
            }

            void SetObj(string[] candidates, UnityEngine.Object value)
            {
                foreach (var n in candidates)
                {
                    var f = type.GetField(n, BF);
                    if (f != null && (f.FieldType == typeof(Texture2D) || typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)))
                    { f.SetValue(toggle, value); return; }
                }
            }

            string menuPath = string.IsNullOrEmpty(data.MenuPath) ? data.Name : $"{data.MenuPath}/{data.Name}";

            SetStr(NameFieldNames,      menuPath);
            SetStr(ParamNameFieldNames, data.ParamName ?? "");
            SetBool(DefaultOnFieldNames,  data.DefaultOn);
            SetBool(InvertFieldNames,     data.Inverted);
            SetBool(SavedParamFieldNames, data.SavedParam);
            SetBool(UseIntFieldNames,     data.UseInt);
            SetBool(SliderFieldNames,     data.SliderMode);
            SetObj(IconFieldNames, data.Icon);

            if (data.ObjectEntries == null || data.ObjectEntries.Count == 0) return;

            FieldInfo objsField = null;
            foreach (var n in ObjectsFieldNames)
            {
                var f = type.GetField(n, BF);
                if (f != null && typeof(System.Collections.IList).IsAssignableFrom(f.FieldType))
                { objsField = f; break; }
            }
            if (objsField == null)
                objsField = type.GetFields(BF)
                    .FirstOrDefault(f => typeof(System.Collections.IList).IsAssignableFrom(f.FieldType));

            if (objsField == null) { Debug.LogWarning("[Gri Tools] Could not find objects field on Toggle type."); return; }

            Type elemType = _objectToggleType;
            if (elemType == null)
            {
                var listGenericArgs = objsField.FieldType.GetGenericArguments();
                elemType = listGenericArgs.Length > 0 ? listGenericArgs[0] : null;
            }
            if (elemType == null) { Debug.LogWarning("[Gri Tools] Could not determine ObjectToggle element type."); return; }

            var listType = typeof(List<>).MakeGenericType(elemType);
            var objList  = (System.Collections.IList)Activator.CreateInstance(listType);

            foreach (var entry in data.ObjectEntries)
            {
                if (entry?.Object == null) continue;
                var ot     = Activator.CreateInstance(elemType);
                var otType = ot.GetType();

                foreach (var n in ObjFieldNames)
                {
                    var f = otType.GetField(n, BF);
                    if (f != null && typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                    { f.SetValue(ot, entry.Object); break; }
                }
                var objField = otType.GetFields(BF)
                    .FirstOrDefault(f => f.FieldType == typeof(GameObject)
                                      || f.FieldType == typeof(Transform));
                objField?.SetValue(ot, entry.Object);

                foreach (var n in ObjModeFieldNames)
                {
                    var f = otType.GetField(n, BF);
                    if (f != null && f.FieldType.IsEnum)
                    {
                        try { f.SetValue(ot, Enum.ToObject(f.FieldType, (int)entry.Mode)); }
                        catch { }
                        break;
                    }
                }

                objList.Add(ot);
            }

            objsField.SetValue(toggle, objList);
        }

        private static SerializedProperty FindFeaturesArrayProperty(SerializedObject so)
        {
            foreach (var cfgName in ConfigFieldNames)
            {
                foreach (var featName in FeaturesFieldNames)
                {
                    var p = so.FindProperty($"{cfgName}.{featName}");
                    if (p != null && p.isArray) return p;
                }
            }

            var iter = so.GetIterator();
            while (iter.NextVisible(true))
            {
                if (!iter.isArray) continue;
                if (iter.arraySize == 0) continue;
                var firstElem = iter.GetArrayElementAtIndex(0);
                if (firstElem.propertyType != SerializedPropertyType.ManagedReference) continue;
                var typeName = firstElem.managedReferenceFullTypename ?? "";
                if (typeName.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Feature", StringComparison.OrdinalIgnoreCase) >= 0)
                    return iter.Copy();
            }

            var iter2 = so.GetIterator();
            while (iter2.NextVisible(true))
            {
                if (!iter2.isArray) continue;
                if (iter2.arraySize == 0)
                {
                    if (FeaturesFieldNames.Any(n =>
                        iter2.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                        return iter2.Copy();
                }
            }

            return null;
        }

        private static void WriteToggleProperties(SerializedProperty elem, ToggleData data)
        {
            void SetString(string[] names, string value)
            {
                foreach (var n in names)
                {
                    var p = elem.FindPropertyRelative(n);
                    if (p != null && p.propertyType == SerializedPropertyType.String)
                    { p.stringValue = value; return; }
                }
            }

            void SetBool(string[] names, bool value)
            {
                foreach (var n in names)
                {
                    var p = elem.FindPropertyRelative(n);
                    if (p != null && p.propertyType == SerializedPropertyType.Boolean)
                    { p.boolValue = value; return; }
                }
            }

            void SetObjRef(string[] names, UnityEngine.Object value)
            {
                foreach (var n in names)
                {
                    var p = elem.FindPropertyRelative(n);
                    if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
                    { p.objectReferenceValue = value; return; }
                }
            }

            string menuPathFull = string.IsNullOrEmpty(data.MenuPath) ? data.Name : $"{data.MenuPath}/{data.Name}";
            SetString(MenuPathFieldNames,  menuPathFull);
            SetString(NameFieldNames,      data.Name);
            SetString(ParamNameFieldNames, data.ParamName ?? "");
            SetBool(DefaultOnFieldNames,   data.DefaultOn);
            SetBool(InvertFieldNames,      data.Inverted);
            SetBool(SavedParamFieldNames,  data.SavedParam);
            SetBool(UseIntFieldNames,      data.UseInt);
            SetBool(SliderFieldNames,      data.SliderMode);
            SetObjRef(IconFieldNames,      data.Icon);

            if (data.ObjectEntries == null || data.ObjectEntries.Count == 0) return;

            SerializedProperty objsArr = null;
            foreach (var n in ObjectsFieldNames)
            {
                objsArr = elem.FindPropertyRelative(n);
                if (objsArr != null && objsArr.isArray) break;
            }
            if (objsArr == null) return;

            objsArr.ClearArray();

            for (int i = 0; i < data.ObjectEntries.Count; i++)
            {
                var entry = data.ObjectEntries[i];
                if (entry?.Object == null) continue;

                objsArr.InsertArrayElementAtIndex(objsArr.arraySize);
                var objElem = objsArr.GetArrayElementAtIndex(objsArr.arraySize - 1);

                if (objElem.propertyType == SerializedPropertyType.ManagedReference)
                {
                    if (_objectToggleType != null)
                        objElem.managedReferenceValue = Activator.CreateInstance(_objectToggleType);
                }

                foreach (var n in ObjFieldNames)
                {
                    var p = objElem.FindPropertyRelative(n);
                    if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
                    { p.objectReferenceValue = entry.Object; break; }
                }

                foreach (var n in ObjModeFieldNames)
                {
                    var p = objElem.FindPropertyRelative(n);
                    if (p != null && p.propertyType == SerializedPropertyType.Enum)
                    { p.enumValueIndex = (int)entry.Mode; break; }
                }
            }
        }

        private static void AddFixWriteDefaults(Component comp)
        {
            if (_fixWriteDefaultsType == null)
            {
                Debug.LogWarning("[Gri Tools] FixWriteDefaults type not found — skipping.");
                return;
            }

            var features = EnsureFeaturesList(comp);
            if (features == null) return;

            foreach (var f in features)
                if (f != null && _fixWriteDefaultsType.IsInstanceOfType(f)) return;

            try
            {
                object fwd = Activator.CreateInstance(_fixWriteDefaultsType);

                if (_fi_fwd_fixMode != null)
                {
                    var enumType = _fi_fwd_fixMode.FieldType;
                    if (enumType.IsEnum)
                    {
                        try   { _fi_fwd_fixMode.SetValue(fwd, Enum.Parse(enumType, "Auto", ignoreCase: true)); }
                        catch { _fi_fwd_fixMode.SetValue(fwd, Enum.ToObject(enumType, 0)); }
                    }
                }

                Undo.RecordObject(comp, "Gri Tools: Add FixWriteDefaults");
                features.Add(fwd);
                CommitComponent(comp);
            }
            catch (Exception e) { Debug.LogError($"[Gri Tools] AddFixWriteDefaults: {e}"); }
        }

        private static void CommitComponent(Component comp)
        {
            EditorUtility.SetDirty(comp);
            var so = new SerializedObject(comp);
            so.Update();
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        private static void InitializeConfigIfNull(Component comp)
        {
            if (comp == null || _vrcFuryConfigType == null || _fi_config == null) return;
            if (_fi_config.GetValue(comp) != null) return;
            try
            {
                var so = new SerializedObject(comp);
                so.Update();
                bool found = false;
                foreach (var name in ConfigFieldNames)
                {
                    var prop = so.FindProperty(name);
                    if (prop == null || prop.propertyType != SerializedPropertyType.ManagedReference) continue;
                    prop.managedReferenceValue = Activator.CreateInstance(_vrcFuryConfigType);
                    found = true; break;
                }
                if (!found)
                {
                    var iter = so.GetIterator();
                    while (iter.NextVisible(true))
                    {
                        if (iter.propertyType != SerializedPropertyType.ManagedReference) continue;
                        if (!ConfigFieldNames.Any(n => string.Equals(iter.name, n, StringComparison.OrdinalIgnoreCase))) continue;
                        iter.managedReferenceValue = Activator.CreateInstance(_vrcFuryConfigType);
                        found = true; break;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                if (!found)
                    Debug.LogWarning("[Gri Tools] Could not initialize config. Try updating VRCFury via VCC.");
            }
            catch (Exception e) { Debug.LogWarning($"[Gri Tools] InitializeConfigIfNull: {e.Message}"); }
        }

        private static Type TryFindType(string[] candidates, Type[] allTypes)
        {
            foreach (var name in candidates)
            {
                var t = allTypes.FirstOrDefault(x => x.FullName == name);
                if (t != null) return t;
                var alt = allTypes.FirstOrDefault(x => x.FullName == name.Replace('.', '+'));
                if (alt != null) return alt;
            }
            return null;
        }

        private static FieldInfo TryFindField(Type type, string[] candidates, BindingFlags bf)
        {
            foreach (var name in candidates)
            {
                var fi = type.GetField(name, bf);
                if (fi != null) return fi;
            }
            return null;
        }

        private static bool InheritsFrom(Type t, string baseName)
        {
            var cur = t.BaseType;
            while (cur != null) { if (cur.Name == baseName) return true; cur = cur.BaseType; }
            return false;
        }
    }

    // ── Shared utilities ─────────────────────────────────────────────────────────
    public static class MenuBuilderUtils
    {
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }
            return sb.ToString();
        }

        public static string GetParentPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            int i = fullPath.LastIndexOf('/');
            return i < 0 ? "" : fullPath.Substring(0, i);
        }
    }
}
