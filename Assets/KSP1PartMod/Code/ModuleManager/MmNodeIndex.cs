namespace KSP1PartMod.ModuleManager
{
    /// <summary>The index of a child node patch (<c>@MODULE[X]:HAS[…],1</c>) and which matches an index picks.</summary>
    public static class MmNodeIndex
    {
        /// <summary>Splits the last top-level <c>,n</c> / <c>,-n</c> / <c>,*</c> off a node name. A comma inside brackets
        /// is not an index; text after a top-level comma that is not an index leaves the name unchanged (false).</summary>
        public static bool Split(string childName, out string name, out MmIndex index)
        {
            name = childName ?? "";
            index = MmIndex.None;
            var depth = 0;
            var comma = -1;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '[') depth++;
                else if (c == ']') { if (depth > 0) depth--; }
                else if (c == ',' && depth == 0) comma = i;
            }
            if (comma < 0 || !MmIndex.TryParse(name, comma + 1, name.Length, out index))
            {
                index = MmIndex.None;
                return false;
            }
            name = name.Substring(0, comma).TrimEnd();
            return true;
        }

        /// <summary>The matches to touch among <paramref name="count"/>, in order: no index, all (or the first with
        /// <paramref name="firstMatchDefault"/>); <c>n</c> the n-th; <c>-n</c> the n-th from the end; <c>*</c> all. An
        /// explicit index past the matches is <see cref="MmPick.OutOfRange"/>.</summary>
        public static MmPick Pick(int count, MmIndex index, bool firstMatchDefault)
        {
            if (count < 0) count = 0;
            switch (index.Kind)
            {
                case MmIndexKind.All:
                    return new MmPick(0, count, false);
                case MmIndexKind.N:
                    return index.Value < count ? new MmPick(index.Value, 1, false) : new MmPick(0, 0, true);
                case MmIndexKind.FromEnd:
                    return index.Value >= 1 && index.Value <= count
                        ? new MmPick(count - index.Value, 1, false)
                        : new MmPick(0, 0, true);
                default:
                    return new MmPick(0, firstMatchDefault && count > 0 ? 1 : count, false);
            }
        }
    }
}
