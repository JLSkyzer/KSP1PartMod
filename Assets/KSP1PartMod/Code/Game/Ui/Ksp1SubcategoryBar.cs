using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using I2.Loc;
using KSP.Api.CoreTypes;
using KSP.OAB;
using KSP.UI;
using KSP.UI.Binding;
using KSP1PartMod.Ui;
using Redux.Ksp1Import.Assets;
using UnityEngine;
using UnityEngine.UI;

namespace KSP1PartMod.Game.Ui
{
    /// <summary>The second vertical bar, on one <see cref="AssemblyPartsPicker"/> (VAB or BAE), right of Redux's category
    /// bar. <see cref="OabSubcategories"/> pushes the model at each refresh of the grid; <c>LateUpdate</c> shows or hides
    /// the bar (KSP1 category with two types or more, panel expanded, no search), rebuilds its buttons when the types
    /// change, and keeps it placed against <c>partsCategoriesToolbarRT</c>, whose width Redux changes (scrollbar).
    /// <para>Buttons are copies of the stock Utility button, as Redux makes its own, but they stay out of
    /// <c>categoryButtons</c> (otherwise the picker would treat them as categories), lose their
    /// <c>ObjectAssemblyCategoryButton</c> and listeners, and leave any <c>ToggleGroup</c>.</para>
    /// While the bar is shown, the grid (<c>partContentRT</c>), and the search row when it sits beside the bar, are
    /// narrowed from the left by the bar's width; they get their offsets back when it hides.</summary>
    internal sealed class Ksp1SubcategoryBar : MonoBehaviour
    {
        private sealed class Shift
        {
            public RectTransform Rt;
            public bool Active;
            public float Original, Applied;
        }

        private sealed class SubButton
        {
            public Ksp1Subcategory? Type;
            public Toggle Toggle;
            public GameObject Go;
        }

        private const string ScrollbarName = "CategoryTabScrollbar";
        private static bool _layoutLogged;

        private AssemblyPartsPicker _picker;
        private bool _hasModel;
        private PartCategories _category;
        private string _mod;
        private SubcategoryBarModel _model;
        private Ksp1Subcategory? _selected;
        private Dictionary<Ksp1Subcategory, string> _firstParts = new Dictionary<Ksp1Subcategory, string>();

        private RectTransform _root, _content;
        private ObjectAssemblyCategoryButton _template;
        private readonly List<SubButton> _buttons = new List<SubButton>();
        private readonly List<Sprite> _ownedSprites = new List<Sprite>();
        private readonly Shift _contentShift = new Shift(), _searchShift = new Shift();
        private string _builtFor;
        private bool _placedLogged, _failed;

        public void SetModel(PartCategories category, string mod, SubcategoryBarModel model, Ksp1Subcategory? selected,
            Dictionary<Ksp1Subcategory, string> firstParts)
        {
            _hasModel = model != null;
            _category = category;
            _mod = mod;
            _model = model;
            _selected = selected;
            _firstParts = firstParts ?? new Dictionary<Ksp1Subcategory, string>();
        }

        public void Clear() => _hasModel = false;

        private void Awake() => _picker = GetComponent<AssemblyPartsPicker>();

        private void LateUpdate()
        {
            if (_failed) return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                _failed = true;
                OabSubcategories.Break("the bar", e);
                SafeHide();
            }
        }

