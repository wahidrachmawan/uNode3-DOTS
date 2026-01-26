using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;

[assembly: MakeSerializable(typeof(MaxyGames.UNode.Nodes.IJobEntityContainer))]
namespace MaxyGames.UNode.Nodes {
	[EventGraph("IJobEntity", createName = "newJob")]
	public class IJobEntityContainer : BaseJobContainer, ISuperNodeWithEntry, IGeneratorPrePostInitializer, IErrorCheck {
		public enum DataKind {
			ReadOnly,
			ReadWrite,
			None,
		}
		public enum IndexKind {
			None,
			Entity,
			Chunk,
			ChunkAndEntity
		}
		public class Data {
			public string id = uNodeUtility.GenerateUID();

			public string name;
			public SerializedType type = typeof(IComponentData);
			public DataKind kind;

			[NonSerialized]
			public ValueOutput port;
		}
		public List<Data> datas = new List<Data>() { new Data() };

		public List<ECSQueryFilter> queryFilters = new List<ECSQueryFilter>();

		public bool burstCompile = true;
		public EntityQueryOptions options = EntityQueryOptions.Default;
		public IndexKind indexKind;

		public ValueOutput entity { get; private set; }
		public ValueOutput index { get; private set; }

		public override void RegisterEntry(BaseEntryNode node) {
			base.RegisterEntry(node);

			entity = Node.Utilities.ValueOutput(node, nameof(entity), typeof(Entity), PortAccessibility.ReadOnly);

			switch(indexKind) {
				case IndexKind.None:
					//Cleanup if changed
					index = null;
					break;
				case IndexKind.Chunk:
					index = Node.Utilities.ValueOutput(node, nameof(index), typeof(int)).SetName("chunkIndexInQuery");
					break;
				case IndexKind.Entity:
					index = Node.Utilities.ValueOutput(node, nameof(index), typeof(int)).SetName("entityIndexInQuery");
					break;
				case IndexKind.ChunkAndEntity:
					index = Node.Utilities.ValueOutput(node, nameof(index), typeof(int));
					break;
			}

			for(int i = 0; i < datas.Count; i++) {
				var data = datas[i];
				data.port = Node.Utilities.ValueOutput(node, data.id, () => data.type, PortAccessibility.ReadWrite).SetName(!string.IsNullOrEmpty(data.name) ? data.name : ("Item" + (i + 1)));
				data.port.canSetValue = () => data.kind == DataKind.ReadWrite;
			}
		}

		public void OnPreInitializer() {
			//Ensure this node is registered
			Entry.EnsureRegistered();
			//Manual register the entry node.
			CG.RegisterDependency(entryObject);
			//Initialize the class name
			CG.RegisterUserObject(CG.GenerateNewName(name), this);
		}

