using UnityEngine;

/// <summary>
/// Stores high-level dither conversion style settings and references.
/// </summary>
[CreateAssetMenu(fileName = "DitherStyleProfile", menuName = "Dither 3D/Conversion/Dither Style Profile")]
public class DitherStyleProfile : ScriptableObject
{
    [SerializeField]
    string profileName = "Default";

    [SerializeField]
    ShaderAdapterRegistry shaderAdapterRegistry;

    [SerializeField]
    string notes;

    /// <summary>
    /// User-facing style profile name.
    /// </summary>
    public string ProfileName => profileName;

    /// <summary>
    /// Registry that maps source shaders and material properties.
    /// </summary>
    public ShaderAdapterRegistry ShaderAdapterRegistry => shaderAdapterRegistry;

    /// <summary>
    /// Optional notes for the style profile.
    /// </summary>
    public string Notes => notes;
}
