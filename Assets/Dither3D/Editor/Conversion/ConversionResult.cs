using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds the deterministic conversion output and diagnostic messages.
/// </summary>
public class ConversionResult
{
    readonly List<string> warnings = new List<string>();
    readonly List<string> errors = new List<string>();

    /// <summary>
    /// True when no conversion errors were produced.
    /// </summary>
    public bool Success => errors.Count == 0;

    /// <summary>
    /// Converted material instance produced by the converter.
    /// </summary>
    public Material ConvertedMaterial { get; internal set; }

    /// <summary>
    /// Persisted asset path when conversion was saved as an asset.
    /// </summary>
    public string OutputAssetPath { get; internal set; }

    /// <summary>
    /// Non-fatal warnings produced during conversion.
    /// </summary>
    public IReadOnlyList<string> Warnings => warnings;

    /// <summary>
    /// Fatal conversion errors.
    /// </summary>
    public IReadOnlyList<string> Errors => errors;

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
