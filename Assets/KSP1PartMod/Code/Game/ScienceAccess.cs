using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP1PartMod.Native.Science;

namespace KSP1PartMod.Game
{
    /// <summary>The Redux members the science importer and runtime need (science plan, Task 4), resolved by name once:
    /// <list type="bullet">
    /// <item><c>Data_ScienceExperiment</c> and its behaviour <c>Module_ScienceExperiment</c>: <c>Experiments</c>
    /// (<c>List&lt;ExperimentConfiguration&gt;</c>), <c>NotifyOnCompletion</c>; for the runtime (Task 5) <c>IsPartAnimated</c> and
    /// <c>PartIsDeployed</c>;</item>
    /// <item>the <c>ExperimentConfiguration</c> struct: <c>ExperimentDefinitionID</c>, <c>CrewRequired</c>, <c>TimeToComplete</c>,
    /// <c>ContextualExperiment</c>, <c>ResourcesCost</c> (never null: the part info reads its count), <c>DenyInterruption</c>,
    /// <c>DeployedBoolAnimatorParamName</c>, <c>CFXName</c>; optional <c>ExperimentUsesResources</c>;</item>
    /// <item><c>PartData.HasReportStorage</c>.</item>
    /// </list>
    /// A missing experiment member turns the experiment importers off (<see cref="Targets.MissingExperiment"/>); a missing
    /// <c>HasReportStorage</c> turns report storage off only (<see cref="Targets.MissingStorage"/>); a missing runtime member is
    /// for Task 5 (<see cref="Targets.MissingRuntime"/>).</summary>
    internal static class ScienceAccess
    {
        public const string DataTypeName = "KSP.Modules.Data_ScienceExperiment";
        public const string ModuleTypeName = "KSP.Modules.Module_ScienceExperiment";
        public const string ConfigurationTypeName = "KSP.Modules.ExperimentConfiguration";

        internal sealed class Targets
        {
            public Type Data, Module, Configuration;
            public FieldInfo Experiments, NotifyOnCompletion, IsPartAnimated, PartIsDeployed;
            public FieldInfo DefinitionId, CrewRequired, TimeToComplete, Contextual, ResourcesCost, DenyInterruption, AnimatorParam, CfxName;
            /// <summary>Optional (false is its default).</summary>
            public FieldInfo UsesResources;
            public FieldInfo HasReportStorage;
            public readonly List<string> MissingExperiment = new List<string>();
            public readonly List<string> MissingStorage = new List<string>();
            public readonly List<string> MissingRuntime = new List<string>();
            public bool HasStorage => MissingStorage.Count == 0;
        }

        private static readonly object Lock = new object();
        private static Targets _targets;

        internal static Targets Get()
        {
            lock (Lock) return _targets ?? (_targets = Resolve());
        }

        internal static void Reset()
        {
            lock (Lock) _targets = null;
        }

