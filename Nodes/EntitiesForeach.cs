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
		}

		public enum DataKind {
			ReadOnlyComponent,
			ReadWriteComponent,
			AspectOrOther,
		}
		public class Data {
			public string id = uNodeUtility.GenerateUID();

			public string name;
			public SerializedType type = typeof(IComponentData);
			public DataKind kind = DataKind.ReadWriteComponent;

			[NonSerialized]
			public ValueOutput port;
		}
		public List<Data> datas = new List<Data>() { new Data() };

		public RunKind runOn = RunKind.Run;
		public bool burstCompile;
		public EntityQueryOptions options = EntityQueryOptions.Default;

		public List<ECSQueryFilter> queryFilters = new List<ECSQueryFilter>();
		public List<SerializedType> withSharedComponentFilter = new List<SerializedType>();

		public ValueOutput entity { get; private set; }
		public FlowOutput body { get; private set; }
		public List<ECSJobVariable> JobVariables => CG.GetUserObject<List<ECSJobVariable>>(nodeObject);

		public ECSLogicExecutionMode LogicExecutionMode => ECSLogicExecutionMode.Run;

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
				data.port = ValueOutput(data.id, data.type, PortAccessibility.ReadWrite).SetName(!string.IsNullOrEmpty(data.name) ? data.name : ("Item" + (i + 1)));
				data.port.canSetValue = () => data.kind == DataKind.ReadWriteComponent;
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
				switch(data.kind) {
					case DataKind.ReadOnlyComponent:
						if(runOn != RunKind.Run) goto default;
						vName = vName.CGAccess("ValueRO");
						CG.RegisterPort(data.port, () => vName);
						break;
					case DataKind.ReadWriteComponent:
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
				}
			}
		}

		public override string GetTitle() {
			return "Foreach Entities: " + string.Join(", ", datas.Select(d => d.type.prettyName)).Wrap();
		}

		public override string GetRichTitle() {
			return "Foreach Entities: " + string.Join(", ", datas.Select(d => uNodeUtility.WrapTextWithTypeColor(d.type.prettyName, d.type.type))).Wrap();
		}

		private string GenerateScheduleCode() {
			string className = CG.GetUserObject<string>(this);
			var job = CG.GenerateNewName("job");

			var variables = CG.GetUserObject<List<ECSJobVariable>>(nodeObject);

			return CG.Flow(
				CG.DeclareVariable("job", CG.New(className, null, variables.Select(v => v.name.CGSetValue(v.value())))),
				job.CGFlowInvoke(nameof(IJobEntityExtensions.Schedule)),
				CG.FlowFinish(enter, exit)
			);
		}

		protected override string GenerateFlowCode() {
			if(runOn == RunKind.Schedule) {
				return GenerateScheduleCode();
			}
			List<string> variableNames = datas.Select(item => CG.GetVariableName(item.port)).ToList();
			string iterator =  typeof(SystemAPI).CGInvoke(
					nameof(SystemAPI.Query),
					datas.Select(item => {
						var itemType = item.type.type;
						switch(item.kind) {
							case DataKind.AspectOrOther:
								return itemType;
							case DataKind.ReadOnlyComponent:
								return typeof(RefRO<>).MakeGenericType(itemType);
							case DataKind.ReadWriteComponent:
								return typeof(RefRW<>).MakeGenericType(itemType);
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
			return CG.Flow(
				CG.Foreach(null, string.Join(", ", variableNames).Wrap(), iterator, CG.Flow(body)),
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
				//TODO: add support for index in IJobEntity
				//if(index != null && index.hasValidConnections) {
				//	CG.MPData paramData = new CG.MPData(CG.GetVariableName(index), typeof(int));

				//	switch(indexKind) {
				//		case IndexKind.Chunk:
				//			paramData.RegisterAttribute(typeof(EntityIndexInChunk));
				//			break;
				//		case IndexKind.ChunkAndEntity:
				//			paramData.RegisterAttribute(typeof(EntityIndexInChunk));
				//			paramData.RegisterAttribute(typeof(EntityIndexInQuery));
				//			break;
				//		case IndexKind.Entity:
				//			paramData.RegisterAttribute(typeof(EntityIndexInQuery));
				//			break;
				//	}
				//	parameters.Add(paramData);
				//}
				for(int i = 0; i < variableNames.Count; i++) {
					var data = datas[i];
					switch(data.kind) {
						case DataKind.ReadOnlyComponent:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.In));
							break;
						case DataKind.ReadWriteComponent:
							parameters.Add(new CG.MPData(variableNames[i], data.type, RefKind.Ref));
							break;
						case DataKind.AspectOrOther:
							parameters.Add(new CG.MPData(variableNames[i], data.type));
							break;
					}
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

				//var withAll = queryFilters.Where(item => item.filter == QueryFilter.WithAll).ToList();
				//var withAny = queryFilters.Where(item => item.filter == QueryFilter.WithAny).ToList();
				//var withNone = queryFilters.Where(item => item.filter == QueryFilter.WithNone).ToList();
				//var withChangeFilter = queryFilters.Where(item => item.filter == QueryFilter.WithChangeFilter).ToList();

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

		public string GenerateParallelIndex() {
			throw new NotImplementedException();
		}
	}
}

#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;
	using UnityEditor.Experimental.GraphView;
	using UnityEngine.UIElements;
	using UnityEditor.UIElements;

	class SystemAPIForeachDrawer : NodeDrawer<Nodes.EntitiesForeach> {
		static readonly FilterAttribute componentFilter;
		static readonly FilterAttribute sharedComponentFilter;

		static SystemAPIForeachDrawer() {
			componentFilter = new FilterAttribute(typeof(IComponentData), typeof(IQueryTypeParameter)) {
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
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Type"), type => {
						value.type = type;
						if(type.HasImplementInterface(typeof(IComponentData))) {
							if(value.kind == Nodes.EntitiesForeach.DataKind.AspectOrOther) {
								value.kind = Nodes.EntitiesForeach.DataKind.ReadWriteComponent;
							}
						}
						else {
							value.kind = Nodes.EntitiesForeach.DataKind.AspectOrOther;
						}
						node.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					}, componentFilter, option.unityObject);
					if(value.kind != Nodes.EntitiesForeach.DataKind.AspectOrOther) {
						position.y += EditorGUIUtility.singleLineHeight + 1;
						uNodeGUIUtility.EditValue(position, new GUIContent("Accessibility"), value.kind, (val) => {
							if(val != Nodes.EntitiesForeach.DataKind.AspectOrOther) {
								value.kind = val;
								node.Register();
								uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
							}
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
					if(node.datas[index].kind != Nodes.EntitiesForeach.DataKind.AspectOrOther) {
						return (EditorGUIUtility.singleLineHeight * 3) + 3;
					}
					return (EditorGUIUtility.singleLineHeight * 2) + 2;
				});

			var withAll = node.queryFilters.Where(item => item.filter == QueryFilter.WithAll).ToList();
			var withAny = node.queryFilters.Where(item => item.filter == QueryFilter.WithAny).ToList();
			var withNone = node.queryFilters.Where(item => item.filter == QueryFilter.WithNone).ToList();
			var withChangeFilter = node.queryFilters.Where(item => item.filter == QueryFilter.WithChangeFilter).ToList();

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