        private void Tick()
        {
            var want = OabSubcategories.Active && _hasModel && _model.Show && _picker != null
                && _picker.currentCategory == _category
                && _picker.partContentRT != null && _picker.partContentRT.gameObject.activeInHierarchy;
            if (!want)
            {
                Hide();
                return;
            }
            if (_root == null && !Create())
            {
                _failed = true;
                return;
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            var key = _mod + "|" + _model.Signature + "|" + LocalizationManager.CurrentLanguage;
            if (key != _builtFor)
            {
                Rebuild();
                _builtFor = key;
            }
            foreach (var b in _buttons)
                if (b.Toggle != null) b.Toggle.SetIsOnWithoutNotify(b.Type == _selected);
            Place();
        }

        // ---------------------------------------------------------------- creation

        private bool Create()
        {
            var log = KSP1PartModPlugin.Log;
            var toolbar = _picker.partsCategoriesToolbarRT;
            var parent = toolbar != null ? toolbar.parent as RectTransform : null;
            _template = _picker.GetCategoryButton(PartCategories.Utility);
            if (_template == null && _picker.categoryButtons != null)
                foreach (var b in _picker.categoryButtons)
                    if (b != null) { _template = b; break; }
            if (parent == null || _template == null)
            {
                log?.LogWarning($"[KSP1PartMod] ui: no bar on {_picker.name}: toolbar parent {(parent == null ? "missing" : "ok")}, " +
                    $"button template {(_template == null ? "missing" : "ok")}");
                return false;
            }
            LogLayoutOnce(parent);

            var rootObject = new GameObject("KSP1PartMod Subcategories", typeof(RectTransform)) { layer = toolbar.gameObject.layer };
            _root = (RectTransform)rootObject.transform;
            _root.SetParent(parent, false);
            _root.SetSiblingIndex(toolbar.GetSiblingIndex() + 1);
            rootObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var background = rootObject.AddComponent<Image>();
            var stockBackground = toolbar.GetComponent<Image>();
            if (stockBackground != null)
            {
                background.sprite = stockBackground.sprite;
                background.type = stockBackground.type;
                background.color = stockBackground.color;
                background.pixelsPerUnitMultiplier = stockBackground.pixelsPerUnitMultiplier;
            }
            else background.color = new Color(0f, 0f, 0f, 0f); // still a raycast target, for the scroll wheel
            background.raycastTarget = true;
            rootObject.AddComponent<RectMask2D>();

            var contentObject = new GameObject("Content", typeof(RectTransform)) { layer = toolbar.gameObject.layer };
            _content = (RectTransform)contentObject.transform;
            _content.SetParent(_root, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            var layout = contentObject.AddComponent<VerticalLayoutGroup>();
            var stockLayout = _template.transform.parent != null ? _template.transform.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            if (stockLayout != null)
            {
                layout.padding = new RectOffset(stockLayout.padding.left, stockLayout.padding.right, stockLayout.padding.top, stockLayout.padding.bottom);
                layout.spacing = stockLayout.spacing;
                layout.childAlignment = stockLayout.childAlignment;
                layout.childControlWidth = stockLayout.childControlWidth;
                layout.childControlHeight = stockLayout.childControlHeight;
                layout.childForceExpandWidth = stockLayout.childForceExpandWidth;
                layout.childForceExpandHeight = stockLayout.childForceExpandHeight;
            }
            else
            {
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = layout.childControlHeight = false;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            }
            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = rootObject.AddComponent<ScrollRect>();
            scroll.content = _content;
            scroll.viewport = _root;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            var group = _template.GetComponent<Toggle>()?.group;
            log?.LogInfo($"[KSP1PartMod] ui: sub-category bar created on {_picker.name} under '{parent.name}', next to '{toolbar.name}' " +
                $"(button template '{_template.name}', stock toggle group: {(group == null ? "none" : "'" + group.name + "'")}, " +
                $"stock layout: {(stockLayout == null ? "none" : stockLayout.GetType().Name)})");
            return true;
        }

        private void Rebuild()
        {
            foreach (var b in _buttons)
                if (b.Go != null) Destroy(b.Go);
            _buttons.Clear();
            DestroyOwnedSprites();

            var language = LocalizationManager.CurrentLanguageCode;
            var line = new StringBuilder();
            var main = _picker.GetCategoryButton(_category);
            var allLabel = Ksp1Subcategories.AllLabel(language);
            AddButton(null, allLabel, $"{allLabel}: {_mod}", main != null && main.Icon != null ? main.Icon.sprite : null,
                main != null && main.Icon != null ? main.Icon.color : Color.white);
            line.Append("All");
            foreach (var t in _model.Types)
            {
                var label = Ksp1Subcategories.Label(t.Type, language, Translate);
                var sprite = IconFor(t.Type, out var color, out var source);
                AddButton(t.Type, label, $"{label}: {t.Count} ({_mod})", sprite, color);
                line.Append(", ").Append(t.Type).Append(" (").Append(source).Append(')');
            }
            KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] ui: {_mod}: bar built with {_buttons.Count} button(s): {line}");
        }

        private void AddButton(Ksp1Subcategory? type, string label, string body, Sprite sprite, Color color)
        {
            var clone = Instantiate(_template, _content, false);
            var go = clone.gameObject;
            clone.name = "KSP1 Subcategory - " + (type.HasValue ? type.Value.ToString() : "All");
            SetLayer(clone.gameObject, _content.gameObject.layer);
            var icon = clone.Icon;
            var toggle = clone.GetComponent<Toggle>();
            SetTooltip(clone, label, body);
            // Its Awake listener calls the picker's category callback: the component goes, the listeners too.
            DestroyImmediate(clone);
            if (toggle != null)
            {
                toggle.group = null;
                toggle.onValueChanged = new Toggle.ToggleEvent();
                if (toggle is ToggleExtended extended)
                {
                    extended.ignoreClicksWhenOn = true;
                    extended.ignoreClicksWhenOff = false;
                    extended.PublishMessages = false;
                }
                toggle.SetIsOnWithoutNotify(type == _selected);
                toggle.onValueChanged.AddListener(on => { if (on) OnClicked(type); });
            }
            if (icon != null && sprite != null)
            {
                icon.sprite = sprite;
                icon.color = color;
            }
            if (!toggle) KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] ui: button template has no Toggle, sub-category buttons will not click");
            if (!go.activeSelf) go.SetActive(true);
            _buttons.Add(new SubButton { Type = type, Toggle = toggle, Go = go });
        }