        internal static Targets Resolve()
        {
            var t = new Targets();
            t.Data = AccessTools.TypeByName(DataTypeName);
            t.Module = AccessTools.TypeByName(ModuleTypeName);
            t.Configuration = AccessTools.TypeByName(ConfigurationTypeName);
            if (t.Data == null || !typeof(ModuleData).IsAssignableFrom(t.Data) || t.Data.GetConstructor(Type.EmptyTypes) == null)
            {
                t.Data = null;
                t.MissingExperiment.Add(DataTypeName);
            }
            if (t.Module == null) t.MissingExperiment.Add(ModuleTypeName);
            if (t.Configuration == null || !t.Configuration.IsValueType)
            {
                t.Configuration = null;
                t.MissingExperiment.Add(ConfigurationTypeName);
            }
            if (t.Configuration != null)
            {
                var listType = typeof(List<>).MakeGenericType(t.Configuration);
                if (t.Data != null)
                {
                    t.Experiments = Field(t.Data, "Experiments", listType, t.MissingExperiment, "Data_ScienceExperiment");
                    t.NotifyOnCompletion = Field(t.Data, "NotifyOnCompletion", typeof(bool), t.MissingExperiment, "Data_ScienceExperiment");
                    t.IsPartAnimated = Field(t.Data, "IsPartAnimated", typeof(bool), t.MissingRuntime, "Data_ScienceExperiment");
                    t.PartIsDeployed = Field(t.Data, "PartIsDeployed", typeof(bool), t.MissingRuntime, "Data_ScienceExperiment");
                }
                const string c = "ExperimentConfiguration";
                t.DefinitionId = Field(t.Configuration, "ExperimentDefinitionID", typeof(string), t.MissingExperiment, c);
                t.CrewRequired = Field(t.Configuration, "CrewRequired", typeof(int), t.MissingExperiment, c);
                t.TimeToComplete = Field(t.Configuration, "TimeToComplete", typeof(float), t.MissingExperiment, c);
                t.Contextual = Field(t.Configuration, "ContextualExperiment", typeof(bool), t.MissingExperiment, c);
                t.ResourcesCost = Field(t.Configuration, "ResourcesCost", typeof(List<PartModuleResourceSetting>), t.MissingExperiment, c);
                t.DenyInterruption = Field(t.Configuration, "DenyInterruption", typeof(bool), t.MissingExperiment, c);
                t.AnimatorParam = Field(t.Configuration, "DeployedBoolAnimatorParamName", typeof(string), t.MissingExperiment, c);
                t.CfxName = Field(t.Configuration, "CFXName", typeof(string), t.MissingExperiment, c);
                t.UsesResources = Field(t.Configuration, "ExperimentUsesResources", typeof(bool), new List<string>(), c);
            }
            t.HasReportStorage = Field(typeof(PartData), "HasReportStorage", typeof(bool), t.MissingStorage, "PartData");
            return t;
        }

        private static FieldInfo Field(Type type, string name, Type fieldType, List<string> missing, string typeName)
        {
            var f = AccessTools.Field(type, name);
            if (f != null && !f.IsStatic && f.FieldType == fieldType) return f;
            missing.Add(typeName + "." + name);
            return null;
        }

        /// <summary><paramref name="data"/> is a <c>Data_ScienceExperiment</c>.</summary>
        internal static bool IsExperiment(ModuleData data)
        {
            var type = Get().Data;
            return data != null && type != null && type.IsInstanceOfType(data);
        }

        /// <summary>A <c>Data_ScienceExperiment</c> with one <c>ExperimentConfiguration</c> per configuration of <paramref name="part"/>, in
        /// order: no resource cost (an empty list), no contextual run, no animator parameter or CFX, interruption allowed.
        /// <c>IsPartAnimated</c> and <c>PartIsDeployed</c> stay false (the runtime sets them).</summary>
        internal static ModuleData NewExperiment(SciencePart part)
        {
            var t = Get();
            if (t.MissingExperiment.Count > 0) throw new MissingMemberException(string.Join(", ", t.MissingExperiment));
            var data = (ModuleData)Activator.CreateInstance(t.Data);
            var list = (IList)Activator.CreateInstance(t.Experiments.FieldType);
            foreach (var c in part.Configurations)
            {
                var boxed = Activator.CreateInstance(t.Configuration);
                t.DefinitionId.SetValue(boxed, c.ExperimentDefinitionId);
                t.CrewRequired.SetValue(boxed, c.CrewRequired);
                t.TimeToComplete.SetValue(boxed, (float)c.TimeToComplete);
                t.Contextual.SetValue(boxed, c.ContextualExperiment);
                t.ResourcesCost.SetValue(boxed, new List<PartModuleResourceSetting>());
                t.DenyInterruption.SetValue(boxed, c.DenyInterruption);
                t.AnimatorParam.SetValue(boxed, c.DeployedBoolAnimatorParamName);
                t.CfxName.SetValue(boxed, c.CFXName);
                t.UsesResources?.SetValue(boxed, false);
                list.Add(boxed);
            }
            t.Experiments.SetValue(data, list);
            t.NotifyOnCompletion.SetValue(data, part.NotifyOnCompletion);
            return data;
        }

        /// <summary>The part's report storage flag (false when the member is missing).</summary>
        internal static bool StorageOf(PartData part)
        {
            var f = Get().HasReportStorage;
            return part != null && f != null && (bool)f.GetValue(part);
        }

        /// <summary>Sets the part's report storage flag; false when the member is missing.</summary>
        internal static bool SetStorage(PartData part, bool value)
        {
            var f = Get().HasReportStorage;
            if (part == null || f == null) return false;
            f.SetValue(part, value);
            return true;
        }
    }
}
