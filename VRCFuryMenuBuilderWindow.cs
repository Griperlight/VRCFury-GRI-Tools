using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRCFuryMenuBuilder
{
    /// <summary>
    /// Gri Tools — VRCFury Menu Builder
    /// Access via: Gri Tools > VRCFury Menu Builder
    ///
    /// Architecture:
    ///   Avatar Root     → VRCFury with FixWriteDefaults (Auto)
    ///   Root/Menus      → VRCFury with all Toggle features
    /// </summary>
    public class VRCFuryMenuBuilderWindow : EditorWindow
    {
        private const string WINDOW_TITLE = "VRCFury Menu Builder";
        private const string TOOL_VERSION = "v1.2";
        private static readonly string[] TAB_LABELS = { "  Toggles  ", "  Submenus  ", "  Parameters  " };

        // ── Persistent state ─────────────────────────────────────────────────────
        [SerializeField] private GameObject       _selectedAvatar;
        [SerializeField] private int              _selectedTab;
        [SerializeField] private bool             _toggleFormExpanded   = true;
        [SerializeField] private bool             _existingListExpanded = true;
        [SerializeField] private bool             _paramListExpanded    = true;
        [SerializeField] private ToggleData       _toggleDraft          = new ToggleData { IsNew = true };
        [SerializeField] private string           _newSubmenuName       = "";
        [SerializeField] private string           _newSubmenuParent     = "";
        [SerializeField] private List<SubmenuData> _submenus            = new List<SubmenuData>();

        // ── Runtime state ────────────────────────────────────────────────────────
        private Component          _menusVRCFury;      // VRCFury on "Menus" child
        private bool               _rootIsSetUp;       // avatar root has FixWriteDefaults
        private VRCFuryBridge.AvatarStatus _avatarStatus = new VRCFuryBridge.AvatarStatus();
        private List<ToggleData>   _existingToggles    = new List<ToggleData>();
        private List<string>       _params             = new List<string>();
        private Vector2            _scrollPos;
        private MenuBuilderStyles  _styles;
        private bool               _stylesReady;

        // ── Menu item ────────────────────────────────────────────────────────────
        [MenuItem("Gri Tools/VRCFury Menu Builder", priority = 200)]
        public static void ShowWindow()
        {
            var w = GetWindow<VRCFuryMenuBuilderWindow>(WINDOW_TITLE);
            w.minSize = new Vector2(440, 580);
            w.Show();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            _stylesReady = false;
            if (!VRCFuryBridge.IsAvailable) VRCFuryBridge.Initialize();

            // Refresh datos del avatar ya persistido (si lo hay)
            // ANTES de intentar auto-seleccionar, para no pisar lo serializado
            RefreshFromAvatar();

            // Solo auto-selecciona si el campo está vacío
            TryAutoSelectFromHierarchy();
        }

        // ── Auto-select ──────────────────────────────────────────────────────────
        // OnSelectionChange eliminado intencionalmente.
        // Ya no reaccionamos al click en la jerarquía para no pisar el avatar fijado.

        /// <summary>
        /// Solo rellena el campo avatar si está vacío.
        /// Una vez fijado (por serialización o por el usuario) no lo tocamos.
        /// </summary>
        private void TryAutoSelectFromHierarchy()
        {
            // GUARD: si ya hay un avatar seleccionado, no hacer nada
            if (_selectedAvatar != null) return;

            if (Selection.activeGameObject == null) return;
            var descriptor = Selection.activeGameObject
                .GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
            if (descriptor == null) return;

            _selectedAvatar = descriptor.gameObject;
            RefreshFromAvatar();
            Repaint();
        }

        private void RefreshFromAvatar()
        {
            if (_selectedAvatar == null)
            {
                _menusVRCFury = null;
                _rootIsSetUp  = false;
                _avatarStatus = new VRCFuryBridge.AvatarStatus();
                _existingToggles.Clear();
                _submenus.Clear();
                _params.Clear();
                return;
            }

            // Single authoritative status check
            _avatarStatus = VRCFuryBridge.GetAvatarStatus(_selectedAvatar);
            _rootIsSetUp  = _avatarStatus.HasRootVRCFury && _avatarStatus.HasFixWriteDefaults;
            _menusVRCFury = _avatarStatus.HasMenusVRCFury
                ? VRCFuryBridge.GetMenusVRCFury(_selectedAvatar)
                : null;

            // Limpiar siempre antes de rellenar para no mezclar datos de avatares distintos
            _existingToggles.Clear();

            if (_menusVRCFury != null)
                _existingToggles = VRCFuryBridge.GetExistingToggles(_menusVRCFury);

            // Si había un toggle en edición y cambiamos de avatar, cancelar el draft
            if (!_toggleDraft.IsNew)
                _toggleDraft = new ToggleData { IsNew = true };

            RebuildSubmenusFromToggles();
            RebuildParams();
        }

        private void RebuildSubmenusFromToggles()
        {
            // Partimos de cero para que los submenús reflejen exactamente el
            // avatar actual. Datos de otro avatar se descartan.
            _submenus.Clear();

            var implied = new HashSet<string>();
            foreach (var t in _existingToggles)
            {
                if (string.IsNullOrEmpty(t.MenuPath)) continue;

                // Un path tipo "Ropa/Casual" implica "Ropa" y "Ropa/Casual"
                var parts       = t.MenuPath.Split('/');
                var accumulated = "";
                foreach (var part in parts)
                {
                    accumulated = string.IsNullOrEmpty(accumulated) ? part : $"{accumulated}/{part}";
                    if (implied.Contains(accumulated)) continue;

                    implied.Add(accumulated);
                    _submenus.Add(new SubmenuData
                    {
                        Name       = part,
                        ParentPath = MenuBuilderUtils.GetParentPath(accumulated),
                        IsExpanded = true
                    });
                }
            }

            _submenus = _submenus.OrderBy(s => s.FullPath).ToList();
        }

        private void RebuildParams()
        {
            _params = _existingToggles
                .Select(t => string.IsNullOrEmpty(t.ParamName)
                    ? $"Toggle_{MenuBuilderUtils.SanitizeName(t.Name)}"
                    : t.ParamName)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct().OrderBy(p => p).ToList();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // OnGUI
        // ═════════════════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            DrawThinSeparator();
            DrawAvatarSection();

            if (_selectedAvatar == null)
            {
                EditorGUILayout.Space(12);
                DrawHint("Select an avatar in the Hierarchy or use the picker above.");
                return;
            }

            if (!VRCFuryBridge.IsAvailable)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "VRCFury not detected.\nInstall VRCFury via VCC and re-open this window.",
                    MessageType.Error);
                if (GUILayout.Button("↺  Retry Detection")) VRCFuryBridge.Initialize();
                return;
            }

            DrawSetupSection();
            DrawThinSeparator();

            if (_menusVRCFury == null)
            {
                EditorGUILayout.Space(8);
                DrawHint("Click \"+ Add VRCFury Component\" above to get started.");
                return;
            }

            _selectedTab = GUILayout.Toolbar(_selectedTab, TAB_LABELS, _styles.Tab, GUILayout.Height(26));
            EditorGUILayout.Space(6);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Space(2);
            switch (_selectedTab)
            {
                case 0: DrawTogglesTab();    break;
                case 1: DrawSubmenusTab();   break;
                case 2: DrawParametersTab(); break;
            }
            GUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // HEADER
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                EditorGUILayout.LabelField("⚡  VRCFury Menu Builder", _styles.Header, GUILayout.Height(26));
                GUILayout.FlexibleSpace();
                GUI.color = MenuBuilderStyles.ColMuted;
                EditorGUILayout.LabelField(TOOL_VERSION, EditorStyles.miniLabel, GUILayout.Width(36));
                GUI.color = Color.white;
                GUILayout.Space(6);
            }
            EditorGUILayout.Space(4);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // AVATAR PICKER
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawAvatarSection()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                EditorGUILayout.LabelField("Avatar", _styles.SectionLabel, GUILayout.Width(52));
                EditorGUI.BeginChangeCheck();
                var newAvatar = (GameObject)EditorGUILayout.ObjectField(_selectedAvatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) { _selectedAvatar = newAvatar; RefreshFromAvatar(); }
                GUILayout.Space(6);
            }
            if (_selectedAvatar != null &&
                _selectedAvatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() == null)
            {
                EditorGUILayout.HelpBox("No VRCAvatarDescriptor found on this GameObject.", MessageType.Warning);
                _selectedAvatar = null;
            }
            EditorGUILayout.Space(4);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // STATUS BAR
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawSetupSection()
        {
            EditorGUILayout.Space(2);

            bool fullyReady = _avatarStatus.HasRootVRCFury
                           && _avatarStatus.HasFixWriteDefaults
                           && _avatarStatus.HasMenusObject
                           && _avatarStatus.HasMenusVRCFury;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);

                if (!fullyReady)
                {
                    // ── Show button only ───────────────────────────────────────
                    GUI.backgroundColor = MenuBuilderStyles.ColWarning;
                    if (GUILayout.Button("+ Add VRCFury Component", GUILayout.Height(22), GUILayout.Width(190)))
                    {
                        VRCFuryBridge.SetupAvatarRoot(_selectedAvatar);
                        _menusVRCFury = VRCFuryBridge.GetOrCreateMenusVRCFury(_selectedAvatar);
                        RefreshFromAvatar();
                        ShowNotification(new GUIContent("✓  Avatar ready!"));
                    }
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    // ── Ready: compact green line ──────────────────────────────
                    GUI.color = MenuBuilderStyles.ColSuccess;
                    EditorGUILayout.LabelField("✓  VRCFury", _styles.SubHeader, GUILayout.Width(90));
                    GUI.color = Color.white;

                    GUI.color = MenuBuilderStyles.ColMuted;
                    EditorGUILayout.LabelField(
                        $"{_selectedAvatar.name}/Menus  •  {_existingToggles.Count} toggles",
                        EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    GUI.color = Color.white;
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("↺", GUILayout.Width(24), GUILayout.Height(20)))
                    RefreshFromAvatar();

                // Debug button — shows exactly what was detected
                GUI.color = MenuBuilderStyles.ColMuted;
                if (GUILayout.Button("?", GUILayout.Width(20), GUILayout.Height(20)))
                    EditorUtility.DisplayDialog("Avatar Status Debug", _avatarStatus.DebugLog, "OK");
                GUI.color = Color.white;

                GUILayout.Space(6);
            }
            EditorGUILayout.Space(4);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // TAB: TOGGLES
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawTogglesTab()
        {
            bool isEditing = !_toggleDraft.IsNew;
            string formTitle = isEditing
                ? $"Edit Toggle  —  {_toggleDraft.Name}"
                : "New Toggle";

            _toggleFormExpanded = DrawFoldout(formTitle, _toggleFormExpanded);
            if (_toggleFormExpanded)
            {
                DrawCard(() =>
                {
                    // Name ─────────────────────────────────────────────────────
                    DrawRow("Name", () =>
                        _toggleDraft.Name = EditorGUILayout.TextField(_toggleDraft.Name));
                    EditorGUILayout.Space(3);

                    // Submenu ──────────────────────────────────────────────────
                    DrawRow("Submenu", () =>
                    {
                        var opts = GetSubmenuOptions();
                        int cur  = string.IsNullOrEmpty(_toggleDraft.MenuPath) ? 0
                            : System.Array.IndexOf(opts, _toggleDraft.MenuPath);
                        if (cur < 0) cur = 0;
                        int sel = EditorGUILayout.Popup(cur, opts);
                        _toggleDraft.MenuPath = sel == 0 ? "" : opts[sel];
                    });
                    EditorGUILayout.Space(3);

                    // Icon + flags ─────────────────────────────────────────────
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Icon", GUILayout.Width(54));
                        _toggleDraft.Icon = (Texture2D)EditorGUILayout.ObjectField(
                            _toggleDraft.Icon, typeof(Texture2D), false,
                            GUILayout.Width(42), GUILayout.Height(42));
                        GUILayout.Space(10);
                        using (new EditorGUILayout.VerticalScope())
                        {
                            _toggleDraft.DefaultOn = EditorGUILayout.ToggleLeft(
                                new GUIContent("Default ON", "Object active when avatar loads."),
                                _toggleDraft.DefaultOn);
                            _toggleDraft.Inverted = EditorGUILayout.ToggleLeft(
                                new GUIContent("Inverted", "Object is ON when param is OFF."),
                                _toggleDraft.Inverted);
                        }
                    }
                    EditorGUILayout.Space(6);

                    // Objects list ─────────────────────────────────────────────
                    EditorGUILayout.LabelField("OBJECTS TO TOGGLE", _styles.SectionLabel);
                    EditorGUILayout.Space(2);

                    for (int i = 0; i < _toggleDraft.ObjectEntries.Count; i++)
                    {
                        var entry = _toggleDraft.ObjectEntries[i];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            // Object picker
                            entry.Object = (GameObject)EditorGUILayout.ObjectField(
                                entry.Object, typeof(GameObject), true, GUILayout.MinWidth(120));

                            // Mode dropdown: TurnOn / TurnOff
                            entry.Mode = (ObjectToggleMode)EditorGUILayout.EnumPopup(
                                entry.Mode, GUILayout.Width(76));

                            // Remove
                            GUI.backgroundColor = new Color(0.55f, 0.12f, 0.12f);
                            if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(18)))
                            {
                                _toggleDraft.ObjectEntries.RemoveAt(i);
                                GUI.backgroundColor = Color.white;
                                GUIUtility.ExitGUI();
                            }
                            GUI.backgroundColor = Color.white;
                        }
                    }
                    if (GUILayout.Button("+ Add Object Slot", GUILayout.Height(22)))
                        _toggleDraft.ObjectEntries.Add(new ObjectToggleEntry());

                    EditorGUILayout.Space(8);

                    // Toggle flags ─────────────────────────────────────────────
                    EditorGUILayout.LabelField("OPTIONS", _styles.SectionLabel);
                    EditorGUILayout.Space(2);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            _toggleDraft.DefaultOn = EditorGUILayout.ToggleLeft(
                                new GUIContent("Default ON",
                                    "Parameter starts as ON when avatar loads."),
                                _toggleDraft.DefaultOn);

                            _toggleDraft.Inverted = EditorGUILayout.ToggleLeft(
                                new GUIContent("Inverted (invertRestState)",
                                    "Objects are ON when parameter is OFF and vice versa."),
                                _toggleDraft.Inverted);

                            _toggleDraft.SavedParam = EditorGUILayout.ToggleLeft(
                                new GUIContent("Saved Parameter",
                                    "Value persists across world changes."),
                                _toggleDraft.SavedParam);
                        }
                        using (new EditorGUILayout.VerticalScope())
                        {
                            _toggleDraft.UseInt = EditorGUILayout.ToggleLeft(
                                new GUIContent("Use Int Param",
                                    "Uses an int instead of bool — allows multi-state toggles."),
                                _toggleDraft.UseInt);

                            _toggleDraft.SliderMode = EditorGUILayout.ToggleLeft(
                                new GUIContent("Slider / Radial",
                                    "Adds a radial puppet / slider to the menu instead of a toggle."),
                                _toggleDraft.SliderMode);
                        }
                    }

                    EditorGUILayout.Space(6);

                    // Param name ───────────────────────────────────────────────
                    DrawRow("Param", () =>
                    {
                        _toggleDraft.ParamName = EditorGUILayout.TextField(_toggleDraft.ParamName);
                        if (string.IsNullOrEmpty(_toggleDraft.ParamName) && !string.IsNullOrEmpty(_toggleDraft.Name))
                        {
                            GUI.color = MenuBuilderStyles.ColMuted;
                            EditorGUILayout.LabelField(
                                $"→ Toggle_{MenuBuilderUtils.SanitizeName(_toggleDraft.Name)}",
                                _styles.Hint, GUILayout.Width(180));
                            GUI.color = Color.white;
                        }
                    });
                    EditorGUILayout.Space(4);

                    // Validation ───────────────────────────────────────────────
                    var (canSave, warning) = ValidateToggleDraft();
                    if (!string.IsNullOrEmpty(warning)) DrawHint("⚠  " + warning);
                    EditorGUILayout.Space(4);

                    // Action buttons ───────────────────────────────────────────
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = canSave;
                        if (isEditing)
                        {
                            // Save edits
                            if (GUILayout.Button("💾  Save Changes", _styles.BtnCreate, GUILayout.Height(28)))
                                ExecuteSaveToggle();
                            GUI.enabled = true;

                            // Cancel edit
                            GUI.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                            if (GUILayout.Button("Cancel", GUILayout.Height(28), GUILayout.Width(70)))
                                CancelEdit();
                            GUI.backgroundColor = Color.white;
                        }
                        else
                        {
                            if (GUILayout.Button("✚  Create Toggle", _styles.BtnCreate, GUILayout.Height(28)))
                                ExecuteCreateToggle();
                            GUI.enabled = true;
                        }
                    }
                    GUI.enabled = true;
                });
            }

            EditorGUILayout.Space(10);

            // ── Existing Toggles ───────────────────────────────────────────────
            _existingListExpanded = DrawFoldout(
                $"Existing Toggles  ({_existingToggles.Count})", _existingListExpanded);

            if (_existingListExpanded)
            {
                if (_existingToggles.Count == 0)
                    DrawHint("No toggles yet — create one above.");
                else
                    foreach (var t in _existingToggles)
                        DrawExistingToggleRow(t);
            }
        }

        private void DrawExistingToggleRow(ToggleData t)
        {
            bool isCurrentlyEditing = !_toggleDraft.IsNew && _toggleDraft.Index == t.Index;

            // Highlight the card if currently being edited
            if (isCurrentlyEditing)
                GUI.backgroundColor = new Color(0.20f, 0.30f, 0.50f);

            DrawCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (t.Icon != null)
                    {
                        GUILayout.Label(t.Icon, GUILayout.Width(24), GUILayout.Height(24));
                        GUILayout.Space(4);
                    }

                    var displayPath = string.IsNullOrEmpty(t.MenuPath)
                        ? t.Name : $"{t.MenuPath}  /  {t.Name}";
                    EditorGUILayout.LabelField(displayPath, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    // ── Edit button ─────────────────────────────────────────
                    GUI.backgroundColor = new Color(0.20f, 0.35f, 0.55f);
                    if (GUILayout.Button("✎", GUILayout.Width(26), GUILayout.Height(20)))
                        BeginEditToggle(t);
                    GUI.backgroundColor = Color.white;

                    // ── Delete button ───────────────────────────────────────
                    GUI.backgroundColor = new Color(0.55f, 0.12f, 0.12f);
                    if (GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(20)))
                    {
                        if (EditorUtility.DisplayDialog(
                            "Delete Toggle",
                            $"Delete toggle \"{t.Name}\"?",
                            "Delete", "Cancel"))
                        {
                            VRCFuryBridge.DeleteToggle(_menusVRCFury, t.Index);
                            if (!_toggleDraft.IsNew && _toggleDraft.Index == t.Index)
                                CancelEdit();
                            RefreshFromAvatar();
                            GUI.backgroundColor = Color.white;
                            GUIUtility.ExitGUI();
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }

                // Param + badges row
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(2);
                    var paramName = !string.IsNullOrEmpty(t.ParamName)
                        ? t.ParamName
                        : $"Toggle_{MenuBuilderUtils.SanitizeName(t.Name)}";
                    GUI.color = MenuBuilderStyles.ColAccent;
                    EditorGUILayout.LabelField(paramName, _styles.Tag,
                        GUILayout.Width(Mathf.Min(paramName.Length * 7 + 12, 220)));
                    GUI.color = Color.white;

                    if (t.ObjectEntries.Count > 0) DrawBadge($"{t.ObjectEntries.Count} obj", new Color(0.18f,0.30f,0.40f));
                    if (t.DefaultOn)  DrawBadge("DEFAULT ON", new Color(0.25f, 0.50f, 0.30f));
                    if (t.Inverted)   DrawBadge("INVERTED",   new Color(0.45f, 0.30f, 0.55f));
                    if (t.SavedParam) DrawBadge("SAVED",      new Color(0.40f, 0.30f, 0.15f));
                    if (t.SliderMode) DrawBadge("SLIDER",     new Color(0.25f, 0.25f, 0.55f));
                    if (isCurrentlyEditing) DrawBadge("EDITING", new Color(0.20f, 0.35f, 0.55f));
                }
            });

            GUI.backgroundColor = Color.white;
        }

        private void BeginEditToggle(ToggleData t)
        {
            _toggleDraft           = t.Clone();
            _toggleDraft.IsNew     = false;
            _toggleFormExpanded    = true;
            _scrollPos             = Vector2.zero;
            Repaint();
        }

        private void CancelEdit()
        {
            _toggleDraft = new ToggleData { IsNew = true };
            Repaint();
        }

        private void ExecuteCreateToggle()
        {
            _toggleDraft.IsNew = true;
            bool ok = VRCFuryBridge.CreateToggle(_menusVRCFury, _toggleDraft);
            if (ok) { ShowNotification(new GUIContent($"✓  '{_toggleDraft.Name}' created!")); _toggleDraft = new ToggleData { IsNew = true }; RefreshFromAvatar(); }
            else ShowNotification(new GUIContent("✕  Failed — see Console"));
        }

        private void ExecuteSaveToggle()
        {
            bool ok = VRCFuryBridge.UpdateToggle(_menusVRCFury, _toggleDraft.Index, _toggleDraft);
            if (ok) { ShowNotification(new GUIContent($"✓  '{_toggleDraft.Name}' updated!")); _toggleDraft = new ToggleData { IsNew = true }; RefreshFromAvatar(); }
            else ShowNotification(new GUIContent("✕  Failed — see Console"));
        }

        private (bool canSave, string warning) ValidateToggleDraft()
        {
            if (string.IsNullOrEmpty(_toggleDraft.Name))               return (false, "Name is required.");
            if (_toggleDraft.ObjectEntries.Count == 0)                 return (false, "Add at least one GameObject.");
            if (_toggleDraft.ObjectEntries.Any(e => e.Object == null)) return (false, "Remove empty object slots.");

            var autoParam = $"Toggle_{MenuBuilderUtils.SanitizeName(_toggleDraft.Name)}";
            var paramUsed = string.IsNullOrEmpty(_toggleDraft.ParamName) ? autoParam : _toggleDraft.ParamName;

            bool isDuplicate = _toggleDraft.IsNew && _params.Contains(paramUsed);
            if (isDuplicate) return (true, $"Param '{paramUsed}' already exists — will share it.");

            return (true, null);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // TAB: SUBMENUS
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawSubmenusTab()
        {
            DrawCard(() =>
            {
                EditorGUILayout.LabelField("New Submenu", _styles.SubHeader);
                EditorGUILayout.Space(6);
                DrawRow("Name",   () => _newSubmenuName   = EditorGUILayout.TextField(_newSubmenuName));
                EditorGUILayout.Space(3);
                DrawRow("Parent", () =>
                {
                    var opts = GetSubmenuOptions();
                    int cur  = string.IsNullOrEmpty(_newSubmenuParent) ? 0
                        : System.Array.IndexOf(opts, _newSubmenuParent);
                    if (cur < 0) cur = 0;
                    int sel = EditorGUILayout.Popup(cur, opts);
                    _newSubmenuParent = sel == 0 ? "" : opts[sel];
                });

                if (!string.IsNullOrEmpty(_newSubmenuName))
                {
                    EditorGUILayout.Space(3);
                    var preview = string.IsNullOrEmpty(_newSubmenuParent)
                        ? _newSubmenuName : $"{_newSubmenuParent}/{_newSubmenuName}";
                    GUI.color = MenuBuilderStyles.ColAccent;
                    EditorGUILayout.LabelField($"Path:  {preview}", _styles.Hint);
                    GUI.color = Color.white;
                }

                EditorGUILayout.Space(6);
                GUI.enabled = !string.IsNullOrEmpty(_newSubmenuName);
                if (GUILayout.Button("+ Add Submenu", GUILayout.Height(24)))
                {
                    var ns = new SubmenuData { Name = _newSubmenuName, ParentPath = _newSubmenuParent };
                    if (_submenus.Any(s => s.FullPath == ns.FullPath))
                        ShowNotification(new GUIContent("Already exists!"));
                    else { _submenus.Add(ns); _newSubmenuName = _newSubmenuParent = ""; ShowNotification(new GUIContent("✓  Added")); }
                }
                GUI.enabled = true;
            });

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("STRUCTURE", _styles.SectionLabel);
            EditorGUILayout.Space(4);

            if (_submenus.Count == 0)
                DrawHint("No submenus yet. Add one above or create a toggle with a submenu path.");
            else
                DrawSubmenuTree("", 0);
        }

        private void DrawSubmenuTree(string parentPath, int depth)
        {
            foreach (var sub in _submenus.Where(s => s.ParentPath == parentPath).OrderBy(s => s.Name).ToList())
            {
                bool hasChildren = _submenus.Any(s => s.ParentPath == sub.FullPath);
                int  count       = _existingToggles.Count(t => t.MenuPath == sub.FullPath);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth * 18 + 6);
                    if (hasChildren) sub.IsExpanded = EditorGUILayout.Foldout(sub.IsExpanded, "", true);
                    else GUILayout.Space(16);

                    GUI.color = MenuBuilderStyles.ColAccent;
                    GUILayout.Label("📁", GUILayout.Width(18));
                    GUI.color = Color.white;

                    EditorGUILayout.LabelField(sub.Name, GUILayout.MinWidth(80));
                    if (count > 0) DrawBadge($"{count}t", new Color(0.20f, 0.35f, 0.55f));
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("+ toggle", EditorStyles.miniButton, GUILayout.Width(68)))
                    { _selectedTab = 0; _toggleDraft.MenuPath = sub.FullPath; }

                    GUI.backgroundColor = new Color(0.5f, 0.12f, 0.12f);
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        _submenus.RemoveAll(s => s.FullPath == sub.FullPath || s.FullPath.StartsWith(sub.FullPath + "/"));
                        GUI.backgroundColor = Color.white; GUIUtility.ExitGUI(); return;
                    }
                    GUI.backgroundColor = Color.white;
                }

                if (!hasChildren || sub.IsExpanded) DrawSubmenuTree(sub.FullPath, depth + 1);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // TAB: PARAMETERS
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawParametersTab()
        {
            DrawCard(() =>
            {
                EditorGUILayout.LabelField("MEMORY ESTIMATE", _styles.SectionLabel);
                EditorGUILayout.Space(4);

                int total = _params.Count;
                float frac = total / 256f;
                var barRect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.DrawRect(barRect, new Color(0.14f, 0.14f, 0.16f));
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(frac), barRect.height),
                    frac > 0.85f ? MenuBuilderStyles.ColDanger : frac > 0.60f ? MenuBuilderStyles.ColWarning : MenuBuilderStyles.ColSuccess);
                EditorGUI.LabelField(barRect, $"  {total} / 256 bits  ({total} params)", EditorStyles.miniLabel);

                EditorGUILayout.Space(4);
                GUI.color = MenuBuilderStyles.ColMuted;
                EditorGUILayout.LabelField($"~{Mathf.CeilToInt(total / 8f)} bytes used", EditorStyles.miniLabel);
                GUI.color = Color.white;
            });

            EditorGUILayout.Space(8);
            _paramListExpanded = DrawFoldout($"Parameters  ({_params.Count})", _paramListExpanded);

            if (_paramListExpanded)
            {
                if (_params.Count == 0) DrawHint("No parameters yet.");
                else foreach (var p in _params)
                    DrawCard(() =>
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUI.color = MenuBuilderStyles.ColAccent;
                            EditorGUILayout.LabelField("bool", _styles.Tag, GUILayout.Width(32));
                            GUI.color = Color.white;
                            GUILayout.Space(6);
                            EditorGUILayout.LabelField(p, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            GUI.color = MenuBuilderStyles.ColMuted;
                            EditorGUILayout.LabelField("1 bit", EditorStyles.miniLabel, GUILayout.Width(32));
                            GUI.color = Color.white;
                        }
                    });
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // UI PRIMITIVES
        // ═════════════════════════════════════════════════════════════════════════
        private void DrawCard(System.Action content)
        {
            using (new EditorGUILayout.VerticalScope(_styles.Card)) content();
        }

        private void DrawRow(string label, System.Action control)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(54));
                control();
            }
        }

        private bool DrawFoldout(string title, bool state)
            => EditorGUILayout.Foldout(state, title, true,
                new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold });

        private void DrawThinSeparator()
        {
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), MenuBuilderStyles.ColSeparator);
            EditorGUILayout.Space(3);
        }

        private void DrawHint(string msg)
        {
            using (new EditorGUILayout.HorizontalScope())
            { GUILayout.Space(10); EditorGUILayout.LabelField(msg, _styles.Hint); }
        }

        private void DrawBadge(string label, Color bg)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = bg;
            GUILayout.Label(label, _styles.Tag);
            GUI.backgroundColor = prev;
        }

        private string[] GetSubmenuOptions()
            => new[] { "— Root —" }.Concat(_submenus.Select(s => s.FullPath)).ToArray();

        private void EnsureStyles()
        {
            if (_stylesReady && _styles != null) return;
            _styles = new MenuBuilderStyles(); _stylesReady = true;
        }
    }
}
