using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.Game;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;

namespace KSP1PartMod.Game
{
    /// <summary>The Redux members the ISRU glue needs (converters and drills plan, Task 2), resolved by name once:
    /// <list type="bullet">
    /// <item><c>Ksp1ResourceMapper</c>: the private static <c>DirectResourceMap</c> (KSP1 name → KSP2 name, edited in memory
    /// by <see cref="OreRemap"/>) and the private static <c>ConvertAmount</c> (KSP1 units → KSP2 units, by mass), called for
    /// every rate so a converter moves exactly what Redux's containers hold;</item>
    /// <item><c>Data_ResourceConverter</c> and <c>ResourceConverterFormulaDefinition</c>: the fields the converter importer
    /// writes;</item>
    /// <item><c>Ksp1PartLocalizationBuilder</c>: <c>AddPart</c> (patched), its private <c>Add(string, string)</c> and its
    /// private <c>_catalog</c> (whose public <c>Resolve</c> turns <c>#autoLOC</c> tags into English).</item>
    /// </list>
    /// Each group lists what it lacks; a missing member turns off only the feature that needs it.</summary>
    internal static class IsruAccess
    {
        public const string ResourceMapField = "DirectResourceMap";
        public const string ConvertAmountName = "ConvertAmount";
        public const string LocalizationBuilderTypeName = "Redux.Ksp1Import.Ksp1PartLocalizationBuilder";

        /// <summary>The bound members (null when missing).</summary>
        internal sealed class Targets
        {
            public FieldInfo ResourceMap;
            public MethodInfo ConvertAmount;
            public MethodInfo AddPart, Add;
            public FieldInfo Catalog;
            public readonly List<string> MissingOre = new List<string>();
            public readonly List<string> MissingConverter = new List<string>();
            public readonly List<string> MissingNames = new List<string>();
        }

        private static readonly object Lock = new object();
        private static Targets _targets;
        private static Func<string, string, double, ResourceDefinitionDatabase, Ksp1ResourceMapper.Ksp2ResourceMassLookup, double> _convert;

        /// <summary>Resolved once per session.</summary>
        internal static Targets Get()
        {
            lock (Lock) return _targets ?? (_targets = Resolve());
        }

        internal static Targets Resolve()
        {
            var t = new Targets();
            var mapper = typeof(Ksp1ResourceMapper);

            t.ResourceMap = AccessTools.Field(mapper, ResourceMapField);
            if (t.ResourceMap == null || !t.ResourceMap.IsStatic || !typeof(IDictionary<string, string>).IsAssignableFrom(t.ResourceMap.FieldType))
            {
                t.ResourceMap = null;
                t.MissingOre.Add("Ksp1ResourceMapper." + ResourceMapField);
            }

            t.ConvertAmount = AccessTools.Method(mapper, ConvertAmountName, new[]
            {
                typeof(string), typeof(string), typeof(double), typeof(ResourceDefinitionDatabase), typeof(Ksp1ResourceMapper.Ksp2ResourceMassLookup),
            });
            if (t.ConvertAmount == null || !t.ConvertAmount.IsStatic || t.ConvertAmount.ReturnType != typeof(double))
            {
                t.ConvertAmount = null;
                t.MissingConverter.Add("Ksp1ResourceMapper." + ConvertAmountName + "(string, string, double, ResourceDefinitionDatabase, Ksp2ResourceMassLookup)");
            }
            if (AccessTools.Method(mapper, nameof(Ksp1ResourceMapper.MapResourceName), new[] { typeof(string) }) == null)
                t.MissingConverter.Add("Ksp1ResourceMapper.MapResourceName");

            CheckFields(t.MissingConverter, typeof(Data_ResourceConverter), "Data_ResourceConverter", new Dictionary<string, Type>
            {
                ["FormulaDefinitions"] = typeof(List<ResourceConverterFormulaDefinition>),
                ["ConvertByMass"] = typeof(bool),
                ["ResourceAutoShutdown"] = typeof(bool),
                ["AutoShutdownTemperatureRatio"] = typeof(double),
                ["conversionRate"] = typeof(ModuleProperty<float>),
            });
            CheckFields(t.MissingConverter, typeof(ResourceConverterFormulaDefinition), "ResourceConverterFormulaDefinition", new Dictionary<string, Type>
            {
                ["InternalName"] = typeof(string),
                ["FormulaLocalizationKey"] = typeof(string),
                ["InputResources"] = typeof(List<PartModuleResourceSetting>),
                ["OutputResources"] = typeof(List<PartModuleResourceSetting>),
                ["FluxGenerated"] = typeof(float),
            });
            if (AccessTools.TypeByName("KSP.Modules.Module_ResourceConverter") == null) t.MissingConverter.Add("KSP.Modules.Module_ResourceConverter");

            var builder = AccessTools.TypeByName(LocalizationBuilderTypeName);
            if (builder == null) t.MissingNames.Add(LocalizationBuilderTypeName);
            else
            {
                t.AddPart = AccessTools.Method(builder, "AddPart", new[] { typeof(Ksp1ConfigNode), typeof(string) });
                if (t.AddPart == null || t.AddPart.IsStatic)
                {
                    t.AddPart = null;
                    t.MissingNames.Add("Ksp1PartLocalizationBuilder.AddPart(Ksp1ConfigNode, string)");
                }
                t.Add = AccessTools.Method(builder, "Add", new[] { typeof(string), typeof(string) });
                if (t.Add == null || t.Add.IsStatic || t.Add.ReturnType != typeof(void))
                {
                    t.Add = null;
                    t.MissingNames.Add("Ksp1PartLocalizationBuilder.Add(string, string)");
                }
                t.Catalog = AccessTools.Field(builder, "_catalog");
                if (t.Catalog == null || t.Catalog.IsStatic || t.Catalog.FieldType != typeof(Ksp1LocalizationCatalog))
                {
                    t.Catalog = null;
                    t.MissingNames.Add("Ksp1PartLocalizationBuilder._catalog");
                }
                if (AccessTools.Method(typeof(Ksp1LocalizationCatalog), "Resolve", new[] { typeof(string) }) == null)
                    t.MissingNames.Add("Ksp1LocalizationCatalog.Resolve(string)");
            }
            return t;
        }

