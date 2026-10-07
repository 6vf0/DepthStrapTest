namespace Bloxstrap.Roblox
{
    internal static class FastFlagImport
    {
        public static Dictionary<string, string> Parse(string json)
        {
            if (json.Length > 2_000_000) throw new InvalidDataException("Import is larger than 2 MB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a JSON object of flag names and values.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                ValidateName(entry.Name);
                string value = entry.Value.ValueKind switch
                {
                    JsonValueKind.String => entry.Value.GetString()!,
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => entry.Value.ToString(),
                    _ => throw new InvalidDataException($"{entry.Name}: use a string, number or boolean value.")
                };
                if (value.Length > 4096) throw new InvalidDataException($"{entry.Name}: value is too long.");
                if (!result.TryAdd(entry.Name, value)) throw new InvalidDataException($"Duplicate flag: {entry.Name}");
                if (result.Count > 10_000) throw new InvalidDataException("Import contains more than 10,000 flags.");
            }
            return result;
        }
        public static void ValidateName(string name)
        {
            if (!Regex.IsMatch(name, @"\A(?:D|S)?F(?:Flag|Int|String|Log)[A-Za-z0-9_]{1,180}\z"))
                throw new InvalidDataException("Use a full FastFlag name, for example FFlagDebugSkyGray.");
        }
    }
}
