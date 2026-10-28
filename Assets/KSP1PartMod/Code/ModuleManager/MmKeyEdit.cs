namespace KSP1PartMod.ModuleManager
{
    /// <summary>One value line of a ModuleManager patch, read from the name before its first <c>=</c> (Redux's split,
    /// NEEDS already removed) and its value. Grammar (plan (c), MM's public wiki): command, key, an optional top-level
    /// index (<c>,n</c>, <c>,-n</c>, <c>,*</c>) and an optional trailing operator (<c>*</c>, <c>+</c>, <c>-</c>,
    /// <c>/</c>, counted only: <c>!</c>, <c>^</c>), with or without a space. Operators are read on edit and
    /// create-or-edit lines, indices on edit, create-or-edit and delete lines. Anything else is "passed back": its
    /// <see cref="Key"/> is then Redux's observed base name (cut at the first bracket, comma or colon) and it is
    /// applied with Redux's plain semantics.</summary>
    public readonly struct MmKeyEdit
    {
        public readonly MmCommand Command;
        /// <summary>The key, or Redux's base name for a passed-back line. Empty: Redux skips the line.</summary>
        public readonly string Key;
        public readonly MmIndex Index;
        public readonly MmOperator Operator;
        /// <summary>The raw value: the new value, or the operand.</summary>
        public readonly string Value;
        /// <summary>We did not read the name; <see cref="Key"/> is Redux's base name.</summary>
        public readonly bool PassedBack;
        /// <summary>The name holds a bracket (a selector or MM's vector component <c>key[1,,]</c>, not handled).</summary>
        public readonly bool HasBrackets;
        /// <summary>The trailing operator of a passed-back line with brackets (MM's vector component,
        /// <c>@key[1,,] *= x</c>); <see cref="MmOperator.None"/> otherwise. Such a line is skipped
        /// (<see cref="SkippedVectorEdit"/>).</summary>
        public readonly MmOperator BracketOperator;

        private MmKeyEdit(MmCommand command, string key, MmIndex index, MmOperator op, string value, bool passedBack,
            bool hasBrackets, MmOperator bracketOperator = MmOperator.None)
        {
            BracketOperator = bracketOperator;
            Command = command;
            Key = key;
            Index = index;
            Operator = op;
            Value = value;
            PassedBack = passedBack;
            HasBrackets = hasBrackets;
        }

        /// <summary>An operator we apply (<c>*= += -= /=</c>) or an explicit index: the line needs us.</summary>
        public bool IsOurs => !PassedBack && (IsSupported(Operator) || Index.Kind != MmIndexKind.None && !CountedOnly);

        /// <summary>A vector-component edit with an operator (<c>@key[1,,] *= x</c>): skipped, neither applied nor left to
        /// Redux, which would write the raw operand over every value of the key. Counted.</summary>
        public bool SkippedVectorEdit => PassedBack && HasBrackets && BracketOperator != MmOperator.None;

        /// <summary><c>!=</c> or <c>^=</c>: recognised, counted, never applied.</summary>
        public bool CountedOnly => Operator == MmOperator.Pow || Operator == MmOperator.Regex;

        public static bool IsSupported(MmOperator op) =>
            op == MmOperator.Mul || op == MmOperator.Add || op == MmOperator.Sub || op == MmOperator.Div;

        public static MmCommand CommandOf(char c)
        {
            switch (c)
            {
                case '@': return MmCommand.Edit;
                case '%': return MmCommand.CreateOrEdit;
                case '+': return MmCommand.Copy;
                case '!':
                case '-': return MmCommand.Delete;
                case '&': return MmCommand.Insert;
                default: return MmCommand.None;
            }
        }

        private static MmOperator OperatorOf(char c)
        {
            switch (c)
            {
                case '*': return MmOperator.Mul;
                case '+': return MmOperator.Add;
                case '-': return MmOperator.Sub;
                case '/': return MmOperator.Div;
                case '!': return MmOperator.Pow;
                case '^': return MmOperator.Regex;
                default: return MmOperator.None;
            }
        }

        /// <summary>A cheap test, with no allocation, for the common path of a patch: true when the name is an edit,
        /// create-or-edit or delete line that ends with an operator character or holds a comma, so
        /// <see cref="Parse"/> may find an operator or an index.</summary>
        public static bool MightNeedUs(string rawName)
        {
            if (rawName == null) return false;
            int start = 0, end = rawName.Length;
            while (start < end && char.IsWhiteSpace(rawName[start])) start++;
            while (end > start && char.IsWhiteSpace(rawName[end - 1])) end--;
            if (end - start < 2) return false;
            var command = CommandOf(rawName[start]);
            if (command != MmCommand.Edit && command != MmCommand.CreateOrEdit && command != MmCommand.Delete) return false;
            // a delete line has no operator (a trailing '*' there is a wildcard, left to Redux)
            if (command != MmCommand.Delete && OperatorOf(rawName[end - 1]) != MmOperator.None) return true;
            return rawName.IndexOf(',', start + 1, end - start - 1) >= 0;
        }

        public static MmKeyEdit Parse(string rawName, string rawValue)
        {
            var name = (rawName ?? "").Trim();
            var command = name.Length > 0 ? CommandOf(name[0]) : MmCommand.None;
            var rest = command == MmCommand.None ? name : name.Substring(1).Trim();
            var brackets = rest.IndexOf('[') >= 0 || rest.IndexOf(']') >= 0;
            var readable = (command == MmCommand.Edit || command == MmCommand.CreateOrEdit || command == MmCommand.Delete)
                           && !brackets && rest.IndexOf(':') < 0 && rest.Length > 0;
            if (readable && TryRead(rest, command, out var key, out var index, out var op))
                return new MmKeyEdit(command, key, index, op, rawValue, false, false);
            var baseName = ReduxBaseName(rest);
            var bracketOperator = brackets && (command == MmCommand.Edit || command == MmCommand.CreateOrEdit)
                ? TrailingOperator(rest)
                : MmOperator.None;
            return new MmKeyEdit(command, baseName, MmIndex.None, MmOperator.None, rawValue, baseName != rest, brackets,
                bracketOperator);
        }

        /// <summary>The line as Redux reads it, for a line whose feature is switched off: command from the first character,
        /// <see cref="Key"/> = <see cref="ReduxBaseName"/>, no index, no operator, never skipped. Applied by
        /// <see cref="MmValueEditor"/>, it gives Redux's plain result (<c>@mass *</c> edits nothing,
        /// <c>@mass,0 *</c> writes the operand over every <c>mass</c>).</summary>
        public static MmKeyEdit AsRedux(string rawName, string rawValue)
        {
            var name = (rawName ?? "").Trim();
            var command = name.Length > 0 ? CommandOf(name[0]) : MmCommand.None;
            var rest = command == MmCommand.None ? name : name.Substring(1).Trim();
            return new MmKeyEdit(command, ReduxBaseName(rest), MmIndex.None, MmOperator.None, rawValue, true,
                rest.IndexOf('[') >= 0 || rest.IndexOf(']') >= 0);
        }

        /// <summary>The operator character ending the name, unless it is the <c>*</c> of a <c>,*</c> index.</summary>
        private static MmOperator TrailingOperator(string rest)
        {
            if (rest.Length == 0) return MmOperator.None;
            var op = OperatorOf(rest[rest.Length - 1]);
            if (op != MmOperator.Mul) return op;
            var before = rest.Length - 1;
            while (before > 0 && char.IsWhiteSpace(rest[before - 1])) before--;
            return before > 0 && rest[before - 1] == ',' ? MmOperator.None : op;
        }

        /// <summary>Key, index and operator of a name without brackets or colon.</summary>
        private static bool TryRead(string rest, MmCommand command, out string key, out MmIndex index, out MmOperator op)
        {
            key = null;
            index = MmIndex.None;
            op = MmOperator.None;
            var end = rest.Length;
            if (command != MmCommand.Delete)
            {
                var last = OperatorOf(rest[end - 1]);
                if (last != MmOperator.None)
                {
                    var before = end - 1;
                    while (before > 0 && char.IsWhiteSpace(rest[before - 1])) before--;
                    // "key,*" is the index "all", not a product
                    if (!(last == MmOperator.Mul && before > 0 && rest[before - 1] == ','))
                    {
                        op = last;
                        end = before;
                    }
                }
            }
            var comma = rest.LastIndexOf(',', end - 1 < 0 ? 0 : end - 1);
            if (end <= 0) return false;
            var keyEnd = end;
            if (comma >= 0)
            {
                if (!MmIndex.TryParse(rest, comma + 1, end, out index)) return false;
                keyEnd = comma;
            }
            key = rest.Substring(0, keyEnd).Trim();
            if (key.Length == 0 || key.IndexOf(',') >= 0) return false;
            for (var i = 0; i < key.Length; i++)
                if (char.IsWhiteSpace(key[i])) return false;
            return true;
        }

        /// <summary>The base name Redux reads from a value name (observed behaviour, plan (a)): the text before the first
        /// bracket, then before the first comma, then before the first colon, trimmed.</summary>
        public static string ReduxBaseName(string rest)
        {
            var s = rest ?? "";
            var cut = s.IndexOf('[');
            if (cut >= 0) s = s.Substring(0, cut);
            cut = s.IndexOf(',');
            if (cut >= 0) s = s.Substring(0, cut);
            cut = s.IndexOf(':');
            if (cut >= 0) s = s.Substring(0, cut);
            return s.Trim();
        }

        public override string ToString() =>
            $"{Command} '{Key}'{Index} {Operator}{(PassedBack ? " (passed back)" : "")} = {Value}";
    }
}
