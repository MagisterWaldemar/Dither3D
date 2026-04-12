using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PainterlyValidationSceneEntry
{
    public string label;
    public string category;
    public string scenePath;
    public string cameraName = "Main Camera";
    [TextArea] public string notes;
    public List<string> materialPaths = new List<string>();
}

[CreateAssetMenu(fileName = "PainterlyValidationSceneSet", menuName = "Dither 3D/Painterly Validation Scene Set")]
public class PainterlyValidationSceneSet : ScriptableObject
{
    public List<PainterlyValidationSceneEntry> scenes = new List<PainterlyValidationSceneEntry>();
    public List<string> sharedMaterialPaths = new List<string>();
}
