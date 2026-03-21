using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace aibot.Scripts.Knowledge;

public sealed class KnowledgeValidator
{
    private static readonly string[] BlockedTextMarkers =
    {
        "ignore previous instructions",
        "system prompt",
        "developer message",
        "tool call",
        "powershell",
        "cmd.exe",
        "bash -c",
        "curl http",
        "wget http",
        "file://",
        "vscode://"
    };

    public KnowledgeValidationResult ValidateJsonFile(string path, bool isCustomFile, int maxCustomFileSize)
    {
        var fileName = Path.GetFileName(path);
        if (!KnowledgeSchema.JsonFiles.TryGetValue(fileName, out var rule))
        {
            return KnowledgeValidationResult.Rejected($"Unsupported knowledge json file: {fileName}");
        }

        var info = new FileInfo(path);
        var limit = isCustomFile ? maxCustomFileSize : KnowledgeSchema.DefaultJsonMaxBytes;
        if (!info.Exists)
        {
            return KnowledgeValidationResult.Rejected($"Knowledge file does not exist: {path}");
        }

        if (info.Length > limit)
        {
            return KnowledgeValidationResult.Rejected($"Knowledge file exceeds size limit: {fileName}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            ValidateJsonElement(document.RootElement, fileName);
            return KnowledgeValidationResult.Accepted(rule);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AiBot.Knowledge] Json validation failed for {fileName}: {ex.Message}");
            return KnowledgeValidationResult.Rejected($"Invalid knowledge json: {fileName}");
        }
    }

    public KnowledgeValidationResult ValidateMarkdownFile(string path, bool isCustomFile, int maxCustomFileSize)
    {
        var fileName = Path.GetFileName(path);
        var info = new FileInfo(path);
        var limit = isCustomFile ? maxCustomFileSize : KnowledgeSchema.DefaultMarkdownMaxBytes;
        if (!info.Exists)
        {
            return KnowledgeValidationResult.Rejected($"Knowledge file does not exist: {path}");
        }

        if (info.Length > limit)
        {
            return KnowledgeValidationResult.Rejected($"Markdown file exceeds size limit: {fileName}");
        }

        var content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return KnowledgeValidationResult.Accepted();
        }

        var normalized = GuideKnowledgeBase.Normalize(content);
        if (BlockedTextMarkers.Any(marker => normalized.Contains(GuideKnowledgeBase.Normalize(marker), StringComparison.OrdinalIgnoreCase)))
        {
            return KnowledgeValidationResult.Rejected($"Markdown contains blocked content: {fileName}");
        }

        if (content.Contains("```", StringComparison.Ordinal) || content.Contains("http://", StringComparison.OrdinalIgnoreCase) || content.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeValidationResult.Rejected($"Markdown contains disallowed code block or URL: {fileName}");
        }

        return KnowledgeValidationResult.Accepted();
    }

    private static void ValidateJsonElement(JsonElement element, string fileName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Length > 128)
                    {
                        throw new InvalidOperationException($"Property name too long in {fileName}");
                    }

                    ValidateJsonElement(property.Value, fileName);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateJsonElement(item, fileName);
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                if (value.Length > KnowledgeSchema.DefaultStringMaxLength)
                {
                    throw new InvalidOperationException($"String value too long in {fileName}");
                }

                var normalized = GuideKnowledgeBase.Normalize(value);
                if (BlockedTextMarkers.Any(marker => normalized.Contains(GuideKnowledgeBase.Normalize(marker), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Blocked content found in {fileName}");
                }
                break;
        }
    }
}

public sealed record KnowledgeValidationResult(bool IsAccepted, string? Reason = null, JsonKnowledgeFileRule? Rule = null)
{
    public static KnowledgeValidationResult Accepted(JsonKnowledgeFileRule? rule = null) => new(true, null, rule);

    public static KnowledgeValidationResult Rejected(string reason) => new(false, reason, null);
}