using System;
using System.Collections.Generic;

/// <summary>
/// Serializable report for prefab conversion runs.
/// </summary>
[Serializable]
public class ConversionManifest
{
    public bool dryRun;
    public string generatedAtUtc;
    public string styleProfile;
    public string styleProfileAssetPath;
    public string adapterRegistry;
    public string adapterRegistryAssetPath;
    public ConversionManifestSummary summary = new ConversionManifestSummary();
    public List<ConversionManifestEntry> entries = new List<ConversionManifestEntry>();
}

/// <summary>
/// Aggregate conversion counts.
/// </summary>
[Serializable]
public class ConversionManifestSummary
{
    public int totalPrefabs;
    public int successfulPrefabs;
    public int prefabsWithWarnings;
    public int prefabsWithErrors;
    public int totalReplacements;
    public int totalSkippedSlots;
}

/// <summary>
/// One manifest entry describing a prefab, replacement, or skipped slot.
/// </summary>
[Serializable]
public class ConversionManifestEntry
{
    public string entryType;
    public string source;
    public string output;
    public string adapterUsed;
    public string context;
    public string timestampUtc;
    public List<string> warnings = new List<string>();
    public List<string> errors = new List<string>();
}
