using System;
using System.Collections.Generic;

namespace BTCPayTranslator.Models;

public record ManifestEntry(
    string Code, // "fr"
    string Bcp47, // "fr-FR"
    string Name, // "French"
    string Native, // "Français"
    bool Rtl, // right-to-left indicator
    string File, // "translations/french.json"
    string Sha, // "abc123..."
    string? Maintainer, // "teamssUTXO|https://github.com/teamssUTXO"
    string Updated // 2026-04-12T10:30:00Z
);

public record Manifest(
    List<ManifestEntry> Languages,
    string? Redirect
);