using System;
using System.Collections.Generic;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Plays the KSP1 animations of an imported part that Redux leaves alone. Skeleton: it only reports
    /// that it reached the instance; the binding and driving come later.</summary>
    [DisallowMultipleComponent]
    public class Module_Ksp1Animations : PartBehaviourModule
    {
        /// <summary>Assigned by Redux's <c>PartModuleLoadPatcher.ApplyOnGameObject</c>, which only fills
        /// <c>[SerializeField]</c> fields of a <c>ModuleData</c> type.</summary>
        [SerializeField] private Data_Ksp1Animations data;

        public override Type PartComponentModuleType => typeof(PartComponentModule_Ksp1Animations);

        /// <summary>Runs before <see cref="OnInitialize"/>, during part instantiation: never throws. With no data
        /// (for example our <c>$type</c> not resolved when the definition was read), an empty data object takes its
        /// place, so the part still loads and the module does nothing.</summary>
        protected override void AddDataModules()
        {
            try
            {
                base.AddDataModules();
                if (data == null)
                {
                    KSP1PartModPlugin.Log?.LogWarning($"[KSP1PartMod] anim: {PartName()} module has no data (type not resolved?)");
                    data = new Data_Ksp1Animations();
                }
                DataModules.TryAddUnique(data, out data);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: {PartName()} module data setup failed: {e}");
            }
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                if (data == null) return; // AddDataModules failed and said so
                data.Entries ??= new List<AnimEntryDef>();
                data.SetVisible(data.State, false);
                var n = data.Entries.Count;
                KSP1PartModPlugin.Log?.LogInfo(
                    $"[KSP1PartMod] anim: {PartName()} module present ({PartBackingMode}) with {n} {(n == 1 ? "entry" : "entries")}");
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: {PartName()} module init failed: {e}");
            }
        }

        protected override void OnModuleUpdate(float deltaTime) { }

        protected override void OnModuleOABUpdate(float deltaTime) { }

        protected override void OnShutdown() { }

        private string PartName() => data?.OwningPartData?.partName ?? gameObject.name;
    }
}
