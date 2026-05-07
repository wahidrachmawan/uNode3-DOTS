using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MaxyGames.UNode.Editors.Control {
    public class DefaultCommandBufferFieldControl : FieldControl {
		public override bool IsValidControl(Type type, bool layouted) {
			return type == typeof(DefaultCommandBufferData);
		}

		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, uNodeUtility.EditValueSettings settings) {
			EditorGUI.BeginChangeCheck();
			if(value == null) {
				value = default(DefaultCommandBufferData);
				GUI.changed = true;
			}
			var data = (DefaultCommandBufferData)value;
			data.kind = (DefaultCommandBufferType)EditorGUI.EnumPopup(position, label, data.kind);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(data);
			}
		}
	}
}