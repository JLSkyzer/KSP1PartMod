using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VSwift.Modules.Behaviours;
using VSwift.Modules.Transformers;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Watches the V-SwiFT selection of one part instance (spec "The variant-change signal: poll"). No message is
    /// published for a pick, and a symmetry sync replaces the whole <c>Data_PartSwitch.ActiveVariants</c> list, so the
    /// field is read again on every <see cref="Poll"/> (the list is never cached), each item is compared by reference with
    /// the last one seen and, only when the reference differs, by ordinal value: a new string with the same value (read
    /// from a save, copied by a sync) is not a change. The set ids are those of <c>VariantSets</c> at bind (same index as
    /// <c>ActiveVariants</c>). Poll is allocation-free.</summary>
    internal sealed class VariantWatch
    {
        private static readonly string[] NoIds = new string[0];

        private readonly Func<List<string>> _read;
        private readonly string[] _setIds;
        private readonly string[] _seen, _value;
        private readonly bool[] _changed;
        /// <summary>Per set: variant id -> the names its <c>TransformActivator</c>s switch on (logs only).</summary>
        private readonly Dictionary<string, List<string>>[] _transforms;

        /// <param name="setIds">The variant set ids, in <c>VariantSets</c> order.</param>
        /// <param name="read">Returns the current <c>ActiveVariants</c> list (may be null); called once per poll.</param>
        internal VariantWatch(string[] setIds, Func<List<string>> read, Dictionary<string, List<string>>[] transforms = null)
        {
            _setIds = setIds ?? NoIds;
            _read = read;
            _seen = new string[_setIds.Length];
            _value = new string[_setIds.Length];
            _changed = new bool[_setIds.Length];
            _transforms = transforms;
            var list = Read();
            for (var i = 0; i < _setIds.Length; i++) _seen[i] = _value[i] = Item(list, i);
        }

        /// <summary>The part's <c>Module_PartSwitch</c>, read through a delegate cached here. Null (and
        /// <paramref name="note"/> says why) when the part has no switch or no data yet; a gate then keeps its default
        /// subtype.</summary>
        public static VariantWatch Create(Component part, out string note, out bool initialized)
        {
            initialized = false;
            var ps = part.GetComponent<Module_PartSwitch>();
            if (ps == null)
            {
                note = "no Module_PartSwitch";
                return null;
            }
            initialized = ps.IsInitialized;
            var d = ps.DataPartSwitch;
            if (d == null || d.VariantSets == null)
            {
                note = "Module_PartSwitch has no data";
                return null;
            }
            var ids = new string[d.VariantSets.Count];
            var transforms = new Dictionary<string, List<string>>[ids.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                var set = d.VariantSets[i];
                ids[i] = set?.VariantSetId;
                if (set?.Variants == null) continue;
                foreach (var v in set.Variants)
                {
                    if (v?.Transformers == null || v.VariantId == null) continue;
                    foreach (var t in v.Transformers)
                    {
                        if (!(t is TransformActivator a) || a.Transforms == null) continue;
                        transforms[i] ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
                        if (!transforms[i].TryGetValue(v.VariantId, out var names)) transforms[i][v.VariantId] = names = new List<string>();
                        names.AddRange(a.Transforms);
                    }
                }
            }
            note = null;
            Func<List<string>> read = () => ps == null ? null : ps.DataPartSwitch?.ActiveVariants;
            return new VariantWatch(ids, read, transforms);
        }

        public int SetCount => _setIds.Length;

        public string SetId(int set) => _setIds[set];

        /// <summary>The index of the set named <paramref name="setId"/> (ordinal, as Redux matches), -1 when none.</summary>
        public int IndexOf(string setId)
        {
            if (setId == null) return -1;
            for (var i = 0; i < _setIds.Length; i++)
                if (string.Equals(_setIds[i], setId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>The selected variant of <paramref name="set"/> at the last poll; null when the list has no item for it.</summary>
        public string ActiveOf(int set) => set >= 0 && set < _value.Length ? _value[set] : null;

        /// <summary>The value of <paramref name="set"/> changed at the last <see cref="Poll"/>.</summary>
        public bool Changed(int set) => set >= 0 && set < _changed.Length && _changed[set];

        /// <summary>Reads the selection once; true when a set's value changed (<see cref="Changed"/>). Allocation-free.</summary>
        public bool Poll()
        {
            var list = Read();
            var any = false;
            for (var i = 0; i < _setIds.Length; i++)
            {
                _changed[i] = false;
                var item = Item(list, i);
                if (ReferenceEquals(item, _seen[i])) continue;
                _seen[i] = item;
                if (string.Equals(item, _value[i], StringComparison.Ordinal)) continue;
                _value[i] = item;
                _changed[i] = any = true;
            }
            return any;
        }

        /// <summary>"[set=id, …]" for the bind line.</summary>
        public string Describe()
        {
            var sb = new StringBuilder("[");
            for (var i = 0; i < _setIds.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_setIds[i]).Append('=').Append(_value[i] ?? "none");
            }
            return sb.Append(']').ToString();
        }

        /// <summary>For the log: how many of the transforms <paramref name="variant"/> of <paramref name="set"/> switches
        /// on are active in the hierarchy under <paramref name="root"/> (searched recursively by name, inactive included).
        /// False when the variant switches no transform.</summary>
        public bool TransformsActive(Transform root, int set, string variant, out int active, out int total)
        {
            active = total = 0;
            if (_transforms == null || set < 0 || set >= _transforms.Length || _transforms[set] == null || variant == null
                || !_transforms[set].TryGetValue(variant, out var names) || names.Count == 0)
                return false;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var name in names)
            {
                total++;
                foreach (var t in all)
                    if (t.name == name && t.gameObject.activeInHierarchy)
                    {
                        active++;
                        break;
                    }
            }
            return true;
        }

        private List<string> Read()
        {
            try
            {
                return _read?.Invoke();
            }
            catch (Exception)
            {
                return null; // the switch went away with the part: no selection
            }
        }

        private static string Item(List<string> list, int i) => list != null && i < list.Count ? list[i] : null;
    }
}
