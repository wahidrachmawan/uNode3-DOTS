using System;
using Unity.Entities;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using Unity.Mathematics;

namespace MaxyGames.UNode.Editors.Control {
	class ECS_FieldControl : FieldControl {
		public override bool IsValidControl(Type type, bool layouted) {
			if(type == typeof(Entity) || type == typeof(EntityManager)) {
				return true;
			}
			if(type.IsCastableTo(typeof(IComponentData))) {
				return true;
			}
			if(type.IsDefinedAttribute<NativeContainerAttribute>()) {
				return true;
			}
			return false;
		}

		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, EditValueSettings settings) {
			position = EditorGUI.PrefixLabel(position, label);
			if(value is Entity || value != null && !ReflectionUtils.IsNullOrDefault(value, type)) {
				uNodeGUI.Label(position, value.ToString(), EditorStyles.helpBox);
			}
			else {
				uNodeGUI.Label(position, $"default({type.PrettyName()})", EditorStyles.helpBox);
			}
		}
	}

	class Float4FieldControl : FieldControl<float4> {
		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, EditValueSettings settings) {
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (float4)value;
			position = EditorGUI.PrefixLabel(position, label);
			float4 newValue = EditorGUI.Vector4Field(position, GUIContent.none, oldValue);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(newValue);
			}
		}

		public override void DrawLayouted(object value, GUIContent label, Type type, Action<object> onChanged, EditValueSettings settings) {
			DrawDecorators(settings);
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (float4)value;
			float4 newValue = EditorGUILayout.Vector4Field(label, oldValue);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(newValue);
			}
		}
	}

	class Float3FieldControl : FieldControl<float3> {
		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, EditValueSettings settings) {
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (float3)value;
			position = EditorGUI.PrefixLabel(position, label);
			float3 newValue = EditorGUI.Vector3Field(position, GUIContent.none, oldValue);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(newValue);
			}
		}

		public override void DrawLayouted(object value, GUIContent label, Type type, Action<object> onChanged, EditValueSettings settings) {
			DrawDecorators(settings);
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (float3)value;
			float3 newValue = EditorGUILayout.Vector3Field(label, oldValue);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(newValue);
			}
		}
	}

	class Float2FieldControl : FieldControl<float2> {
		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, EditValueSettings settings) {
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (float2)value;
			position = EditorGUI.PrefixLabel(position, label);
			float2 newValue = EditorGUI.Vector2Field(position, GUIContent.none, oldValue);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(newValue);
			}
		}

		public override void DrawLayouted(object value, GUIContent label, Type type, Action<object> onChanged, EditValueSettings settings) {
			DrawDecorators(settings);
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (float2)value;
			float2 newValue = EditorGUILayout.Vector2Field(label, oldValue);
			if(EditorGUI.EndChangeCheck()) {
				onChanged(newValue);
			}
		}
	}

	class Int2FieldControl : FieldControl<int2> {
		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, EditValueSettings settings) {
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (int2)value;
			position = EditorGUI.PrefixLabel(position, label);
			var newValue = EditorGUI.Vector2IntField(position, GUIContent.none, new(oldValue.x, oldValue.y));
			if(EditorGUI.EndChangeCheck()) {
				onChanged(new int2(newValue.x, newValue.y));
			}
		}

		public override void DrawLayouted(object value, GUIContent label, Type type, Action<object> onChanged, EditValueSettings settings) {
			DrawDecorators(settings);
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (int2)value;
			var newValue = EditorGUILayout.Vector2IntField(label, new(oldValue.x, oldValue.y));
			if(EditorGUI.EndChangeCheck()) {
				onChanged(new int2(newValue.x, newValue.y));
			}
		}
	}

	class Int3FieldControl : FieldControl<int3> {
		public override void Draw(Rect position, GUIContent label, object value, Type type, Action<object> onChanged, EditValueSettings settings) {
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (int3)value;
			position = EditorGUI.PrefixLabel(position, label);
			var newValue = EditorGUI.Vector3IntField(position, GUIContent.none, new(oldValue.x, oldValue.y, oldValue.z));
			if(EditorGUI.EndChangeCheck()) {
				onChanged(new int3(newValue.x, newValue.y, newValue.x));
			}
		}

		public override void DrawLayouted(object value, GUIContent label, Type type, Action<object> onChanged, EditValueSettings settings) {
			DrawDecorators(settings);
			EditorGUI.BeginChangeCheck();
			ValidateValue(ref value);
			var oldValue = (int3)value;
			var newValue = EditorGUILayout.Vector3IntField(label, new(oldValue.x, oldValue.y, oldValue.z));
			if(EditorGUI.EndChangeCheck()) {
				onChanged(new int3(newValue.x, newValue.y, newValue.z));
			}
		}
	}
}
