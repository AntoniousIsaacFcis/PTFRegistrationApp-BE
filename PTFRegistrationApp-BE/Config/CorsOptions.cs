using System;
using System.Collections.Generic;
using System.Linq;

namespace PTFRegistrationApp_BE.Config;

public class CorsOptions
{
    public const string SectionName = "Cors";

    public string AllowedOriginsRaw { get; set; } = string.Empty;

    public IReadOnlyCollection<string> AllowedOrigins =>
        AllowedOriginsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
