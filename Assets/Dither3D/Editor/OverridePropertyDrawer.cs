/*
 * Copyright (c) 2025 Rune Skovbo Johansen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(PropertyAttribute), true)]
public class OverridePropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (attribute == null || attribute.GetType().Name != "OverridePropertyAttribute")
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        SerializedProperty overrideProp =
            property.serializedObject.FindProperty(property.propertyPath + "Override");
        if (overrideProp == null || overrideProp.propertyType != SerializedPropertyType.Boolean)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        Rect posToggle = position;
        posToggle.width = 16;
        EditorGUI.PropertyField(posToggle, overrideProp, GUIContent.none);

        using (new EditorGUI.DisabledScope(!overrideProp.boolValue))
        {
            position.xMin += 16;
            EditorGUIUtility.labelWidth -= 16;
            EditorGUI.PropertyField(position, property, label);
            EditorGUIUtility.labelWidth = 0;
        }
    }
}
