using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>The Redux members the radiator importer needs (radiators plan, Task 2), resolved by name once:
    /// <list type="bullet">
    /// <item><c>Ksp1PartModuleImporterRegistry</c>'s private static <b>list</b> overload <c>CreateSerialized(Type,
    /// IReadOnlyList&lt;ModuleData&gt;, Ksp1PartImportContext)</c>, which adds every data object to the part and builds one
    /// <c>SerializedPartModule</c> that carries them all (<c>Module_ActiveRadiator</c> has two);</item>
    /// <item><c>Data_Cooler</c>: <c>defaultFluxRemoved</c> (JSON <c>fluxRemoved</c>, kW), <c>requiredResources</c>,
    /// <c>coolerEnabled</c> and the <c>CurrentCoolerState</c> setter (its pre-bind state). The type implements Unity.Entities'
    /// <c>IComponentData</c>, which this assembly does not reference: it is only handled by reflection;</item>
    /// <item><c>Data_ActiveRadiator</c>: <c>RadiatorDirection</c> and <c>ProceduralRadiatorFluxPerAreaUnit</c>.</item>
    /// </list>
    /// A missing <c>Data_Cooler</c> member turns the radiator importer off (<see cref="Targets.MissingCooler"/>); a missing list
    /// overload or <c>Data_ActiveRadiator</c> member only turns <c>Module_ActiveRadiator</c> off
    /// (<see cref="Targets.MissingActive"/>): deployable radiators then get <c>Module_Cooler</c>.</summary>
    internal static class RadiatorAccess
    {
        public const string DataCoolerTypeName = "KSP.Modules.Data_Cooler";
        public const string DataActiveRadiatorTypeName = "KSP.Modules.Data_ActiveRadiator";
        public const string ModuleCoolerTypeName = "KSP.Modules.Module_Cooler";
        public const string ModuleActiveRadiatorTypeName = "KSP.Modules.Module_ActiveRadiator";
        public const string CoolerStatesTypeName = "KSP.Sim.impl.CoolerStates";
        public const string TransformDirName = "TransformDir";
        /// <summary>The native <c>AcceptanceThreshold</c> of both KSP2 radiators (26w42c part data).</summary>
        public const double AcceptanceThreshold = 0.01;

        /// <summary>The bound members (null when missing).</summary>
        internal sealed class Targets
        {
            public Type DataCooler, DataActiveRadiator, ModuleCooler, ModuleActiveRadiator, CoolerStates, TransformDir;
            public MethodInfo CreateSerializedList;
            public FieldInfo FluxRemoved, RequiredResources, CoolerEnabled;
            public PropertyInfo CurrentCoolerState;
            public FieldInfo RadiatorDirection, FluxPerArea;
            /// <summary>Optional: the collections the native part data holds empty and Redux's code reads without a null check
            /// (<c>resourceDisplayNames</c>, <c>emissiveMaterialNames</c>, <c>InputResourcesIDs</c>, <c>RequestConfigs</c>).</summary>
            public readonly List<FieldInfo> EmptyCollections = new List<FieldInfo>();
            /// <summary>Without these, no radiator importer.</summary>
            public readonly List<string> MissingCooler = new List<string>();
            /// <summary>Without these, deployable radiators get <c>Module_Cooler</c>.</summary>
            public readonly List<string> MissingActive = new List<string>();
            /// <summary>The list overload of <c>CreateSerialized</c> was found.</summary>
            public bool HasListForm => CreateSerializedList != null;
            public bool HasActiveRadiator => MissingActive.Count == 0;
        }

        /// <summary>Tests: resolve as if the list overload did not exist.</summary>
        internal static bool HideListFormForTests;

        private static readonly object Lock = new object();
        private static Targets _targets;
        private static Func<Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext, SerializedPartModule> _createList;

        /// <summary>Resolved once per session.</summary>
        internal static Targets Get()
        {
            lock (Lock) return _targets ?? (_targets = Resolve());
        }

        internal static void Reset()
        {
            lock (Lock)
            {
                _targets = null;
                _createList = null;
            }
            HideListFormForTests = false;
        }

        internal static Targets Resolve()
        {
            var t = new Targets();

            t.DataCooler = AccessTools.TypeByName(DataCoolerTypeName);
            t.ModuleCooler = AccessTools.TypeByName(ModuleCoolerTypeName);
            t.CoolerStates = AccessTools.TypeByName(CoolerStatesTypeName);
            if (t.DataCooler == null || !typeof(ModuleData).IsAssignableFrom(t.DataCooler)) { t.DataCooler = null; t.MissingCooler.Add(DataCoolerTypeName); }
            if (t.ModuleCooler == null) t.MissingCooler.Add(ModuleCoolerTypeName);
            if (t.CoolerStates == null || !t.CoolerStates.IsEnum) { t.CoolerStates = null; t.MissingCooler.Add(CoolerStatesTypeName); }
            if (t.DataCooler != null)
            {
                if (t.DataCooler.GetConstructor(Type.EmptyTypes) == null) t.MissingCooler.Add("Data_Cooler()");
                t.FluxRemoved = Field(t.DataCooler, "defaultFluxRemoved", typeof(double), t.MissingCooler, "Data_Cooler");
                t.RequiredResources = Field(t.DataCooler, "requiredResources", typeof(List<PartModuleResourceSetting>), t.MissingCooler, "Data_Cooler");
                t.CoolerEnabled = Field(t.DataCooler, "coolerEnabled", typeof(ModuleProperty<bool>), t.MissingCooler, "Data_Cooler");
                t.CurrentCoolerState = AccessTools.Property(t.DataCooler, "CurrentCoolerState");
                if (t.CurrentCoolerState == null || t.CoolerStates == null || t.CurrentCoolerState.PropertyType != t.CoolerStates ||
                    t.CurrentCoolerState.GetSetMethod(true) == null || t.CurrentCoolerState.GetGetMethod(true) == null ||
                    t.CurrentCoolerState.GetSetMethod(true).IsStatic)
                {
                    t.CurrentCoolerState = null;
                    t.MissingCooler.Add("Data_Cooler.CurrentCoolerState (setter)");
                }
                if (t.CoolerStates != null)
                    foreach (var state in new[] { "OFF", "IDLE" })
                        if (!Enum.GetNames(t.CoolerStates).Contains(state)) t.MissingCooler.Add("CoolerStates." + state);
                foreach (var name in new[] { "resourceDisplayNames", "emissiveMaterialNames", "InputResourcesIDs", "RequestConfigs" })
                {
                    var f = AccessTools.Field(t.DataCooler, name);
                    if (f != null && !f.IsStatic && (f.FieldType.IsArray || typeof(IList).IsAssignableFrom(f.FieldType)) &&
                        (f.FieldType.IsArray || f.FieldType.GetConstructor(Type.EmptyTypes) != null))
                        t.EmptyCollections.Add(f);
                }
            }

            var registry = AccessTools.TypeByName(NativeModules.RegistryTypeName);
            if (registry != null && !HideListFormForTests)
            {
                var m = AccessTools.Method(registry, NativeModules.CreateSerializedName,
                    new[] { typeof(Type), typeof(IReadOnlyList<ModuleData>), typeof(Ksp1PartImportContext) });
                if (m != null && m.IsStatic && m.ReturnType == typeof(SerializedPartModule)) t.CreateSerializedList = m;
            }
            if (t.CreateSerializedList == null)
                t.MissingActive.Add("Ksp1PartModuleImporterRegistry." + NativeModules.CreateSerializedName + "(Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext)");

            t.DataActiveRadiator = AccessTools.TypeByName(DataActiveRadiatorTypeName);
            t.ModuleActiveRadiator = AccessTools.TypeByName(ModuleActiveRadiatorTypeName);
            if (t.DataActiveRadiator == null || !typeof(ModuleData).IsAssignableFrom(t.DataActiveRadiator) ||
                t.DataActiveRadiator.GetConstructor(Type.EmptyTypes) == null)
            {
                t.DataActiveRadiator = null;
                t.MissingActive.Add(DataActiveRadiatorTypeName);
            }
            if (t.ModuleActiveRadiator == null) t.MissingActive.Add(ModuleActiveRadiatorTypeName);
            if (t.DataActiveRadiator != null)
            {
                // Data_LiftingSurface.TransformDir, read from the field itself.
                var direction = AccessTools.Field(t.DataActiveRadiator, "RadiatorDirection");
                if (direction != null && !direction.IsStatic && direction.FieldType.IsEnum && direction.FieldType.Name == TransformDirName &&
                    Enum.GetNames(direction.FieldType).Contains("X"))
                {
                    t.RadiatorDirection = direction;
                    t.TransformDir = direction.FieldType;
                }
                else t.MissingActive.Add("Data_ActiveRadiator.RadiatorDirection (TransformDir.X)");
                t.FluxPerArea = Field(t.DataActiveRadiator, "ProceduralRadiatorFluxPerAreaUnit", typeof(float), t.MissingActive, "Data_ActiveRadiator");
            }
            return t;
        }

        private static FieldInfo Field(Type type, string name, Type fieldType, List<string> missing, string typeName)
        {
            var f = AccessTools.Field(type, name);
            if (f != null && !f.IsStatic && f.FieldType == fieldType) return f;
            missing.Add(typeName + "." + name);
            return null;
        }

        /// <summary>Redux's list <c>CreateSerialized</c>: adds every data object to the part's module data, in order.</summary>
        internal static SerializedPartModule CreateSerialized(Type behaviourType, IReadOnlyList<ModuleData> data, Ksp1PartImportContext context)
        {
            Func<Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext, SerializedPartModule> create;
            lock (Lock)
            {
                if (_createList == null)
                {
                    var method = Get().CreateSerializedList ?? throw new MissingMethodException("Ksp1PartModuleImporterRegistry", NativeModules.CreateSerializedName);
                    _createList = (Func<Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext, SerializedPartModule>)Delegate.CreateDelegate(
                        typeof(Func<Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext, SerializedPartModule>), method);
                }
                create = _createList;
            }
            return create(behaviourType, data, context);
        }

        /// <summary>A <c>Data_Cooler</c> of <paramref name="kw"/> kW drawing <paramref name="resources"/> (per second), on and
        /// <c>IDLE</c> when <paramref name="on"/>, else off and <c>OFF</c> (KSP2's own start).</summary>
        internal static ModuleData NewCooler(double kw, IEnumerable<(string Name, double Rate)> resources, bool on)
        {
            var t = Get();
            if (t.MissingCooler.Count > 0) throw new MissingMemberException(string.Join(", ", t.MissingCooler));
            var data = (ModuleData)Activator.CreateInstance(t.DataCooler);
            foreach (var f in t.EmptyCollections)
                if (f.GetValue(data) == null)
                    f.SetValue(data, f.FieldType.IsArray ? Array.CreateInstance(f.FieldType.GetElementType(), 0) : Activator.CreateInstance(f.FieldType));
            t.FluxRemoved.SetValue(data, kw);
            t.RequiredResources.SetValue(data, (resources ?? Enumerable.Empty<(string, double)>())
                .Select(r => new PartModuleResourceSetting { ResourceName = r.Item1, Rate = (float)r.Item2, AcceptanceThreshold = AcceptanceThreshold })
                .ToList());
            var enabled = (ModuleProperty<bool>)t.CoolerEnabled.GetValue(data);
            if (enabled == null) throw new InvalidOperationException("Data_Cooler.coolerEnabled is null");
            enabled.SetValue(on);
            t.CurrentCoolerState.SetValue(data, Enum.Parse(t.CoolerStates, on ? "IDLE" : "OFF"));
            return data;
        }

        /// <summary>A <c>Data_ActiveRadiator</c> as KSP2's own deployable radiator: direction X, no procedural flux.</summary>
        internal static ModuleData NewActiveRadiator()
        {
            var t = Get();
            if (t.MissingActive.Count > 0) throw new MissingMemberException(string.Join(", ", t.MissingActive));
            var data = (ModuleData)Activator.CreateInstance(t.DataActiveRadiator);
            t.RadiatorDirection.SetValue(data, Enum.Parse(t.TransformDir, "X"));
            t.FluxPerArea.SetValue(data, 0f);
            return data;
        }

        /// <summary><paramref name="data"/> is a <c>Data_Cooler</c>.</summary>
        internal static bool IsCooler(ModuleData data)
        {
            var type = Get().DataCooler;
            return data != null && type != null && type.IsInstanceOfType(data);
        }

        internal static double FluxOf(ModuleData cooler) => (double)Get().FluxRemoved.GetValue(cooler);

        internal static List<PartModuleResourceSetting> ResourcesOf(ModuleData cooler) =>
            Get().RequiredResources.GetValue(cooler) as List<PartModuleResourceSetting> ?? new List<PartModuleResourceSetting>();

        internal static bool EnabledOf(ModuleData cooler) => ((ModuleProperty<bool>)Get().CoolerEnabled.GetValue(cooler)).GetValue();

        /// <summary>The state's name (<c>OFF</c>, <c>IDLE</c>, ...).</summary>
        internal static string StateOf(ModuleData cooler) => Get().CurrentCoolerState.GetValue(cooler)?.ToString();

        /// <summary>The kW per EC/s of a native cooler: <c>(kW, EC/s)</c>, EC/s 0 when it uses no electric charge.</summary>
        internal static (double Kw, double Ec) PaceOf(ModuleData cooler) =>
            (FluxOf(cooler), ResourcesOf(cooler).Where(r => r.ResourceName == IsruResources.ElectricCharge).Sum(r => (double)r.Rate));
    }
}
