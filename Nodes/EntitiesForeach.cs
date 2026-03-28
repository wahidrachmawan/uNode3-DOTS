using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS", "Foreach ( Entities )", scope = NodeScope.ECSGraph, hasFlowInput = true, hasFlowOutput = true)]
	public class EntitiesForeach : FlowNode, IGeneratorPrePostInitializer, INodeEntitiesForeach {
		public enum RunKind {
			Run,
			Schedule,
			ScheduleParallel,
		}
		public enum IndexKind {
			None = 0,
			ChunkIndexInQuery = 1,
			EntityIndexInChunk = 2,
			EntityIndexInQuery = 4,
		}
		public enum DataKind {
			ReadOnlyComponent,
			ReadWriteComponent,
			ReadOnlyEnableableComponent,
			ReadWriteEnableableComponent,
		}
		public class Data {
			public string id = uNodeUtility.GenerateUID();

			public string name;
			public SerializedType type = typeof(IComponentData);
			public DataKind kind = DataKind.ReadWriteComponent;

			public bool IsEnableableQuery => kind == DataKind.ReadOnlyEnableableComponent || kind == DataKind.ReadWriteEnableableComponent;

			[NonSerialized]
			public ValueOutput port;
		}
		public List<Data> datas = new List<Data>() { new Data() };

		public RunKind runOn = RunKind.Run;
		public bool burstCompile;
		public EntityQueryOptions options = EntityQueryOptions.Default;
		public IndexKind indexKind = IndexKind.None;

		public List<ECSQueryFilter> queryFilters = new List<ECSQueryFilter>();
		public List<SerializedType> withSharedComponentFilter = new List<SerializedType>();

		public ValueOutput entity { get; private set; }
		public FlowOutput body { get; private set; }
		public ValueOutput index { get; private set; }
		public ValueOutput chunkIndexInQuery { get; private set; }
		public List<ECSJobVariable> JobVariables => CG.GetUserObject<List<ECSJobVariable>>(nodeObject);

		public ECSLogicExecutionMode LogicExecutionMode => runOn switch {
			RunKind.Schedule => ECSLogicExecutionMode.Schedule,
			RunKind.ScheduleParallel => ECSLogicExecutionMode.ScheduleParallel,
			_ => ECSLogicExecutionMode.Run,
		};

	protected override void OnRegister() {
			body = FlowOutput(nameof(body));
			base.OnRegister();
			exit.SetName("Exit");
			entity = ValueOutput(nameof(entity), typeof(Entity));
			if(datas.Count == 0) {
				datas.Add(new Data());
			}
			for(int i = 0; i < datas.Count; i++) {
				var data = datas[i];
				data.port = ValueOutput(data.id, () => data.IsEnableableQuery ? typeof(bool) : data.type, PortAccessibility.ReadWrite).SetName(!string.IsNullOrEmpty(data.name) ? data.name : ("Item" + (i + 1)));
				data.port.canSetValue = () => data.kind == DataKind.ReadWriteComponent || data.kind == DataKind.ReadWriteEnableableComponent;
			}
			if(indexKind != IndexKind.None && indexKind != IndexKind.ChunkIndexInQuery) {
				index = ValueOutput(nameof(index), typeof(int)).SetName("index");
			}
			if(runOn == RunKind.ScheduleParallel || indexKind == IndexKind.ChunkIndexInQuery) {
				chunkIndexInQuery = ValueOutput(nameof(chunkIndexInQuery), typeof(int)).SetName("chunkIndexInQuery");
				chunkIndexInQuery.SetTooltip("Used as the chunk index inside the current query.");
			}
		}

		protected override void OnExecuted(Flow flow) {
			throw new Exception("ECS is not supported in reflection mode.");
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();

			if(entity.hasValidConnections) {
				var vName = CG.RegisterVariable(entity);
				CG.RegisterPort(entity, () => vName);
			}

			foreach(var data in datas) {
				var vName = CG.RegisterVariable(data.port);
				if(data.type.type?.IsValueType == false) {
					CG.RegisterPort(data.port, () => vName);
					continue;
				}
				switch(data.kind) {
					case DataKind.ReadOnlyComponent:
					case DataKind.ReadOnlyEnableableComponent:
						if(runOn != RunKind.Run) goto default;
						vName = vName.CGAccess("ValueRO");
						CG.RegisterPort(data.port, () => vName);
						break;
					case DataKind.ReadWriteComponent:
					case DataKind.ReadWriteEnableableComponent:
						if(runOn != RunKind.Run) goto default;
						CG.RegisterPort(data.port, () => {
							if(CG.generationState.contextState == CG.ContextState.Set) {
								//Auto use the ValueRW for set
								return vName.CGAccess("ValueRW");
							} else {
								//Auto use the ValueRO for read
								return vName.CGAccess("ValueRO");
							}
						});
						break;
					default:
						CG.RegisterPort(data.port, () => vName);
						break;
				}
			}
		}

		public override void CheckError(ErrorAnalyzer analyzer) {
			base.CheckError(analyzer);
			foreach(var data in datas) {
				switch(data.kind) {
					case DataKind.ReadOnlyComponent:
					case DataKind.ReadWriteComponent:
						if(data.type.type == null || data.type.type.HasImplementInterface(typeof(IComponentData)) == false) {
							analyzer.RegisterError(this, "Invalid or Unassigned type for query: " + data.port.name);
						}
						break;
					case DataKind.ReadOnlyEnableableComponent:
					case DataKind.ReadWriteEnableableComponent:
						if(data.type.type == null || data.type.type.HasImplementInterface(typeof(IEnableableComponent)) == false) {
							analyzer.RegisterError(this, "Invalid or Unassigned type for query: " + data.port.name);
						}
						break;
				}
			}
		}

		public override string GetTitle() {
			return runOn.ToString() + " Foreach: " + string.Join(", ", datas.Select(d => d.type.prettyName)).Wrap();
		}

		public override string GetRichTitle() {
			return runOn.ToString() + " Foreach: " + string.Join(", ", datas.Select(d => uNodeUtility.WrapTextWithTypeColor(d.type.prettyName, d.type.type))).Wrap();
		}

		private string GenerateScheduleCode() {
			string className = CG.GetUserObject<string>(this);
			var job = CG.GenerateNewName("job");

			var variables = CG.GetUserObject<List<ECSJobVariable>>(nodeObject);

			return CG.Flow(
				CG.DeclareVariable("job", CG.New(className, null, variables.Select(v => v.name.CGSetValue(v.value())))),
				job.CGFlowInvoke(runOn == RunKind.Schedule ? nameof(IJobEntityExtensions.Schedule) : nameof(IJobEntityExtensions.ScheduleParallel)),
				CG.FlowFinish(enter, exit)
			);
		}

		protected override string GenerateFlowCode() {
			if(runOn != RunKind.Run) {
				return GenerateScheduleCode();
			}
			List<string> variableNames = datas.Select(item => CG.GetVariableName(item.port)).ToList();
			string iterator =  typeof(SystemAPI).CGInvoke(
					nameof(SystemAPI.Query),
					datas.Select(item => {
						var itemType = item.type.type;
						if(itemType.IsValueType == false)
							return itemType;
						switch(item.kind) {
							case DataKind.ReadOnlyComponent:
								return typeof(RefRO<>).MakeGenericType(itemType);
							case DataKind.ReadWriteComponent:
								return typeof(RefRW<>).MakeGenericType(itemType);
							case DataKind.ReadOnlyEnableableComponent:
								return typeof(EnabledRefRO<>).MakeGenericType(itemType);
							case DataKind.ReadWriteEnableableComponent:
								return typeof(EnabledRefRW<>).MakeGenericType(itemType);
						}
						throw null;
					}).ToArray()
				);

			var queries = queryFilters.OrderBy(data => (int)data.filter).GroupBy(data => data.filter);
			foreach(var group in queries) {
				iterator = iterator.CGInvoke(group.Key.ToString(), group.Select(item => item.type.type).ToArray());
			}

			//var withAll = queryFilters.Where(item => item.filter == QueryFilter.WithAll).ToList();
			//var withAny = queryFilters.Where(item => item.filter == QueryFilter.WithAny).ToList();
			//var withNone = queryFilters.Where(item => item.filter == QueryFilter.WithNone).ToList();
			//var withChangeFilter = queryFilters.Where(item => item.filter == QueryFilter.WithChangeFilter).ToList();
			//if(withAll.Count > 0) {
			//	iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithAll), withAll.Select(item => item.type.type).ToArray());
			//}
			//if(withAny.Count > 0) {
			//	iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithAny), withAny.Select(item => item.type.type).ToArray());
			//}
			//if(withNone.Count > 0) {
			//	iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithNone), withNone.Select(item => item.type.type).ToArray());
			//}
			//if(withChangeFilter.Count > 0) {
			//	iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithChangeFilter), withChangeFilter.Select(item => item.type.type).ToArray());
			//}
			if(withSharedComponentFilter.Count > 0) {
				iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithSharedComponentFilter), withSharedComponentFilter.Select(item => item.type).ToArray());
			}
			if(entity.hasValidConnections) {
				iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithEntityAccess));
				variableNames.Add(CG.GetVariableName(entity));
			}
			if(options != EntityQueryOptions.Default) {
				iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithOptions), options.CGValue());
			}
			var foreachVarNames = string.Join(", ", variableNames);
			if(variableNames.Count > 1) {
				foreachVarNames = foreachVarNames.Wrap();
			}
			if(index != null && index.hasValidConnections) {
				return CG.Flow(
					CG.DeclareVariable(index, 0.CGValue()),
					CG.Foreach(null, foreachVarNames, iterator, CG.Flow(CG.Flow(body), CG.Set(CG.GetVariableName(index), 1, SetType.Add))),
					CG.FlowFinish(enter, exit)
				);
			}
			return CG.Flow(
				CG.Foreach(null, foreachVarNames, iterator, CG.Flow(body)),
				CG.FlowFinish(enter, exit)
			);
		}

		public void OnPreInitializer() {
			if(runOn != RunKind.Run) {
				//Initialize the class name
				CG.RegisterUserObject(CG.GenerateNewName(name), this);
				CG.RegisterUserObject(new List<ECSJobVariable>(), nodeObject);
			}
		}

		public void OnPostInitializer() {
			if(runOn == RunKind.Run) return;
			if(chunkIndexInQuery != null) {
				var vName = CG.RegisterVariable(chunkIndexInQuery);
				CG.RegisterPort(chunkIndexInQuery, () => vName);
			}
			if(index != null && index.hasValidConnections) {
				var vName = CG.RegisterVariable(index);
				CG.RegisterPort(index, () => vName);
			}
			var targetBody = body.GetTargetNode();
			string className = CG.GetUserObject<string>(this);
			List<string> variableNames = datas.Select(item => CG.RegisterVariable(item.port)).ToList();

			HashSet<NodeObject> connectedBeforeNode = new HashSet<NodeObject>();
			HashSet<NodeObject> connectedAfterNode = new HashSet<NodeObject>();
			CG.Nodes.FindAllConnections(this, ref connectedBeforeNode, includeFlowInput: true, includeFlowOutput: false, includeValueInput: true);
			CG.Nodes.FindAllConnections(targetBody, ref connectedAfterNode);

			connectedBeforeNode.Remove(this);
			connectedAfterNode.Remove(this);

			HashSet<Function> usedFunctions = new();
			HashSet<Property> usedProperties = new();
			List<ECSJobVariable> localVariables = JobVariables;
			foreach(var node in connectedBeforeNode) {
				connectedAfterNode.Remove(node);
			}
			foreach(var node in connectedAfterNode) {
				foreach(var port in node.ValueInputs) {
					foreach(var con in port.ValidConnections.Cast<ValueConnection>()) {
						if(connectedBeforeNode.Contains(con.output.node)) {
							string vName;
							if(CG.HasRegisteredVariable(con.output)) {
								vName = CG.GetVariableName(con.output);
							}
							else {
								vName = CG.GenerateNewName(con.output.IsPrimaryPort() && (con.output.name == "Out" || con.output.name == "Output") ? con.output.node.name : con.output.name);
							}
							localVariables.Add(new ECSJobVariable() {
								name = vName,
								type = con.output.type,
								value = () => CG.GeneratePort(con.output),
								owner = con.output,
							});
							CG.RegisterPort(port, () => vName);
						}
					}
				}
				if(node.node is MultipurposeNode mNode) {
					var member = mNode.member;
					if(member.targetType == MemberData.TargetType.uNodeFunction) {
						usedFunctions.Add(member.target.startItem.GetReferenceValue() as Function);
					}
					else if(member.targetType == MemberData.TargetType.uNodeProperty) {
						usedProperties.Add(member.target.startItem.GetReferenceValue() as Property);
					}
					else if(member.targetType == MemberData.TargetType.uNodeVariable) {
						if(mNode.output != null && mNode.output.hasValidConnections) {
							var vName = member.target.startName;
							localVariables.Add(new ECSJobVariable() {
								name = vName,
								type = mNode.nodeObject.ReturnType(),
								value = () => CG.GeneratePort(mNode.output),
								owner = mNode.output,
							});
							foreach(var con in mNode.output.connections) {
								if(con.isValid == false) continue;
								CG.RegisterPort(con.input, () => vName);
							}
						}
					}
					else {
						if(mNode.output != null && mNode.output.hasValidConnections) {
							if(member.targetType.IsTargetingGraphValue()) {
								var vName = CG.GenerateNewName(mNode.name);
								localVariables.Add(new ECSJobVariable() {
									name = vName,
									type = mNode.nodeObject.ReturnType(),
									value = () => CG.GeneratePort(mNode.output),
									owner = mNode.output,
								});
								foreach(var con in mNode.output.connections) {
									if(con.isValid == false) continue;
									CG.RegisterPort(con.input, () => vName);
								}
							}
						}
					}
				}
			}

			CG.RegisterPostGeneration((classData) => {
				if(CG.IsFlowRegistered(enter) == false) {
					return;
				}
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
						string content = CG.DeclareVariable(data.type, data.name, modifier: FieldModifier.PublicModifier/*, attributes: new[] { CG.Attribute(typeof(ReadOnlyAttribute)) }*/);
						classBuilder.RegisterVariable(content);
					}
				}
				if(usedFunctions.Count > 0) {
					foreach(var func in usedFunctions) {
						var data = CG.generatorData.GetMethodData(func);
						if(data != null && data.modifier.Static == false) {
							classBuilder.RegisterFunction(data.GenerateCode());
						}
					}
				}
				if(usedProperties.Count > 0) {
					foreach(var prop in usedProperties) {
						var data = CG.generatorData.GetPropertyData(prop.name);
						if(data != null && data.modifier.Static == false) {
							classBuilder.RegisterProperty(data.GenerateCode());
						}
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
				for(int i = 0; i < variableNames.Count; i++) {
					var data = datas[i];
					if(data.type.type.IsValueType == false)
						continue;
					switch(data.kind) {
						case DataKind.ReadOnlyComponent:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.In));
							break;
						case DataKind.ReadWriteComponent:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.Ref));
							break;
						case DataKind.ReadOnlyEnableableComponent:
							parameters.Add(new CG.MPData(variableNames[i], typeof(EnabledRefRO<>).MakeGenericType(data.type)));
							break;
						case DataKind.ReadWriteEnableableComponent:
							parameters.Add(new CG.MPData(variableNames[i], typeof(EnabledRefRW<>).MakeGenericType(data.type)));
							break;
					}
				}
				if(chunkIndexInQuery != null) {
					CG.MPData paramData = new CG.MPData(CG.GetVariableName(chunkIndexInQuery), typeof(int));
					paramData.RegisterAttribute(typeof(ChunkIndexInQuery));
					parameters.Add(paramData);
				}
				if(index != null && index.hasValidConnections) {
					CG.MPData paramData = new CG.MPData(CG.GetVariableName(index), typeof(int));

					switch(indexKind) {
						case IndexKind.ChunkIndexInQuery:
							paramData.RegisterAttribute(typeof(ChunkIndexInQuery));
							break;
						case IndexKind.EntityIndexInQuery:
							paramData.RegisterAttribute(typeof(EntityIndexInQuery));
							break;
						case IndexKind.EntityIndexInChunk:
							paramData.RegisterAttribute(typeof(EntityIndexInChunk));
							break;
					}
					parameters.Add(paramData);
				}
				method.parameters = parameters;
				//Generate code for execute logic
				method.code = CG.Flow(body);

				//Filters
				var queries = queryFilters.OrderBy(data => (int)data.filter).GroupBy(data => data.filter);
				foreach(var group in queries) {
					switch(group.Key) {
						case QueryFilter.WithAll:
							classBuilder.RegisterAttribute(typeof(WithAllAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
						case QueryFilter.WithAny:
							classBuilder.RegisterAttribute(typeof(WithAnyAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
						case QueryFilter.WithNone:
							classBuilder.RegisterAttribute(typeof(WithNoneAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
						case QueryFilter.WithChangeFilter:
							classBuilder.RegisterAttribute(typeof(WithChangeFilterAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
						case QueryFilter.WithAbsent:
							classBuilder.RegisterAttribute(typeof(WithAbsentAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
						case QueryFilter.WithDisabled:
							classBuilder.RegisterAttribute(typeof(WithDisabledAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
						case QueryFilter.WithPresent:
							classBuilder.RegisterAttribute(typeof(WithPresentAttribute), group.Select(item => CG.Value(item.type)).ToArray());
							break;
					}
				}

				if(options != EntityQueryOptions.Default) {
					classBuilder.RegisterAttribute(typeof(WithOptionsAttribute), options.CGValue());
				}

				//Register the generated function code
				classBuilder.RegisterFunction(method.GenerateCode());
				//Register the generated type code
				classData.RegisterNestedType(CG.WrapWithInformation(classBuilder.GenerateCode(), this));
			});
		}

		public string GenerateParallelIndex() {
			if(runOn != RunKind.ScheduleParallel)
				throw new Exception("Attemp to get parallel index on non Parallel job");
			return CG.GetVariableName(chunkIndexInQuery);
		}
	}
}

#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;

	class EntitiesForeachDrawer : NodeDrawer<Nodes.EntitiesForeach> {
		static readonly FilterAttribute componentFilter;
		static readonly FilterAttribute enableableComponentFilter;
		static readonly FilterAttribute sharedComponentFilter;

		static EntitiesForeachDrawer() {
			componentFilter = new FilterAttribute(typeof(IComponentData), typeof(IQueryTypeParameter)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = true,
				DisplayValueType = true,
			};
			enableableComponentFilter = new FilterAttribute(typeof(IEnableableComponent)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = true,
				DisplayValueType = true,
			};
			sharedComponentFilter = new FilterAttribute(typeof(ISharedComponentData)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = false,
				DisplayValueType = true,
			};
		}

		public override void DrawLayouted(DrawerOption option) {
			var node = GetNode(option);

			UInspector.Draw(option.property[nameof(node.runOn)]);
			if(node.runOn != Nodes.EntitiesForeach.RunKind.Run) {
				UInspector.Draw(option.property[nameof(node.burstCompile)]);
			}
			UInspector.Draw(option.property[nameof(node.options)]);
			UInspector.Draw(option.property[nameof(node.indexKind)]);

			uNodeGUI.DrawCustomList(node.datas, "Query",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var portName = EditorGUI.DelayedTextField(position, new GUIContent("Item " + index), value.name);
					if(portName != value.name) {
						value.name = portName;
						node.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					if(value.kind == Nodes.EntitiesForeach.DataKind.ReadOnlyComponent || value.kind == Nodes.EntitiesForeach.DataKind.ReadWriteComponent) {
						uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Component Type"), type => {
							value.type = type;
							node.Register();
							uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						}, componentFilter, option.unityObject);
					}
					else {
						uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Component Type"), type => {
							value.type = type;
							node.Register();
							uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						}, enableableComponentFilter, option.unityObject);
					}
					if(value.type.type?.IsValueType == true) {
						position.y += EditorGUIUtility.singleLineHeight + 1;
						uNodeGUIUtility.EditValue(position, new GUIContent("Accessibility"), value.kind, (val) => {
							value.kind = val;
							node.Register();
							uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
						});
					}
				},
				add: position => {
					option.RegisterUndo();
					node.datas.Add(new Nodes.EntitiesForeach.Data());
					node.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					node.datas.RemoveAt(index);
					node.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
				},
				elementHeight: index => {
					if(node.datas[index].type.type?.IsValueType == false) {
						return (EditorGUIUtility.singleLineHeight * 2) + 3;
					}
					return (EditorGUIUtility.singleLineHeight * 3) + 3;
				});

			uNodeGUI.DrawCustomList(node.queryFilters, "Query Filters",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var filter = (QueryFilter)EditorGUI.EnumPopup(position, new GUIContent("Filter " + index), value.filter);
					if(filter != value.filter) {
						value.filter = filter;
						node.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Component Type"), type => {
						value.type = type;
						node.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					}, componentFilter, option.unityObject);
				},
				add: position => {
					option.RegisterUndo();
					node.queryFilters.Add(new());
					node.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					node.queryFilters.RemoveAt(index);
					node.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 3;
				});

			uNodeGUI.DrawTypeList("With Shared Component Filter", node.withSharedComponentFilter, sharedComponentFilter, node.GetUnityObject());

			DrawInputs(option);
			DrawOutputs(option);
			DrawErrors(option);
		}
	}
}
#endif