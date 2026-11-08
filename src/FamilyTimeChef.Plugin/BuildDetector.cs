using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FamilyTimeChef.Plugin
{
    /// <summary>
    /// Identifies exactly which of the supported Family Time installations is running.
    /// Identity comes from the Unity build-guid in boot.config plus the managed assembly
    /// hash, so DX11/DX12 variants of the same episode are distinguished reliably.
    /// </summary>
    internal static class BuildDetector
    {
        internal sealed class Identity
        {
            public int Episode;             // 1..3, or 0 when unknown
            public string EpisodeLabel = "Unknown";
            public string BuildLabel = "unknown";
            public string Renderer = "unknown";
            public string BuildGuid = string.Empty;
            public string AssemblyHash = string.Empty;
            public string DataPath = string.Empty;
            public bool Supported;
            public string Note = string.Empty;
        }

        // Verified by hashing boot.config across the five target installations.
        private static readonly string[][] Known =
        {
            new[] { "8d3d19ebbeee47fe87a8b50e94d811ac", "3", "b108 (DX11)" },
            new[] { "fa09aa9aa9304331adf97799fc2522a8", "3", "b108 (DX12)" },
            new[] { "7f4d0cabd2c94f808e9faee9b9cc936a", "2", "Build 37 (DX12)" },
            new[] { "c88cb84d51b0427a8e7b5a6dc9b42841", "2", "Build 37 (DX11)" },
            new[] { "482c8d266eb940c5b056e0427413ede0", "1", "Patreon" },
        };

        public static Identity Detect()
        {
            var id = new Identity();
            try
            {
                id.DataPath = Application.dataPath;
                id.Renderer = SystemInfo.graphicsDeviceType.ToString();

                string boot = Path.Combine(id.DataPath, "boot.config");
                if (File.Exists(boot))
                {
                    foreach (var raw in File.ReadAllLines(boot))
                    {
                        var line = raw.Trim();
                        if (line.StartsWith("build-guid", StringComparison.OrdinalIgnoreCase))
                        {
                            int eq = line.IndexOf('=');
                            if (eq >= 0) id.BuildGuid = line.Substring(eq + 1).Trim();
                        }
                    }
                }

                string asm = Path.Combine(id.DataPath, "Managed", "Assembly-CSharp.dll");
                if (File.Exists(asm))
                {
                    id.AssemblyHash = HashFile(asm);
                }

                foreach (var row in Known)
                {
                    if (string.Equals(row[0], id.BuildGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        id.Episode = int.Parse(row[1]);
                        id.BuildLabel = row[2];
                        id.Supported = true;
                        id.EpisodeLabel = "Episode " + id.Episode;
                        return id;
                    }
                }

                id.EpisodeLabel = "Unsupported build";
                id.Note = string.IsNullOrEmpty(id.BuildGuid)
                    ? "boot.config did not contain a build-guid; this is not a Family Time build we recognise."
                    : "build-guid " + id.BuildGuid + " is not one of the supported Episode 1/2/3 builds. " +
                      "The tool will stay read-only rather than risk writing into an unknown layout.";
            }
            catch (Exception ex)
            {
                id.Note = "Detection failed: " + ex.Message;
            }
            return id;
        }

        /// <summary>
        /// FNV-1a over the raw file bytes. This runtime lacks
        /// HashAlgorithm.ComputeHash(Stream), and a diagnostic fingerprint does not need
        /// a cryptographic hash.
        /// </summary>
        public static string HashFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                ulong h = 14695981039346656037UL;
                foreach (var b in bytes)
                {
                    h ^= b;
                    h *= 1099511628211UL;
                }
                return h.ToString("x16");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
