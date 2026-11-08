using System;
using System.Collections.Generic;
using System.Text;

namespace FamilyTimeChef.Plugin
{
    /// <summary>
    /// Minimal, dependency-free line protocol shared with the external Studio GUI.
    /// Frames are '|' separated; the first three fields are always kind, id, verb/status.
    /// </summary>
    internal static class Protocol
    {
        public const char Sep = '|';
        public const char Kv = '=';

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(c == Sep || c == '\n' || c == '\r' ? ' ' : c);
            return sb.ToString();
        }

        public static string Build(string kind, string id, string status, IDictionary<string, string> fields)
        {
            var sb = new StringBuilder();
            sb.Append(kind).Append(Sep).Append(id ?? "0").Append(Sep).Append(Clean(status));
            if (fields != null)
            {
                foreach (var kv in fields)
                {
                    sb.Append(Sep).Append(kv.Key).Append(Kv).Append(Clean(kv.Value));
                }
            }
            return sb.ToString();
        }

        public static bool TryParse(string line, out string kind, out string id, out string verb, out Dictionary<string, string> fields)
        {
            kind = id = verb = null;
            fields = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            var parts = line.Trim().Split(Sep);
            if (parts.Length < 2) return false;

            kind = parts[0];
            id = parts[1];
            verb = parts.Length > 2 ? parts[2] : string.Empty;

            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 3; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf(Kv);
                if (eq <= 0) continue;
                fields[parts[i].Substring(0, eq).Trim()] = parts[i].Substring(eq + 1).Trim();
            }
            return true;
        }
    }
}