		public void OnPostInitializer() {
			string className = CG.GetUserObject<string>(this);
			List<string> variableNames = datas.Select(item => CG.RegisterVariable(item.port)).ToList();

			for(int i = 0; i < datas.Count; i++) {
				int index = i;
				CG.RegisterPort(datas[index].port, () => variableNames[index]);
			}
			for(int i = 0; i < variableDatas.Count; i++) {
				var data = variableDatas[i];
				var vName = CG.RegisterVariable(data.port);
				CG.RegisterPort(data.port, () => vName);
			}

			if(entity.hasValidConnections) {
				var vName = CG.RegisterVariable(entity);
				CG.RegisterPort(entity, () => vName);
			}

			if(index != null && index.hasValidConnections) {
				var vName = CG.RegisterVariable(index);
				CG.RegisterPort(index, () => vName);
			}

			List<ECSJobVariable> localVariables = JobVariables;
			if(variableDatas.Count > 0) {
				for(int i = 0; i < variableDatas.Count; i++) {
					var data = variableDatas[i];
					localVariables.Add(new ECSJobVariable() {
						name = CG.GetVariableName(data.port),
						type = data.type,
						owner = data,
					});
				}
			}

			CG.RegisterPostGeneration((classData) => {
				//Create class
				var classBuilder = new CG.ClassData(className);
				classBuilder.implementedInterfaces.Add(typeof(IJobEntity));
				classBuilder.SetToPartial();
				classBuilder.SetTypeToStruct();
				if(burstCompile && !CG.debugScript) {
					classBuilder.RegisterAttribute(typeof(BurstCompileAttribute));
				}
				if(localVariables.Count > 0) {
					for(int i = 0; i < localVariables.Count; i++) {
						var data = localVariables[i];
						classBuilder.RegisterVariable(CG.DeclareVariable(data.type, data.name, modifier: FieldModifier.PublicModifier));
					}
				}

				//Create execute method
				var method = new CG.MData(nameof(IJobChunk.Execute), typeof(void)) {
					modifier = new FunctionModifier(),
				};
				List<CG.MPData> parameters = new List<CG.MPData>();
				if(entity.hasValidConnections) {
					parameters.Add(new CG.MPData(CG.GetVariableName(entity), typeof(Entity)));
				}
				if(index != null && index.hasValidConnections) {
					CG.MPData paramData = new CG.MPData(CG.GetVariableName(index), typeof(int));

					switch(indexKind) {
						case IndexKind.Chunk:
							paramData.RegisterAttribute(typeof(EntityIndexInChunk));
							break;
						case IndexKind.ChunkAndEntity:
							paramData.RegisterAttribute(typeof(EntityIndexInChunk));
							paramData.RegisterAttribute(typeof(EntityIndexInQuery));
							break;
						case IndexKind.Entity:
							paramData.RegisterAttribute(typeof(EntityIndexInQuery));
							break;
					}
					parameters.Add(paramData);
				}
				for(int i = 0; i < variableNames.Count; i++) {
					var data = datas[i];
					switch(data.kind) {
						case DataKind.ReadOnly:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.In));
							break;
						case DataKind.ReadWrite:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.Ref));
							break;
						case DataKind.None:
							parameters.Add(new CG.MPData(variableNames[i], data.type));
							break;
					}
				}
				method.parameters = parameters;
				//Generate code for execute logic
				method.code = CG.GeneratePort(Entry.nodeObject.primaryFlowOutput);

				foreach(var q in queryFilters) {
					switch(q.filter) {
						case QueryFilter.WithAll:
							classBuilder.RegisterAttribute(typeof(WithAllAttribute), CG.Value(q.type));
							break;
						case QueryFilter.WithAny:
							classBuilder.RegisterAttribute(typeof(WithAnyAttribute), CG.Value(q.type));
							break;
						case QueryFilter.WithNone:
							classBuilder.RegisterAttribute(typeof(WithNoneAttribute), CG.Value(q.type));
							break;
						case QueryFilter.WithChangeFilter:
							classBuilder.RegisterAttribute(typeof(WithChangeFilterAttribute), CG.Value(q.type));
							break;
						case QueryFilter.WithAbsent:
							classBuilder.RegisterAttribute(typeof(WithAbsentAttribute), CG.Value(q.type));
							break;
						case QueryFilter.WithDisabled:
							classBuilder.RegisterAttribute(typeof(WithDisabledAttribute), CG.Value(q.type));
							break;
						case QueryFilter.WithPresent:
							classBuilder.RegisterAttribute(typeof(WithPresentAttribute), CG.Value(q.type));
							break;
					}
				}

				//var withAll = queryFilters.Where(item => item.filter == QueryFilter.WithAll).ToList();
				//var withAny = queryFilters.Where(item => item.filter == QueryFilter.WithAny).ToList();
				//var withNone = queryFilters.Where(item => item.filter == QueryFilter.WithNone).ToList();
				//var withChangeFilter = queryFilters.Where(item => item.filter == QueryFilter.WithChangeFilter).ToList();

				////Filters
				//if(withAll.Count > 0) {
				//	classBuilder.RegisterAttribute(typeof(WithAllAttribute), withAll.Select(item => CG.Value(item.type)).ToArray());
				//}
				//if(withAny.Count > 0) {
				//	classBuilder.RegisterAttribute(typeof(WithAnyAttribute), withAny.Select(item => CG.Value(item.type)).ToArray());
				//}
				//if(withNone.Count > 0) {
				//	classBuilder.RegisterAttribute(typeof(WithNoneAttribute), withNone.Select(item => CG.Value(item.type)).ToArray());
				//}
				//if(withChangeFilter.Count > 0) {
				//	classBuilder.RegisterAttribute(typeof(WithChangeFilterAttribute), withChangeFilter.Select(item => CG.Value(item.type)).ToArray());
				//}
				if(options != EntityQueryOptions.Default) {
					classBuilder.RegisterAttribute(typeof(WithOptionsAttribute), options.CGValue());
				}