        private static void SetTooltip(ObjectAssemblyCategoryButton button, string title, string body)
        {
            foreach (var t in button.GetComponentsInChildren<ObjectAssemblyBuilderTooltipDisplay>(true)) t.SetTooltipText(title);
            foreach (var t in button.GetComponentsInChildren<BasicTextTooltipData>(true)) t.TooltipTitleKey = title;
            foreach (var root in button.GetComponentsInChildren<ContextBindRoot>(true))
            {
                var context = new DataContext();
                context.AddProperty("TooltipTitle", new Property<string>(title));
                context.AddProperty("TooltipBody", new Property<string>(body));
                context.AddProperty("TooltipDescription", new Property<string>(body));
                root.BindContext(context);
            }
        }

        private void OnClicked(Ksp1Subcategory? type)
        {
            try
            {
                OabSubcategories.Selection.Set(_mod, type);
                _selected = type;
                OABAudioEventManager.onVABEditorPartsListCategorySelected();
                _picker.RefreshPartsPicker();
            }
            catch (Exception e)
            {
                _failed = true;
                OabSubcategories.Break("a sub-category click", e);
                SafeHide();
            }
        }

        private static string Translate(string key) =>
            LocalizationManager.TryGetTranslation(key, out var text) ? text : null;

        /// <summary>Stock sprite of the same category, else the first visible part's icon, else the template's.</summary>
        private Sprite IconFor(Ksp1Subcategory type, out Color color, out string source)
        {
            color = Color.white;
            Sprite sprite;
            if (Ksp1Subcategories.PrefersPartIcon(type) && TryPartIcon(type, out sprite, out source)) return sprite;
            foreach (var name in Ksp1Subcategories.StockIconCategories(type))
            {
                if (!Enum.TryParse(name, out PartCategories stock)) continue;
                var button = _picker.GetCategoryButton(stock);
                if (button == null || button.Icon == null || button.Icon.sprite == null) continue;
                color = button.Icon.color;
                source = $"stock {name} sprite '{button.Icon.sprite.name}'";
                return button.Icon.sprite;
            }
            if (TryPartIcon(type, out sprite, out source)) return sprite;
            var fallback = _template.Icon != null ? _template.Icon.sprite : null;
            if (_template.Icon != null) color = _template.Icon.color;
            source = "template sprite";
            return fallback;
        }

        private bool TryPartIcon(Ksp1Subcategory type, out Sprite sprite, out string source)
        {
            sprite = null;
            source = null;
            if (!_firstParts.TryGetValue(type, out var part) || part == null) return false;
            if (!Ksp1ImportedPartRegistry.TryGetIcon(Ksp1ImportedPartRegistry.GetIconKey(part), out sprite) || sprite == null) return false;
            _ownedSprites.Add(sprite); // a copy made for us (Redux's CloneIcon)
            source = $"icon of part '{part}'";
            return true;
        }

        // ---------------------------------------------------------------- layout

