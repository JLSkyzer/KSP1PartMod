namespace KSP1PartMod.Placement
{
    /// <summary>What the tech placement needs to know about one KSP1 part config.</summary>
    public sealed class Ksp1PartInfo
    {
        public string PartName { get; }
        public string ModName { get; }
        /// <summary>Trimmed, comment stripped; null if absent.</summary>
        public string TechRequired { get; }
        /// <summary>Trimmed, comment stripped; null if absent.</summary>
        public string Category { get; }
        /// <summary>"0v","1v","1.5v","2v","3v","4v","mk2","mk3","srf" or null.</summary>
        public string Size { get; }
        public bool HiddenByConfig { get; }

        public Ksp1PartInfo(string partName, string modName, string techRequired, string category, string size, bool hiddenByConfig)
        {
            PartName = partName;
            ModName = modName;
            TechRequired = techRequired;
            Category = category;
            Size = size;
            HiddenByConfig = hiddenByConfig;
        }
    }
}
