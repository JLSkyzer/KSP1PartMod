using System;
using KSP.Sim.Definitions;
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

        protected override void AddDataModules()
        {
            base.AddDataModules();
            DataModules.TryAddUnique(data, out data);
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                if (data == null)
                {
                    KSP1PartModPlugin.Log.LogWarning($"[KSP1PartMod] anim: {PartName()} module has no data");
                    return;
                }
                data.SetVisible(data.State, false);
                var n = data.Entries?.Count ?? 0;
                KSP1PartModPlugin.Log.LogInfo(
                    $"[KSP1PartMod] anim: {PartName()} module present ({PartBackingMode}) with {n} {(n == 1 ? "entry" : "entries")}");
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError($"[KSP1PartMod] anim: {PartName()} module init failed: {e}");
            }
        }

        protected override void OnModuleUpdate(float deltaTime) { }

        protected override void OnModuleOABUpdate(float deltaTime) { }

        protected override void OnShutdown() { }

        private string PartName() => data?.OwningPartData?.partName ?? gameObject.name;
    }
}