        /// <summary>Right of the toolbar (without its scrollbar), same height; the grid gives up the same width.</summary>
        private void Place()
        {
            var toolbar = _picker.partsCategoriesToolbarRT;
            var parent = _root.parent as RectTransform;
            if (toolbar == null || parent == null) return;
            LocalBounds(toolbar, parent, out var xMin, out var xMax, out var yMin, out var yMax);
            var scrollbar = toolbar.Find(ScrollbarName) as RectTransform;
            var scrollWidth = 0f;
            if (scrollbar != null && scrollbar.gameObject.activeSelf)
            {
                LocalBounds(scrollbar, parent, out var sMin, out var sMax, out _, out _);
                scrollWidth = sMax - sMin;
            }
            var width = Mathf.Max(8f, xMax - xMin - scrollWidth);
            var position = new Vector2(xMax - parent.rect.xMin, yMin - parent.rect.yMin);
            var size = new Vector2(width, yMax - yMin);
            if (_root.anchorMin != Vector2.zero) _root.anchorMin = Vector2.zero;
            if (_root.anchorMax != Vector2.zero) _root.anchorMax = Vector2.zero;
            if (_root.pivot != Vector2.zero) _root.pivot = Vector2.zero;
            if ((_root.anchoredPosition - position).sqrMagnitude > 0.01f) _root.anchoredPosition = position;
            if ((_root.sizeDelta - size).sqrMagnitude > 0.01f) _root.sizeDelta = size;

            var worldWidth = width * Mathf.Abs(parent.lossyScale.x);
            var contentBefore = _picker.partContentRT.offsetMin.x;
            Apply(_contentShift, _picker.partContentRT, worldWidth, true);
            var search = _picker.SearchFilterSortRT;
            var searchBeside = search != null && OverlapsVertically(search, _root);
            Apply(_searchShift, search, worldWidth, searchBeside);
            if (_placedLogged) return;
            _placedLogged = true;
            KSP1PartModPlugin.Log?.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "[KSP1PartMod] ui: bar placed at x {0:0.#} y {1:0.#} size {2:0.#}x{3:0.#} in '{4}' (toolbar scrollbar {5:0.#}); " +
                "partContentRT offsetMin.x {6:0.#} -> {7:0.#}; SearchFilterSortRT {8}",
                position.x, position.y, size.x, size.y, parent.name, scrollWidth, contentBefore, _picker.partContentRT.offsetMin.x,
                search == null ? "missing" : searchBeside ? $"beside the bar, narrowed too (offsetMin.x {search.offsetMin.x:0.#})" : "above or below the bar, left as it is"));
        }

        /// <summary>Moves the left edge of rt right by worldWidth (in its parent's units) while wanted, and back when
        /// not. A value someone else wrote in between becomes the new original.</summary>
        private static void Apply(Shift s, RectTransform rt, float worldWidth, bool want)
        {
            if (rt == null) return;
            if (s.Rt != rt)
            {
                Restore(s);
                s.Rt = rt;
                s.Active = false;
            }
            var parent = rt.parent as RectTransform;
            var scale = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
            var local = want ? worldWidth / (scale > 1e-6f ? scale : 1f) : 0f;
            var current = rt.offsetMin.x;
            if (!s.Active)
            {
                if (local <= 0f) return;
                s.Original = current;
                s.Applied = 0f;
                s.Active = true;
            }
            else if (Mathf.Abs(current - (s.Original + s.Applied)) > 0.01f)
            {
                s.Original = current;
                s.Applied = 0f;
            }
            if (Mathf.Abs(s.Applied - local) <= 0.01f) return;
            rt.offsetMin = new Vector2(s.Original + local, rt.offsetMin.y);
            s.Applied = local;
        }

        private static void Restore(Shift s)
        {
            if (!s.Active || s.Rt == null)
            {
                s.Active = false;
                return;
            }
            if (Mathf.Abs(s.Rt.offsetMin.x - (s.Original + s.Applied)) <= 0.01f)
                s.Rt.offsetMin = new Vector2(s.Original, s.Rt.offsetMin.y);
            s.Active = false;
            s.Applied = 0f;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static void LocalBounds(RectTransform rt, RectTransform space, out float xMin, out float xMax, out float yMin, out float yMax)
        {
            rt.GetWorldCorners(Corners);
            xMin = yMin = float.MaxValue;
            xMax = yMax = float.MinValue;
            foreach (var c in Corners)
            {
                var p = space.InverseTransformPoint(c);
                xMin = Mathf.Min(xMin, p.x);
                xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y);
                yMax = Mathf.Max(yMax, p.y);
            }
        }

        private static bool OverlapsVertically(RectTransform a, RectTransform b)
        {
            a.GetWorldCorners(Corners);
            float aMin = Corners[0].y, aMax = Corners[1].y;
            b.GetWorldCorners(Corners);
            float bMin = Corners[0].y, bMax = Corners[1].y;
            return Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin) > 1f;
        }

        // ---------------------------------------------------------------- hide / destroy

        private void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            Restore(_contentShift);
            Restore(_searchShift);
        }

        private void SafeHide()
        {
            try { Hide(); }
            catch (Exception) { /* the session flag already stops everything */ }
        }

        private void OnDestroy()
        {
            try
            {
                Hide();
                if (_root != null) Destroy(_root.gameObject);
                DestroyOwnedSprites();
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] ui: bar cleanup failed: " + e.Message);
            }
        }

        private void DestroyOwnedSprites()
        {
            foreach (var s in _ownedSprites)
                if (s != null) Destroy(s);
            _ownedSprites.Clear();
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayer(child.gameObject, layer);
        }

        // ---------------------------------------------------------------- diagnostics

        /// <summary>Once per session: the RectTransforms the layout relies on, the stock toggle group and the stock
        /// sprites, so the in-game test settles what the decompile cannot show (it is prefab data).</summary>
        private void LogLayoutOnce(RectTransform toolbarParent)
        {
            if (_layoutLogged) return;
            _layoutLogged = true;
            var log = KSP1PartModPlugin.Log;
            if (log == null) return;
            void Rt(string label, RectTransform rt)
            {
                if (rt == null)
                {
                    log.LogInfo($"[KSP1PartMod] ui: layout {label}: missing");
                    return;
                }
                var parent = rt.parent;
                var group = parent != null ? parent.GetComponent<LayoutGroup>() : null;
                log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "[KSP1PartMod] ui: layout {0} '{1}' parent '{2}'{3} anchors ({4:0.##},{5:0.##})-({6:0.##},{7:0.##}) pivot ({8:0.##},{9:0.##}) " +
                    "offsetMin ({10:0.#},{11:0.#}) offsetMax ({12:0.#},{13:0.#}) rect ({14:0.#},{15:0.#},{16:0.#},{17:0.#}) active={18}",
                    label, rt.name, parent != null ? parent.name : "-", group != null ? " [" + group.GetType().Name + "]" : "",
                    rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y, rt.pivot.x, rt.pivot.y,
                    rt.offsetMin.x, rt.offsetMin.y, rt.offsetMax.x, rt.offsetMax.y,
                    rt.rect.x, rt.rect.y, rt.rect.width, rt.rect.height, rt.gameObject.activeInHierarchy));
            }
            Rt("partsPickerBGPanelRT", _picker.partsPickerBGPanelRT);
            Rt("toolbar parent", toolbarParent);
            Rt("partsCategoriesToolbarRT", _picker.partsCategoriesToolbarRT);
            Rt("partContentRT", _picker.partContentRT);
            Rt("SearchFilterSortRT", _picker.SearchFilterSortRT);
            Rt("partsPickerScrollRect", _picker.partsPickerScrollRect != null ? _picker.partsPickerScrollRect.transform as RectTransform : null);
            Rt("manipulateShipToolbarRT", _picker.manipulateShipToolbarRT);
            var sprites = new StringBuilder();
            foreach (var sub in Ksp1Subcategories.Ordered)
                foreach (var name in Ksp1Subcategories.StockIconCategories(sub))
                {
                    if (sprites.ToString().Contains(" " + name + "=")) continue;
                    var button = Enum.TryParse(name, out PartCategories c) ? _picker.GetCategoryButton(c) : null;
                    sprites.Append(' ').Append(name).Append('=')
                        .Append(button == null ? "<no button>" : button.Icon == null || button.Icon.sprite == null ? "<no sprite>" : "'" + button.Icon.sprite.name + "'");
                }
            log.LogInfo("[KSP1PartMod] ui: stock category sprites:" + sprites);
        }
    }
}
