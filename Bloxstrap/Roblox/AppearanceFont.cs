using System.Text.Json.Nodes;

namespace Bloxstrap.Roblox
{
    internal static class AppearanceFont
    {
        internal static string Store(string source)
        {
            string extension = Validate(source);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
            string directory = Path.Combine(Paths.Base, "Fonts");
            Directory.CreateDirectory(directory);
            string stored = Path.Combine(directory, hash + extension);
            if (!File.Exists(stored)) File.Copy(source, stored);
            return stored;
        }

        private static string Validate(string source)
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            using var stream = File.OpenRead(source);
            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) != 4 || !((extension == ".ttf" && header.SequenceEqual(new byte[] { 0, 1, 0, 0 })) ||
                (extension == ".otf" && header.SequenceEqual("OTTO"u8))))
                throw new InvalidDataException("Choose a valid .ttf or .otf font file.");
            return extension;
        }

        // Feed these files through the normal mod manifest. Reset and build changes then
        // restore Roblox package files through the existing verified package pipeline.
        internal static Dictionary<string, string> CreateFiles(string versionDirectory, string? font,
            IReadOnlyDictionary<string, string> otherMods)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(font)) return result;
            string extension = Validate(font);
            string families = Path.Combine(versionDirectory, "content", "fonts", "families");
            if (!Directory.Exists(families)) return result;
            string staging = Path.Combine(Paths.Cache, "AppearanceFont");
            Directory.CreateDirectory(staging);
            string asset = "DepthStrapCustomFont" + extension;
            result[Path.Combine("content", "fonts", asset)] = font;
            foreach (string installed in Directory.EnumerateFiles(families, "*.json"))
            {
                string relative = Path.GetRelativePath(versionDirectory, installed);
                string input = otherMods.TryGetValue(relative, out string? mod) ? mod : installed;
                try
                {
                    var family = JsonNode.Parse(File.ReadAllText(input));
                    if (family?["faces"] is not JsonArray faces || faces.Count == 0) continue;
                    foreach (var face in faces.OfType<JsonObject>()) face["assetId"] = "rbxasset://fonts/" + asset;
                    string output = Path.Combine(staging, Path.GetFileName(installed));
                    File.WriteAllText(output, family.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    result[relative] = output;
                }
                catch (JsonException ex) { App.Logger.WriteException("AppearanceFont::Family", ex); }
            }
            return result;
        }
    }
}
