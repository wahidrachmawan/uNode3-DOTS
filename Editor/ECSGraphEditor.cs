using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Editors {
	[CustomEditor(typeof(ECSGraph), true)]
	public class ECSGraphEditor : GraphAssetEditor {
		private static FilterAttribute typeFilter = new FilterAttribute() { DisplayReferenceType = true, DisplayValueType = true };
		//private static FilterAttribute systemFilter = new FilterAttribute(typeof(ISystem), typeof(SystemBase));
		private static FilterAttribute inheritFilter = new FilterAttribute(typeof(SystemBase)) {
			DisplaySealedType = false,
			ArrayManipulator = false,
			DisplayInterfaceType = false,
			DisplayValueType = false,
		};
		private static FilterAttribute systemOrderFilter = new FilterAttribute(typeof(ComponentSystemBase), typeof(ISystem)) {
			DisplayAbstractType = false,
			ArrayManipulator = false,
			DisplayInterfaceType = false,
			OnlyGetType = true,
		};
		private static FilterAttribute updateInGroupFilter = new FilterAttribute(typeof(ComponentSystemGroup)) {
			ArrayManipulator = false,
			DisplayInterfaceType = false,
			OnlyGetType = true,
		};

		static ECSGraphEditor() {
			typeFilter.ValidateType = (type) => {
				if(type.HasImplementInterface(typeof(Unity.Entities.IComponentData))) {
					return true;
				}
				if(type.HasImplementInterface(typeof(Unity.Entities.ISharedComponentData))) {
					return true;
				}
				return false;
			};
		}

		public override void DrawGUI(bool isInspector) {
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
				//TODO: make inheritance support for ECS graph
				//uNodeGUIUtility.DrawTypeDrawer(uNodeGUIUtility.GetRect(), asset.inheritType, new GUIContent("Inherit From"), (type) => {
				//	asset.inheritType = type;
				//}, inheritFilter, asset);
			}
			else {
				uNodeGUIUtility.ShowField(nameof(asset.burstCompile), asset, asset);
			}

			uNodeGUI.DrawTypeList("Required For Updates", asset.requiredForUpdates, typeFilter, target);

			uNodeGUI.DrawCustomList(asset.systemOrder, "System Orders",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;

					var pos = EditorGUI.PrefixLabel(position, new GUIContent("System"));
					pos.width -= 20;
					if(EditorGUI.DropdownButton(pos, new GUIContent(value.Name, value.graph != null ? uNodeEditorUtility.GetTypeIcon(value.graph) : uNodeEditorUtility.GetTypeIcon(value.type)), FocusType.Keyboard, EditorStyles.objectField)) {
						UnityEngine.Object uobj = value.graph;
						if(uobj == null) {
							if(value.type?.type != null) {
								uobj = uNodeEditorUtility.GetMonoScript(value.type);
							}
						}
						if(uobj != null) {
							if(Event.current.clickCount == 2) {
								Selection.activeObject = uobj;
							}
							else {
								EditorGUIUtility.PingObject(uobj);
							}
						}
					}
					if(GUI.Button(new Rect(pos.x + pos.width, pos.y, 20, pos.height), GUIContent.none, uNodeGUIStyle.objectField)) {
						var items = new List<ItemSelector.CustomItem>();
						items.AddRange(ItemSelector.MakeCustomItemsForInstancedType(typeof(ECSGraph), (val) => {
							if(val is ECSGraph graph) {
								value.graph = graph;
								if(value.kind == SystemOrderKind.UpdateInGroup) {
									value.kind = SystemOrderKind.UpdateAfter;
								}
								uNodeEditorUtility.MarkDirty(asset);
							}
						}, false));
						ItemSelector.ShowWindow(asset, value.kind == SystemOrderKind.UpdateInGroup ? updateInGroupFilter : systemOrderFilter, val => {
							value.type = val.startType;
							value.graph = null;
							uNodeEditorUtility.MarkDirty(asset);
						}).SetDisplayNoneItem(false).SetCustomItems(items).ChangePosition(pos.ToScreenRect());
					}
					uNodeEditorUtility.GUIDropArea(pos,
						onDragPerform: () => {
							if(DragAndDrop.objectReferences.Length != 1) {
								return;
							}
							var dragObj = DragAndDrop.objectReferences[0];
							if(dragObj is ECSGraph) {
								value.graph = dragObj as ECSGraph;
								if(value.kind == SystemOrderKind.UpdateInGroup) {
									value.kind = SystemOrderKind.UpdateAfter;
								}
								uNodeEditorUtility.MarkDirty(asset);
							}
							else {
								uNodeEditorUtility.DisplayErrorMessage("Invalid dragged object.");
								DragAndDrop.objectReferences = new UnityEngine.Object[0];
							}
						},
						repaintAction: () => {
							//GUI.DrawTexture(pos, uNodeEditorUtility.MakeTexture(1, 1, new Color(0, 0.5f, 1, 0.5f)));
							EditorGUI.DrawRect(pos, new Color(0, 0.5f, 1, 0.5f));
						});

					position.y += EditorGUIUtility.singleLineHeight + 1;
					var kind = (SystemOrderKind)EditorGUI.EnumPopup(position, "Kind", value.kind);
					if(kind != value.kind) {
						value.kind = kind;
						if(kind == SystemOrderKind.UpdateInGroup) {
							value.graph = null;
						}
						uNodeEditorUtility.MarkDirty(asset);
					}
				},
				add: position => {
					asset.systemOrder.Add(new SystemOrderData() { type = typeof(InitializationSystemGroup), kind = SystemOrderKind.UpdateInGroup });
					uNodeEditorUtility.MarkDirty(asset);
				},
				remove: index => {
					asset.systemOrder.RemoveAt(index);
					uNodeEditorUtility.MarkDirty(asset);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 3;
				});


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

			if(isInspector) {
				DrawExecutionMode();
				DrawOpenGraph();
			}
		}

		public override void DrawExecutionMode() {
			ECSGraph asset = target as ECSGraph;

			bool IsGraphCompiled() {
				var scriptData = GenerationUtility.GetGraphData(asset);
				if(scriptData.isValid) {
					return System.IO.File.Exists(SystemCompiler.OutputDllPath) && System.IO.File.Exists(scriptData.path);
				}
				return false;
			};

			if(!IsGraphCompiled()) {
				var boxRect = EditorGUILayout.BeginVertical();
				EditorGUILayout.HelpBox("This graph is still not compiled.\n[Click to Compile]", MessageType.Info);
				EditorGUILayout.EndVertical();
				if(Event.current.clickCount == 1 && Event.current.button == 0 && boxRect.Contains(Event.current.mousePosition)) {
					GraphUtility.SaveAllGraph();
					SystemCompiler.GenerateAndCompileGraphs();
					Event.current.Use();
				}
			}
			else if(GenerationUtility.IsGraphUpToDate(asset)) {
				EditorGUILayout.HelpBox("This graph is compiled and run with native C#", MessageType.Info);
			}
			else {
				var boxRect = EditorGUILayout.BeginVertical();
				EditorGUILayout.HelpBox("This graph is Run using Native C# but script is outdated.\n[Click To Recompile]", MessageType.Warning);
				EditorGUILayout.EndVertical();
				if(Event.current.clickCount == 1 && Event.current.button == 0 && boxRect.Contains(Event.current.mousePosition)) {
					GraphUtility.SaveAllGraph();
					SystemCompiler.GenerateAndCompileGraphs();
					Event.current.Use();
				}
			}
		}
	}
}