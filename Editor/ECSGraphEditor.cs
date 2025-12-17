using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Editors {
	[CustomEditor(typeof(ECSGraph), true)]
	public class ECSGraphEditor : Editor {
		private static FilterAttribute typeFilter = new FilterAttribute() { DisplayReferenceType = false, DisplayValueType = true };
		private static FilterAttribute systemFilter = new FilterAttribute(typeof(ISystem), typeof(SystemBase));
		private static FilterAttribute inheritFilter = new FilterAttribute(typeof(SystemBase)) {
			DisplaySealedType = false,
			ArrayManipulator = false,
			DisplayInterfaceType = false,
			DisplayValueType = false,
		};

		static ECSGraphEditor() {
			typeFilter.ValidateType = (type) => {
				if(type.IsValueType) {
					if(type.HasImplementInterface(typeof(Unity.Entities.IComponentData))) {
						return true;
					}
					if(type.HasImplementInterface(typeof(Unity.Entities.ISharedComponentData))) {
						return true;
					}
				}
				return false;
			};
		}

		public override void OnInspectorGUI() {
			var monoScript = uNodeEditorUtility.GetMonoScript(target);
			if(monoScript != null) {
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.ObjectField("Script", monoScript, typeof(MonoScript), true);
				EditorGUI.EndDisabledGroup();
			}
			var asset = target as ECSGraph;

			uNodeGUIUtility.ShowField(nameof(asset.@namespace), asset, asset);
			uNodeGUI.DrawNamespace("Using Namespaces", asset.usingNamespaces, asset, (arr) => {
				asset.usingNamespaces = arr as List<string> ?? arr.ToList();
				uNodeEditorUtility.MarkDirty(asset);
			});
			uNodeGUIUtility.ShowField(nameof(asset.modifier), asset, asset);

			int popupIndex = asset.inheritType == typeof(ValueType) ? 1 : 0;
			var newPopupIndex = EditorGUILayout.Popup(new GUIContent("System Kind"), popupIndex, new[] { "SystemBase", "ISystem" });
			if(popupIndex != newPopupIndex) {
				if(newPopupIndex == 0) {
					asset.inheritType = typeof(SystemBase);
				}
				else {
					asset.inheritType = typeof(ValueType);
				}
			}
			if(popupIndex == 0) {
				uNodeGUIUtility.DrawTypeDrawer(uNodeGUIUtility.GetRect(), asset.inheritType, new GUIContent("Inherit From"), (type) => {
					asset.inheritType = type;
				}, inheritFilter, asset);
			}
			else {
				uNodeGUIUtility.ShowField(nameof(asset.burstCompile), asset, asset);
			}

			uNodeGUI.DrawTypeList("Required For Updates", asset.requiredForUpdates, typeFilter, target);

			//if(asset is IAttributeSystem) 
			{
				uNodeGUI.DrawAttribute((asset as IAttributeSystem).Attributes, asset, null, asset.inheritType == typeof(ValueType) ? AttributeTargets.Struct : AttributeTargets.Class);
			}

			if(asset is IInterfaceSystem) {
				uNodeGUI.DrawInterfaces(asset as IInterfaceSystem);
			}
			uNodeGUIUtility.EditValueLayouted(nameof(asset.GraphData.graphLayout), asset.GraphData, val => {
				asset.GraphData.graphLayout = (GraphLayout)val;
				UGraphView.ClearCache(asset);
				uNodeEditor.window?.Refresh(true);
			});
		}

	}
}