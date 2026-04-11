using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Asset containing source-to-target shader adapter mappings.
/// </summary>
[CreateAssetMenu(fileName = "ShaderAdapterRegistry", menuName = "Dither 3D/Conversion/Shader Adapter Registry")]
public class ShaderAdapterRegistry : ScriptableObject
{
    [SerializeField]
    List<ShaderAdapterMapping> shaderMappings = new List<ShaderAdapterMapping>();

    /// <summary>
    /// Shader mapping entries used by the conversion pipeline.
    /// </summary>
    public IReadOnlyList<ShaderAdapterMapping> ShaderMappings => shaderMappings;
}
