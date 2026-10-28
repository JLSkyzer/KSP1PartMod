using System;
using System.IO;

namespace KSP1PartMod.Animation.Textures
{
    /// <summary>How a texture file was loaded by Redux's KSP1 importer. Redux loads a file as linear (and, for a bitmap,
    /// swizzled to DXT5nm) when it is asked as a normal map or its name ends in <c>NRM</c>; otherwise as sRGB colour.
    /// Redux's own cache is keyed by path only, so the first usage wins; sharing across part rebuilds never serves a
    /// texture loaded under the other usage.</summary>
    public enum TextureUsageClass
    {
        Color,
        Normal,
    }

    /// <summary>What the <c>LoadTexture</c> prefix does with Redux's cache entry for the resolved path.</summary>
    public enum CacheLookup
    {
        /// <summary>Leave Redux alone (sharing inactive, or no entry: Redux loads the file).</summary>
        RunRedux,
        /// <summary>Hand the cached texture to the caller, skipping Redux's method.</summary>
        Share,
        /// <summary>The cached texture was destroyed: drop the entry, then Redux loads the file.</summary>
        EvictDestroyed,
        /// <summary>The cached texture was loaded under the other usage: drop the entry
        /// (the texture is not destroyed, the parts using it keep it), then Redux loads the file for this usage.</summary>
        EvictOtherUsage,
    }

    /// <summary>What <c>ClearCache(destroyTextures)</c> does.</summary>
    public enum CacheClear
    {
        /// <summary>Redux's own clear (destroying the textures when asked).</summary>
        Redux,
        /// <summary>Drop only the entries whose texture is destroyed; the rest stays shared.</summary>
        DropDestroyedOnly,
    }

    /// <summary>The rules of KSP1 texture sharing across part rebuilds (<c>share_ksp1_textures</c>). Sharing is active
    /// only once Redux's import is complete and outside <c>ReleaseResidentPrefabs</c>, which frees every prefab and its
    /// textures and must keep doing so.</summary>
    public static class TextureShareRule
    {
        /// <summary>Redux's <c>LooksLikeNormalMapPath</c>: the file name without extension ends in NRM, any case.</summary>
        public static bool LooksLikeNormalMap(string path) =>
            !string.IsNullOrEmpty(path) &&
            Path.GetFileNameWithoutExtension(path).EndsWith("NRM", StringComparison.OrdinalIgnoreCase);

        public static TextureUsageClass Classify(string resolvedPath, bool normalRequested) =>
            normalRequested || LooksLikeNormalMap(resolvedPath) ? TextureUsageClass.Normal : TextureUsageClass.Color;

        public static bool SharingActive(bool importComplete, bool releasing) => importComplete && !releasing;

        /// <param name="recorded">The usage the cached texture was loaded under, null when it was loaded before sharing
        /// started (during Redux's import, whose own rule is "first usage wins": it is shared as Redux would).</param>
        public static CacheLookup Lookup(bool sharingActive, bool hasEntry, bool entryDestroyed,
            TextureUsageClass? recorded, TextureUsageClass requested)
        {
            if (!sharingActive || !hasEntry) return CacheLookup.RunRedux;
            if (entryDestroyed) return CacheLookup.EvictDestroyed;
            return recorded == null || recorded == requested ? CacheLookup.Share : CacheLookup.EvictOtherUsage;
        }

        /// <summary>A destroying clear (Redux's <c>Clear</c>) always runs as Redux wrote it.</summary>
        public static CacheClear Clear(bool destroyTextures, bool sharingActive) =>
            !destroyTextures && sharingActive ? CacheClear.DropDestroyedOnly : CacheClear.Redux;

        /// <summary><c>DestroyPrefabGraph</c> (Redux's temporary icon prefab) keeps its textures while sharing: they are
        /// in the shared cache and live parts use them.</summary>
        public static bool KeepTexturesOfDestroyedGraph(bool destroyTextures, bool sharingActive) =>
            destroyTextures && sharingActive;
    }
}