        private static void CheckFields(List<string> missing, Type type, string typeName, Dictionary<string, Type> fields)
        {
            foreach (var kv in fields)
            {
                var f = AccessTools.Field(type, kv.Key);
                if (f == null || f.IsStatic || f.FieldType != kv.Value) missing.Add(typeName + "." + kv.Key);
            }
        }

        /// <summary>Redux's live resource table, or null when <c>DirectResourceMap</c> is missing.</summary>
        internal static IDictionary<string, string> ResourceMap()
        {
            var field = Get().ResourceMap;
            return field?.GetValue(null) as IDictionary<string, string>;
        }

        /// <summary>Redux's <c>ConvertAmount</c> (null database and mass lookup are Redux's own "unknown": amount unchanged).</summary>
        internal static double ConvertAmount(string source, string target, double amount, ResourceDefinitionDatabase database)
        {
            Func<string, string, double, ResourceDefinitionDatabase, Ksp1ResourceMapper.Ksp2ResourceMassLookup, double> convert;
            lock (Lock)
            {
                if (_convert == null)
                {
                    var method = Get().ConvertAmount ?? throw new MissingMethodException("Ksp1ResourceMapper", ConvertAmountName);
                    _convert = (Func<string, string, double, ResourceDefinitionDatabase, Ksp1ResourceMapper.Ksp2ResourceMassLookup, double>)
                        Delegate.CreateDelegate(typeof(Func<string, string, double, ResourceDefinitionDatabase, Ksp1ResourceMapper.Ksp2ResourceMassLookup, double>), method);
                }
                convert = _convert;
            }
            return convert(source, target, amount, database, null);
        }

        /// <summary>The game's resource database once it holds definitions (ElectricCharge known), else null (tests, or too
        /// early in the load).</summary>
        internal static ResourceDefinitionDatabase Database()
        {
            var db = GameManager.Instance?.Game?.ResourceDefinitionDatabase;
            if (db == null) return null;
            return db.GetResourceIDFromName(Native.IsruResources.ElectricCharge) == ResourceDefinitionID.InvalidID ? null : db;
        }

        internal static bool Exists(ResourceDefinitionDatabase db, string name) =>
            db != null && !string.IsNullOrEmpty(name) && db.GetResourceIDFromName(name) != ResourceDefinitionID.InvalidID;

        /// <summary>The KSP2 unit mass (t), NaN when unknown.</summary>
        internal static double MassPerUnit(ResourceDefinitionDatabase db, string name)
        {
            if (db == null || string.IsNullOrEmpty(name)) return double.NaN;
            var id = db.GetResourceIDFromName(name);
            return id == ResourceDefinitionID.InvalidID ? double.NaN : db.GetResourceUnitMass(id);
        }
    }
}
