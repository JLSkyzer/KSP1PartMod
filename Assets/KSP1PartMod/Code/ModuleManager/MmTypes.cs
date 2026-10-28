namespace KSP1PartMod.ModuleManager
{
    /// <summary>The first character of a ModuleManager name, as Redux maps it (plan (a)): <c>@</c> edit, <c>%</c>
    /// create-or-edit, <c>+</c> copy, <c>!</c>/<c>-</c> delete, <c>&amp;</c> insert, none add.</summary>
    public enum MmCommand { None, Edit, CreateOrEdit, Copy, Delete, Insert }

    /// <summary>An explicit <c>,n</c> / <c>,-n</c> / <c>,*</c> index after a value or node name.</summary>
    public enum MmIndexKind { None, N, FromEnd, All }

    /// <summary>The operator before the <c>=</c> of a value line. <see cref="Pow"/> (<c>!=</c>) and
    /// <see cref="Regex"/> (<c>^=</c>) are recognised and counted, never applied.</summary>
    public enum MmOperator { None, Mul, Add, Sub, Div, Pow, Regex }

    /// <summary>An index: <see cref="Value"/> is n for <see cref="MmIndexKind.N"/> (0-based) and the distance from the
    /// end for <see cref="MmIndexKind.FromEnd"/> (1 = last).</summary>
    public readonly struct MmIndex
    {
        public readonly MmIndexKind Kind;
        public readonly int Value;

        public MmIndex(MmIndexKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }

        public static readonly MmIndex None = new MmIndex(MmIndexKind.None, 0);
        public static readonly MmIndex All = new MmIndex(MmIndexKind.All, 0);

        /// <summary><c>*</c>, a whole number, or <c>-n</c> with n ≥ 1 (spaces around allowed). No allocation.</summary>
        public static bool TryParse(string text, int start, int end, out MmIndex index)
        {
            index = None;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start >= end) return false;
            if (end - start == 1 && text[start] == '*')
            {
                index = All;
                return true;
            }
            var negative = text[start] == '-';
            if (negative) start++;
            if (start >= end || end - start > 9) return false;
            var n = 0;
            for (var i = start; i < end; i++)
            {
                var c = text[i];
                if (c < '0' || c > '9') return false;
                n = n * 10 + (c - '0');
            }
            if (negative)
            {
                if (n == 0) return false;
                index = new MmIndex(MmIndexKind.FromEnd, n);
            }
            else index = new MmIndex(MmIndexKind.N, n);
            return true;
        }

        public override string ToString() =>
            Kind == MmIndexKind.N ? "," + Value : Kind == MmIndexKind.FromEnd ? ",-" + Value : Kind == MmIndexKind.All ? ",*" : "";
    }

    /// <summary>Why a root patch is dropped by <see cref="MmPluginRule.RootPatchDrop"/>.</summary>
    public enum MmDropReason { Keep, DropBlockedFolder, DropForBlocked, DropBeforeAfterBlocked, DropBeforeAfterAbsent }

    /// <summary>A root patch decision and the mod name it is about (null for <see cref="MmDropReason.Keep"/>).</summary>
    public readonly struct MmDrop
    {
        public readonly MmDropReason Reason;
        public readonly string Mod;

        public MmDrop(MmDropReason reason, string mod)
        {
            Reason = reason;
            Mod = mod;
        }

        public static readonly MmDrop Keep = new MmDrop(MmDropReason.Keep, null);
    }

    /// <summary>A contiguous range of matches to touch (<see cref="MmNodeIndex.Pick"/>).</summary>
    public readonly struct MmPick
    {
        public readonly int Start;
        public readonly int Count;
        /// <summary>An explicit index past the matches: nothing is touched.</summary>
        public readonly bool OutOfRange;

        public MmPick(int start, int count, bool outOfRange)
        {
            Start = start;
            Count = count;
            OutOfRange = outOfRange;
        }

        public bool Contains(int ordinal) => ordinal >= Start && ordinal < Start + Count;
    }
}