				//Register the generated function code
				classBuilder.RegisterFunction(method.GenerateCode());
				//Register the generated type code
				classData.RegisterNestedType(CG.WrapWithInformation(classBuilder.GenerateCode(), this));
			});
		}

		void IErrorCheck.CheckError(ErrorAnalyzer analizer) {
			for(int i = 0; i < datas.Count; i++) {
				if(datas[i].type.type?.IsInterface == true) {
					analizer.RegisterError(this, "Please assign correct query type of IComponentData.");
					return;
				}
			}
		}
	}
}

#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;
	using UnityEngine.UIElements;

	class IJobEntityContainerDrawer : UGraphElementDrawer<Nodes.IJobEntityContainer> {
		static readonly FilterAttribute componentFilter;

		static IJobEntityContainerDrawer() {
			componentFilter = new FilterAttribute(typeof(IComponentData), typeof(IQueryTypeParameter)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = true,
				DisplayValueType = true,
			};
		}

		public override void DrawLayouted(DrawerOption option) {
			var node = GetValue(option);

			DrawHeader(option);

			UInspector.Draw(option.property[nameof(node.burstCompile)]);
			UInspector.Draw(option.property[nameof(node.options)]);
			UInspector.Draw(option.property[nameof(node.indexKind)]);

			uNodeGUI.DrawCustomList(node.variableDatas, "Variables",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var portName = EditorGUI.DelayedTextField(position, new GUIContent("Name "), value.name);
					if(portName != value.name) {
						value.name = portName;
						node.Entry.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Important);
						uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Type"), type => {
						value.type = type;
						node.Entry.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Important);
						uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
					}, FilterAttribute.DefaultTypeFilter, option.unityObject);
				},
				add: position => {
					option.RegisterUndo();
					node.variableDatas.Add(new ());
					node.Entry.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Important);
					uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					node.variableDatas.RemoveAt(index);
					node.Entry.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Important);
					uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 2;
				});

			uNodeGUI.DrawCustomList(node.datas, "Query",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var portName = EditorGUI.DelayedTextField(position, new GUIContent("Item " + index), value.name);
					if(portName != value.name) {
						value.name = portName;
						node.Entry.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Type"), type => {
						value.type = type;
						if(type.HasImplementInterface(typeof(IComponentData))) {
							if(value.kind == Nodes.IJobEntityContainer.DataKind.None) {
								value.kind = Nodes.IJobEntityContainer.DataKind.ReadWrite;
							}
						}
						else {
							value.kind = Nodes.IJobEntityContainer.DataKind.None;
						}
						node.Entry.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
					}, componentFilter, option.unityObject);
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.EditValue(position, new GUIContent("Accessibility"), value.kind, (val) => {
						value.kind = val;
						if(value.kind == Nodes.IJobEntityContainer.DataKind.None && value.type.type.HasImplementInterface(typeof(IComponentData))) {
							value.kind = Nodes.IJobEntityContainer.DataKind.ReadWrite;
						}
						node.Entry.Register();
					});
				},
				add: position => {
					option.RegisterUndo();
					node.datas.Add(new ());
					node.Entry.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					node.datas.RemoveAt(index);
					node.Entry.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 3) + 3;
				});

			uNodeGUI.DrawCustomList(node.queryFilters, "Query Filters",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var filter = (QueryFilter)EditorGUI.EnumPopup(position, new GUIContent("Filter " + index), value.filter);
					if(filter != value.filter) {
						value.filter = filter;
						node.Entry.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Component Type"), type => {
						value.type = type;
						node.Entry.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
					}, componentFilter, option.unityObject);
				},
				add: position => {
					option.RegisterUndo();
					node.queryFilters.Add(new());
					node.Entry.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					node.queryFilters.RemoveAt(index);
					node.Entry.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(node.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 3;
				});

			DrawErrors(option);
		}
	}
}
#endif