using System;
using System.Collections.Generic;
using System.Linq;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>
    /// Turns a relationship's cascade settings into words.
    ///
    /// "What happens to the children when I delete this?" is one of the most
    /// common questions asked of a data model, and the answer lives in metadata
    /// nobody reads. Makers know the named behaviours (Parental, Referential);
    /// everyone else needs the sentence, so both are produced.
    /// </summary>
    public static class CascadeText
    {
        /// <summary>The behaviour name a maker would recognise, or null.</summary>
        public static string Name(CascadeModel c)
        {
            if (c == null) return null;

            bool othersStay = Is(c.Assign, "NoCascade") && Is(c.Share, "NoCascade") &&
                              Is(c.Unshare, "NoCascade") && Is(c.Reparent, "NoCascade");

            if (Is(c.Delete, "Cascade") && Is(c.Assign, "Cascade") && Is(c.Share, "Cascade") &&
                Is(c.Unshare, "Cascade") && Is(c.Reparent, "Cascade"))
                return "Parental";
            if (Is(c.Delete, "RemoveLink") && othersStay) return "Referential";
            if (Is(c.Delete, "Restrict") && othersStay) return "Referential, restrict delete";
            return "Configurable cascading";
        }

        /// <summary>
        /// One or two sentences describing what actions on the parent do to the
        /// child records. Plural phrasing throughout, so no table name ever
        /// needs an "a"/"an" that its display name might not fit.
        /// </summary>
        public static string Describe(CascadeModel c, string parent, string child, string lookup)
        {
            if (c == null) return null;

            var parts = new List<string>();

            var delete = DeleteSentence(c.Delete, parent, child, lookup);
            if (delete != null) parts.Add(delete);

            var others = OtherActionsSentence(c, child);
            if (others != null) parts.Add(others);

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        private static string DeleteSentence(string delete, string parent, string child, string lookup)
        {
            switch (delete)
            {
                case "Cascade":
                    return $"Deleting {parent} records also deletes their related {child} records.";
                case "RemoveLink":
                    return string.IsNullOrEmpty(lookup)
                        ? $"Deleting {parent} records clears the lookup on related {child} records, which are kept."
                        : $"Deleting {parent} records clears the {lookup} lookup on related {child} records, which are kept.";
                case "Restrict":
                    return $"{parent} records cannot be deleted while related {child} records still reference them.";
                case "NoCascade":
                    return $"Deleting {parent} records does not affect related {child} records.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Groups assign/share/unshare/reparent by what they do, so a typical
        /// relationship reads as one clause rather than four.
        /// </summary>
        private static string OtherActionsSentence(CascadeModel c, string child)
        {
            var actions = new[]
            {
                new KeyValuePair<string, string>("assign", c.Assign),
                new KeyValuePair<string, string>("share", c.Share),
                new KeyValuePair<string, string>("unshare", c.Unshare),
                new KeyValuePair<string, string>("reparent", c.Reparent)
            }.Where(a => !string.IsNullOrEmpty(a.Value)).ToList();
            if (actions.Count == 0) return null;

            var clauses = new List<string>();
            foreach (var group in actions.GroupBy(a => a.Value))
            {
                var effect = Effect(group.Key, child);
                if (effect == null) continue;
                clauses.Add(JoinWords(group.Select(g => g.Key).ToList()) + " " + effect);
            }
            if (clauses.Count == 0) return null;

            var sentence = string.Join("; ", clauses);
            return char.ToUpperInvariant(sentence[0]) + sentence.Substring(1) + ".";
        }

        private static string Effect(string cascade, string child)
        {
            switch (cascade)
            {
                case "Cascade": return $"cascade to all related {child} records";
                case "Active": return $"cascade to active related {child} records";
                case "UserOwned": return $"cascade to related {child} records owned by the same user";
                case "NoCascade": return $"do not cascade to related {child} records";
                default: return null;
            }
        }

        private static string JoinWords(List<string> words)
        {
            if (words.Count == 1) return words[0];
            return string.Join(", ", words.Take(words.Count - 1)) + " and " + words[words.Count - 1];
        }

        private static bool Is(string value, string expected)
            => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
