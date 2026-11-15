using System;
using HarmonyLib;
using KSP.Sim.impl;
using KSP.VFX.Reentry;
using KSP1PartMod.Animation.Settings;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Redux 26w46a dropped the generated reentry hull: <c>FXReentryContextualManager.CollectPartGlow</c> gives
    /// each part without a <c>ReentryMesh</c> a child "Reentry glow" (<c>ReentryMesh.FindGlowRenderers(PartBehavior)</c>)
    /// that writes a property block, without reading it, onto the part's own renderers every <c>LateUpdate</c> while
    /// active. That object has no <c>ReentryGroupData</c> and is made after our bind, so a postfix on
    /// <c>FindGlowRenderers</c> tells the part's <see cref="Module_Ksp1Animations"/> to rewrite its renderers every frame
    /// while the glow is active. 26w42c has no such method: nothing is patched there. Never throws.</summary>
    internal static class ReentryGlowWatch
    {
        public const string HarmonyId = "KSP1PartMod.ReentryGlowWatch";
        private static bool _broken;

        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.Enabled) return false;
            var find = AccessTools.Method(typeof(ReentryMesh), "FindGlowRenderers", new[] { typeof(PartBehavior) });
            if (find == null)
            {
                KSP1PartModPlugin.Log?.LogInfo("[KSP1PartMod] anim: reentry glow watch not armed (no ReentryMesh.FindGlowRenderers in this Redux)");
                return false;
            }
            harmony.Patch(find, postfix: new HarmonyMethod(typeof(ReentryGlowWatch), nameof(AfterFindGlowRenderers)));
            KSP1PartModPlugin.Log?.LogInfo("[KSP1PartMod] anim: reentry glow watch armed");
            return true;
        }

        internal static void AfterFindGlowRenderers(ReentryMesh __instance, PartBehavior __0)
        {
            if (_broken || __instance == null || __0 == null) return;
            try
            {
                var anim = __0.GetComponentInChildren<Module_Ksp1Animations>(true);
                if (anim != null) anim.WatchGlow(__instance.gameObject);
            }
            catch (Exception e)
            {
                _broken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] anim: reentry glow watch failed, stopped for this session: " + e);
            }
        }
    }
}
