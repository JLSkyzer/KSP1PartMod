using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>One <c>RESOURCE</c> of a <c>B9_TANK_TYPE</c> or of a SUBTYPE (B9PartSwitch's public wiki, "Tank
    /// Definitions" and "ModuleB9PartSwitch", read 2026-10-28). A missing value is null; a value that is not a number is
    /// null too.</summary>
    public sealed class B9ResourceDef
    {
        public string Name;
        public double? UnitsPerVolume;
        public double? PercentFilled;
        public double? Amount;
        public double? MaxAmount;

        public static B9ResourceDef From(IKsp1Node node) => node == null ? null : new B9ResourceDef
        {
            Name = B9Values.Trim(node.GetValue("name")),
            UnitsPerVolume = B9Values.Optional(node.GetValue("unitsPerVolume")),
            PercentFilled = B9Values.Optional(node.GetValue("percentFilled")),
            Amount = B9Values.Optional(node.GetValue("amount")),
            MaxAmount = B9Values.Optional(node.GetValue("maxAmount")),
        };

        /// <summary>This definition with every value <paramref name="over"/> has replacing it (the wiki: a SUBTYPE
        /// RESOURCE of the tank's resource name overrides its fields).</summary>
        public B9ResourceDef MergedWith(B9ResourceDef over) => over == null ? this : new B9ResourceDef
        {
            Name = Name,
            UnitsPerVolume = over.UnitsPerVolume ?? UnitsPerVolume,
            PercentFilled = over.PercentFilled ?? PercentFilled,
            Amount = over.Amount ?? Amount,
            MaxAmount = over.MaxAmount ?? MaxAmount,
        };
    }

    /// <summary>One <c>B9_TANK_TYPE</c>: <c>tankMass</c> and <c>tankCost</c> per unit of volume, a default
    /// <c>percentFilled</c>, and its resources.</summary>
    public sealed class B9TankType
    {
        public string Name;
        /// <summary>Dry mass per unit of volume, in tonnes; null when the type has none (Redux's built-ins).</summary>
        public double? TankMass;
        public double? TankCost;
        public double? PercentFilled;
        public IReadOnlyList<B9ResourceDef> Resources = new B9ResourceDef[0];
        /// <summary>One of Redux's built-in names, given by the glue (resources only).</summary>
        public bool BuiltIn;

        public static B9TankType From(IKsp1Node node) => node == null ? null : new B9TankType
        {
            Name = B9Values.Trim(node.GetValue("name")),
            TankMass = B9Values.Optional(node.GetValue("tankMass")),
            TankCost = B9Values.Optional(node.GetValue("tankCost")),
            PercentFilled = B9Values.Optional(node.GetValue("percentFilled")),
            Resources = node.GetNodes("RESOURCE").Select(B9ResourceDef.From)
                .Where(r => !string.IsNullOrEmpty(r.Name)).ToList(),
        };

        /// <summary>A type known only from Redux's built-in catalog: resources, no <c>tankMass</c> or <c>tankCost</c>.</summary>
        public static B9TankType ResourceOnly(string name, IEnumerable<B9ResourceDef> resources) => new B9TankType
        {
            Name = B9Values.Trim(name), BuiltIn = true,
            Resources = (resources ?? Enumerable.Empty<B9ResourceDef>()).Where(r => r != null).ToList(),
        };

        /// <summary>B9's hard-coded empty type: no mass, no cost, no resources (wiki).</summary>
        public static B9TankType Structural() => new B9TankType { Name = B9TankTypes.StructuralName, TankMass = 0, TankCost = 0 };
    }

    /// <summary>The <c>B9_TANK_TYPE</c> catalog by name (ignoring case). A duplicate name keeps the first definition
    /// (the wiki: "duplicates won't be loaded") and is listed in <see cref="Duplicates"/>; <c>Structural</c> is added when
    /// no file defines it; Redux's built-ins fill the names no file defines.</summary>
    public sealed class B9TankTypes
    {
        public const string StructuralName = "Structural";
        private const string RootName = "B9_TANK_TYPE";

        private readonly Dictionary<string, B9TankType> _byName = new Dictionary<string, B9TankType>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _duplicates = new List<string>();

        /// <summary>The names defined more than once in the files, as spelled by the later definitions.</summary>
        public IReadOnlyList<string> Duplicates => _duplicates;
        public int Count => _byName.Count;
        public IEnumerable<B9TankType> All => _byName.Values;

        /// <param name="roots">Root nodes; only those named <c>B9_TANK_TYPE</c> (or <c>B9_TANK_TYPE:…</c> in a raw
        /// file) are read.</param>
        /// <param name="builtIns">Redux's built-in types (<see cref="B9TankType.ResourceOnly"/>), passed by the glue.</param>
        public static B9TankTypes From(IEnumerable<IKsp1Node> roots, IEnumerable<B9TankType> builtIns = null)
        {
            var types = new B9TankTypes();
            foreach (var root in roots ?? Enumerable.Empty<IKsp1Node>())
            {
                if (!IsTankRoot(root)) continue;
                var t = B9TankType.From(root);
                if (string.IsNullOrEmpty(t.Name)) continue;
                if (types._byName.ContainsKey(t.Name)) types._duplicates.Add(t.Name);
                else types._byName[t.Name] = t;
            }
            foreach (var b in builtIns ?? Enumerable.Empty<B9TankType>())
                if (b != null && !string.IsNullOrEmpty(b.Name) && !types._byName.ContainsKey(b.Name))
                    types._byName[b.Name] = b;
            if (!types._byName.ContainsKey(StructuralName)) types._byName[StructuralName] = B9TankType.Structural();
            return types;
        }

        /// <summary>The type of that name (ignoring case, trimmed), or null.</summary>
        public B9TankType Find(string name)
        {
            var n = B9Values.Trim(name);
            return string.IsNullOrEmpty(n) ? null : _byName.TryGetValue(n, out var t) ? t : null;
        }

        private static bool IsTankRoot(IKsp1Node node)
        {
            var name = node?.Name;
            if (name == null) return false;
            var colon = name.IndexOf(':');
            return (colon >= 0 ? name.Substring(0, colon) : name).Trim() == RootName;
        }
    }
}
