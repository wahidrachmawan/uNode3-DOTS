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
	[TypeIcons.IconGuid("bc396893cb8045f4da8ffa1d71e478da")]
	public class IJobEntityContainer : BaseJobContainer, IIcon, ISuperNodeWithEntry, IGeneratorPrePostInitializer, IErrorCheck {
		[Flags]
		public enum IndexKind {
			None = 0,
			ChunkIndexInQuery = 1,
			EntityIndexInChunk = 2,
			EntityIndexInQuery = 4,
		}
		[Serializable]
		public class Data {
			public string id = uNodeUtility.GenerateUID();

			public string name;
			public SerializedType type = typeof(IComponentData);
			public DataAccessor kind;

			[NonSerialized]
			public ValueOutput port;
		}
		public List<Data> datas = new List<Data>() { new Data() };

		public List<ECSQueryFilter> queryFilters = new List<ECSQueryFilter>();

		public bool burstCompile = true;
		public EntityQueryOptions options = EntityQueryOptions.Default;
		public IndexKind indexKind;

		public ValueOutput entity { get; private set; }
		public ValueOutput chunkIndexInQuery { get; private set; }
		public ValueOutput entityIndexInChunk { get; private set; }
		public ValueOutput entityIndexInQuery { get; private set; }

		public override void RegisterEntry(BaseEntryNode node) {
			base.RegisterEntry(node);

			entity = Node.Utilities.ValueOutput(node, nameof(entity), typeof(Entity), PortAccessibility.ReadOnly);

			//Cleanup
			chunkIndexInQuery = null;
			entityIndexInQuery = null;
			entityIndexInChunk = null;
			//Create index ports
			if(indexKind.HasFlags(IndexKind.ChunkIndexInQuery)) {
				chunkIndexInQuery = Node.Utilities.ValueOutput(node, nameof(chunkIndexInQuery), typeof(int)).SetName("chunkIndexInQuery");
				chunkIndexInQuery.SetTooltip("Used as the chunk index inside the current query.");
			}
			if(indexKind.HasFlags(IndexKind.EntityIndexInQuery)) {
				entityIndexInQuery = Node.Utilities.ValueOutput(node, nameof(entityIndexInQuery), typeof(int)).SetName("entityIndexInQuery");
				entityIndexInQuery.SetTooltip("Used as a way to get the packed entity index inside the current query.\n\n" +
					"This is generally way more expensive than ChunkIndexInQuery and EntityIndexInChunk.\n" +
					"As it it will schedule a EntityQuery.CalculateBaseEntityIndexArrayAsync job to get an offset buffer.\n" +
					"If you just want a sortkey for your EntityCommandBuffer.ParallelWriter simply use ChunkIndexInQuery\n" +
					"as it is different for every thread, which is all a ParallelWriter needs to sort with.");
			}
			if(indexKind.HasFlags(IndexKind.EntityIndexInChunk)) {
				entityIndexInChunk = Node.Utilities.ValueOutput(node, nameof(entityIndexInChunk), typeof(int)).SetName("entityIndexInChunk");
				entityIndexInChunk.SetTooltip("Used as the entity index inside the current chunk.");
			}

			for(int i = 0; i < datas.Count; i++) {
				var data = datas[i];
				data.port = Node.Utilities.ValueOutput(node, data.id, () => data.type, PortAccessibility.ReadWrite).SetName(!string.IsNullOrEmpty(data.name) ? data.name : ("Item" + (i + 1)));
				data.port.canSetValue = () => data.kind == DataAccessor.ReadWrite;
			}
		}

		public void OnPreInitializer() {
			//Ensure this node is registered
			Entry.EnsureRegistered();
			//Manual register the entry node.
			CG.RegisterDependency(entryObject);
			//Initialize the class name
			CG.RegisterUserObject(CG.GenerateNewName(name), this);
			if(executionMode == ECSLogicExecutionMode.ScheduleParallel && indexKind.HasFlags(IndexKind.ChunkIndexInQuery) == false) {
				throw new Exception("You need to use ChunkIndexInQuery index kind if using ScheduleParallel execution mode");
			} 
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

			if(chunkIndexInQuery != null) {
				var vName = CG.RegisterVariable(chunkIndexInQuery);
				CG.RegisterPort(chunkIndexInQuery, () => vName);
			}
			if(entityIndexInQuery != null) {
				var vName = CG.RegisterVariable(entityIndexInQuery);
				CG.RegisterPort(entityIndexInQuery, () => vName);
			}
			if(entityIndexInChunk != null) {
				var vName = CG.RegisterVariable(entityIndexInChunk);
				CG.RegisterPort(entityIndexInChunk, () => vName);
			}

			List<ECSJobVariable> localVariables = JobVariables;
			if(variableDatas.Count > 0) {
				for(int i = 0; i < variableDatas.Count; i++) {
					var data = variableDatas[i];
					localVariables.Add(new ECSJobVariable() {
						name = CG.GetVariableName(data.port),
						type = data.type,
						owner = data,
						attributes = data.attributes,
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
						classBuilder.RegisterVariable(
							CG.DeclareVariable(
								data.type, 
								data.name, 
								modifier: FieldModifier.PublicModifier, 
								attributes: data.attributes.Select(att => CG.Attribute(att)))
							);
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
				if(chunkIndexInQuery != null) {
					CG.MPData paramData = new CG.MPData(CG.GetVariableName(chunkIndexInQuery), typeof(int));
					paramData.RegisterAttribute(typeof(ChunkIndexInQuery));
					parameters.Add(paramData);
				}
				if(entityIndexInQuery != null) {
					CG.MPData paramData = new CG.MPData(CG.GetVariableName(entityIndexInQuery), typeof(int));
					paramData.RegisterAttribute(typeof(EntityIndexInQuery));
					parameters.Add(paramData);
				}
				if(entityIndexInChunk != null) {
					CG.MPData paramData = new CG.MPData(CG.GetVariableName(entityIndexInChunk), typeof(int));
					paramData.RegisterAttribute(typeof(EntityIndexInChunk));
					parameters.Add(paramData);
				}
				for(int i = 0; i < variableNames.Count; i++) {
					var data = datas[i];
					switch(data.kind) {
						case DataAccessor.ReadOnly:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.In));
							break;
						case DataAccessor.ReadWrite:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.Ref));
							break;
						case DataAccessor.None:
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

		public Type GetIcon() {
			return typeof(IJobEntityContainer);
		}

		public override string GenerateParallelIndex() {
			return CG.GetVariableName(chunkIndexInQuery);
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
			var container = GetValue(option);

			DrawHeader(option);

			UInspector.Draw(option.property[nameof(container.burstCompile)]);
			UInspector.Draw(option.property[nameof(container.options)]);
			UInspector.Draw(option.property[nameof(container.indexKind)]);
			UInspector.Draw(option.property[nameof(container.executionMode)]);

			uNodeGUI.DrawCustomList(container.variableDatas, "Variables",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var portName = EditorGUI.DelayedTextField(position, new GUIContent("Name "), value.name);
					if(portName != value.name) {
						value.name = portName;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Type"), type => {
						value.type = type;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}, FilterAttribute.DefaultTypeFilter, option.unityObject);
					position.y += EditorGUIUtility.singleLineHeight + 3;
					uNodeGUI.DrawAttribute(position, value.attributes, container.GetUnityObject(), atts => {
						value.attributes = atts;
					}, AttributeTargets.Field);
				},
				add: position => {
					option.RegisterUndo();
					container.variableDatas.Add(new ());
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					container.variableDatas.RemoveAt(index);
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					if(container.variableDatas[index].attributes.Count > 1) {
						return (EditorGUIUtility.singleLineHeight * 2) + 2 + 72 + (Mathf.Max(0, container.variableDatas[index].attributes.Count - 1) * 23);
					}
					return (EditorGUIUtility.singleLineHeight * 2) + 2 + 72;
				});

			uNodeGUI.DrawCustomList(container.datas, "Query",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var portName = EditorGUI.DelayedTextField(position, new GUIContent("Item " + index), value.name);
					if(portName != value.name) {
						value.name = portName;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Type"), type => {
						value.type = type;
						if(type.HasImplementInterface(typeof(IComponentData))) {
							if(value.kind == DataAccessor.None) {
								value.kind = DataAccessor.ReadWrite;
							}
						}
						else {
							value.kind = DataAccessor.None;
						}
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}, componentFilter, option.unityObject);
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.EditValue(position, new GUIContent("Accessibility"), value.kind, (val) => {
						value.kind = val;
						if(value.kind == DataAccessor.None && value.type.type.HasImplementInterface(typeof(IComponentData))) {
							value.kind = DataAccessor.ReadWrite;
						}
						container.Entry.Register();
					});
				},
				add: position => {
					option.RegisterUndo();
					container.datas.Add(new ());
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					container.datas.RemoveAt(index);
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 3) + 3;
				});

			uNodeGUI.DrawCustomList(container.queryFilters, "Query Filters",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var filter = (QueryFilter)EditorGUI.EnumPopup(position, new GUIContent("Filter " + index), value.filter);
					if(filter != value.filter) {
						value.filter = filter;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Component Type"), type => {
						value.type = type;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}, componentFilter, option.unityObject);
				},
				add: position => {
					option.RegisterUndo();
					container.queryFilters.Add(new());
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					container.queryFilters.RemoveAt(index);
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Average);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 3;
				});

			DrawErrors(option);
		}
	}
}
#endif