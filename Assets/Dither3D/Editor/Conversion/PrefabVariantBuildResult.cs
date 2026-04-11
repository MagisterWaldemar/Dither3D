using System.Collections.Generic;

/// <summary>
/// Holds variant generation output and diagnostics.
/// </summary>
public class PrefabVariantBuildResult
{
    readonly List<PrefabMaterialReplacement> replacements = new List<PrefabMaterialReplacement>();
    readonly List<PrefabMaterialSkip> skippedSlots = new List<PrefabMaterialSkip>();
    readonly List<string> warnings = new List<string>();
    readonly List<string> errors = new List<string>();

    /// <summary>
    /// True when variant generation succeeded with no fatal errors.
    /// </summary>
    public bool Success => errors.Count == 0 && !string.IsNullOrEmpty(VariantAssetPath);

    /// <summary>
    /// Persisted prefab variant asset path.
    /// </summary>
    public string VariantAssetPath { get; internal set; }

    /// <summary>
    /// Converted material replacements applied to renderer slots.
    /// </summary>
    public IReadOnlyList<PrefabMaterialReplacement> Replacements => replacements;

    /// <summary>
    /// Renderer slots that were intentionally skipped.
    /// </summary>
    public IReadOnlyList<PrefabMaterialSkip> SkippedSlots => skippedSlots;

    /// <summary>
    /// Non-fatal warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings => warnings;

    /// <summary>
    /// Fatal build errors.
    /// </summary>
    public IReadOnlyList<string> Errors => errors;

    internal void AddReplacement(PrefabMaterialReplacement replacement)
    {
        if (replacement != null)
            replacements.Add(replacement);
    }

    internal void AddSkippedSlot(PrefabMaterialSkip skipped)
    {
        if (skipped != null)
            skippedSlots.Add(skipped);
    }

    internal void AddWarning(string message)
    {
        if (!string.IsNullOrEmpty(message))
            warnings.Add(message);
    }

    internal void AddError(string message)
    {
        if (!string.IsNullOrEmpty(message))
            errors.Add(message);
    }
}

/// <summary>
/// Describes a successful material replacement for one renderer slot.
/// </summary>
public class PrefabMaterialReplacement
{
    public PrefabMaterialReplacement(string rendererPath, int slotIndex, string sourceMaterialPath, string convertedMaterialPath)
    {
        RendererPath = rendererPath;
        SlotIndex = slotIndex;
        SourceMaterialPath = sourceMaterialPath;
        ConvertedMaterialPath = convertedMaterialPath;
    }

    public string RendererPath { get; }
    public int SlotIndex { get; }
    public string SourceMaterialPath { get; }
    public string ConvertedMaterialPath { get; }
}

/// <summary>
/// Describes a skipped material slot and the reason.
/// </summary>
public class PrefabMaterialSkip
{
    public PrefabMaterialSkip(string rendererPath, int slotIndex, string reason)
    {
        RendererPath = rendererPath;
        SlotIndex = slotIndex;
        Reason = reason;
    }

    public string RendererPath { get; }
    public int SlotIndex { get; }
    public string Reason { get; }
}
