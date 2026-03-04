using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using MaxyGames.UNode.Nodes;
using Unity.Collections;

namespace MaxyGames.UNode {
	public interface INodeEntitiesForeach {
		List<ECSJobVariable> JobVariables { get; }
		ECSLogicExecutionMode LogicExecutionMode { get; }
		string GenerateParallelIndex();

		public void AddJobVariable(ECSJobVariable variable) {
			var data = JobVariables;
			if(data.Any(item => item.owner == variable.owner || item.name == variable.name) == false) {
				data.Add(variable);
			}
		}
	}

	public enum QueryFilter {
		WithAll = 0,
		WithAny = 1,
		WithNone = 2,
		WithChangeFilter = 3,
		WithAbsent = 4,
		WithDisabled = 5,
		WithPresent = 6,
	}

	public enum QueryBuilderFilter {
		WithAll = 0,
		WithAny = 1,
		WithNone = 2,
		WithChangeFilter = 3,
		WithAbsent = 4,
		WithDisabled = 5,
		WithPresent = 6,
	}

	[Serializable]
	public class ECSQueryFilter {
		public QueryFilter filter;
		public SerializedType type = typeof(IComponentData);
	}

	[Serializable]
	public class ECSQueryBuilderFilter {
		public QueryBuilderFilter filter;
		public SerializedType type = typeof(IComponentData);
	}

	public interface IECSNode {

	}

	public enum ECSLogicExecutionMode {
		Auto = 0,
		Run = 1,
		Schedule = 2,
		ScheduleParallel = 3,
	}

	public enum LookupExecutionKind {
		Auto,
		SystemAPI,
		EntityManager,
		Lookup,
	}

	public class ECSJobVariable {
		public string name;
		public Type type;
		public List<AttributeData> attributes = new();
		public Func<string> value;

		public object owner;
		public object userData;
	}

	public static class ECSGraphUtility {
		public static string GetEntityManager(NodeObject nodeObject) {
			if(CG.isGenerating) {
				var result = CG.GetUserObject<string>((nodeObject.graphContainer, "EntityManager", typeof(EntityManager)));
				if(result == null) {
					result = CG.GenerateNewName("entityManager");
					CG.RegisterUserObject(result, (nodeObject.graphContainer, "EntityManager", typeof(EntityManager)));

					CG.RegisterPostClassManipulator(data => {
						var mdata = data.GetMethodData(nameof(ISystem.OnUpdate));
						if(mdata == null)
							throw new Exception($"There's no {nameof(ISystem.OnUpdate)} event/function in a graph");

						var graph = nodeObject.graphContainer as ECSGraph;
						var contents = CG.DeclareVariable(result, CG.Access(graph.CodegenStateName).CGAccess("EntityManager"));
						mdata.AddCode(contents, -1000);
					});
				}
				return result;
			}
			return null;
		}

		public static string RegisterVariable(object owner, string name, Type type, string value) {
			if(CG.isGenerating) {
				var result = CG.GetUserObject<string>((owner, "Vars", type));
				if(result == null) {
					result = CG.GenerateNewName(name);
					CG.RegisterUserObject(result, (owner, "Vars", type));

					CG.RegisterPostClassManipulator(data => {
						data.AddVariable(new CG.VData(result, type, autoCorrection: false));

						var mdata = data.GetMethodData(nameof(ISystem.OnCreate));
						if(mdata == null)
							throw new Exception($"There's no {nameof(ISystem.OnCreate)} event/function in a graph");

						var contents = CG.Set(result, value);
						mdata.AddCode(contents, -10);
					});
				}
				return result;
			}
			return null;
		}

		public static string GetECBSingleton<T>(NodeObject nodeObject) where T : IECBSingleton {
			return GetECBSingleton(typeof(T), nodeObject);
		}

		/// <summary>
		/// Retrieves the appropriate ECS command information for the specified node object, including the associated entities
		/// foreach context, command name, and command type.
		/// </summary>
		/// <param name="nodeObject">The node object for which to retrieve ECS command information.</param>
		/// <param name="entitiesForeach">When this method returns, contains the entities foreach context associated with the node object, if found.</param>
		/// <param name="commandName">When this method returns, contains the name of the ECS command variable.</param>
		/// <param name="commandType">When this method returns, contains the type of the ECS command.</param>
		/// <param name="autoRegisterVariableInJob">true to automatically register the command variable in the job; otherwise, false.</param>
		public static void GetECSCommand(NodeObject nodeObject, out INodeEntitiesForeach entitiesForeach, out string commandName, out Type commandType, bool autoRegisterVariableInJob = true, bool isValue = false) {
			commandName = null;
			commandType = null;
			var conenctions = CG.Nodes.FindAllConnections(nodeObject, false, false, true, isValue);
			INodeEntitiesForeach entities = null;
			foreach(var node in conenctions) {
				if(entities == null && node.node is INodeEntitiesForeach) {
					entities = node.node as INodeEntitiesForeach;
					break;
				}
			}
			if(entities == null) {
				entities = nodeObject.GetObjectInParent(element => {
					if(element is INodeEntitiesForeach || element is NodeObject node && node.node is INodeEntitiesForeach) {
						return true;
					}
					return false;
				}) as INodeEntitiesForeach;
			}
			entitiesForeach = entities;
			if(entities != null) {
				var executionMode = entitiesForeach.LogicExecutionMode;
				if(executionMode == ECSLogicExecutionMode.ScheduleParallel || executionMode == ECSLogicExecutionMode.Auto && 
					entities is IJobEntityContainer jobEntity && 
					jobEntity.indexKind.HasFlags(IJobEntityContainer.IndexKind.ChunkIndexInQuery)) {

					var ecbName = GetECBSingleton<EndSimulationEntityCommandBufferSystem.Singleton>(nodeObject);
					if(autoRegisterVariableInJob) {
						var variables = entities.JobVariables;
						if(variables != null) {
							entities.AddJobVariable(new ECSJobVariable() {
								name = ecbName,
								type = typeof(EntityCommandBuffer.ParallelWriter),
								value = () => ecbName.CGInvoke(nameof(EntityCommandBuffer.AsParallelWriter)),
								owner = typeof(EndSimulationEntityCommandBufferSystem.Singleton),
							});
						}
					}
					commandName = ecbName;
					commandType = typeof(EntityCommandBuffer.ParallelWriter);
					return;
				}
				if(executionMode == ECSLogicExecutionMode.Auto || executionMode == ECSLogicExecutionMode.Schedule) {
					var ecbName = GetECBSingleton<EndSimulationEntityCommandBufferSystem.Singleton>(nodeObject);
					if(autoRegisterVariableInJob) {
						var variables = entities.JobVariables;
						if(variables != null) {
							entities.AddJobVariable(new ECSJobVariable() {
								name = ecbName,
								type = typeof(EntityCommandBuffer),
								value = () => ecbName,
								owner = typeof(EndSimulationEntityCommandBufferSystem.Singleton),
							});
						}
					}
					commandName = ecbName;
					commandType = typeof(EntityCommandBuffer);
					return;
				}
			}
			BaseGraphEvent evt = null;
			foreach(var node in conenctions) {
				if(node.node is BaseGraphEvent) {
					evt = node.node as BaseGraphEvent;
					break;
				}
			}
			if(evt != null) {
				commandName = GetEntityManager(evt);
				commandType = typeof(EntityManager);
			}
			else {
				var ecbName = GetEntityManager(nodeObject);
				if(autoRegisterVariableInJob) {
					var variables = entities.JobVariables;
					if(variables != null) {
						entities.AddJobVariable(new ECSJobVariable() {
							name = ecbName,
							type = typeof(EntityManager),
							value = () => ecbName,
							owner = typeof(EntityManager),
						});
					}
				}
				commandName = ecbName;
				commandType = typeof(EntityManager);
			}
		}

		public static string GetValueCode(NodeObject nodeObject, string name, Type type, Func<ECSLogicExecutionMode, string> value, PortAccessibility accessibility) {
			GetECSCommand(nodeObject, out var entities, out var commandName, out var commandType, autoRegisterVariableInJob: false, isValue: true);
			if(commandType == typeof(EntityManager)) {
				return value(ECSLogicExecutionMode.Run);
			}
			else if(commandType == typeof(EntityCommandBuffer) || commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
				var variables = entities.JobVariables;
				if(variables != null) {
					var nm = name;
					return GetVariableCodeValue(
						nm,
						type,
						value(commandType == typeof(EntityCommandBuffer) ? ECSLogicExecutionMode.Schedule : ECSLogicExecutionMode.ScheduleParallel),
						variables,
						accessibility,
						commandType == typeof(EntityCommandBuffer.ParallelWriter));
				}
				else {
					throw null;
				}
			}
			else {
				throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
			}
		}

		public static string GetComponentLookup(NodeObject nodeObject, Type ComponentType, PortAccessibility accessibility) {
			GetECSCommand(nodeObject, out var entities, out var commandName, out var commandType, autoRegisterVariableInJob: false, isValue: true);
			if(commandType == typeof(EntityManager)) {
				return CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.GetComponentLookup),
					new[] { ComponentType },
					accessibility == PortAccessibility.ReadOnly ? true.CGValue() : null);
			}
			else if(commandType == typeof(EntityCommandBuffer) || commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
				var variables = entities.JobVariables;
				if(variables != null) {
					return GetComponentLookup(commandType, variables, accessibility, commandType == typeof(EntityCommandBuffer.ParallelWriter));
				}
				else {
					throw null;
				}
			}
			else {
				throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
			}
		}

		public static string GetComponentLookup(Type componentType, List<ECSJobVariable> variables, PortAccessibility accessibility, bool parallel = false) {
			var nm = componentType.Name + "Lookup";
			var lookupType = typeof(ComponentLookup<>).MakeGenericType(componentType);
			return GetVariableCodeValue(
				nm, 
				lookupType,
				CG.Invoke(
					typeof(SystemAPI), 
					nameof(SystemAPI.GetComponentLookup),
					new[] { componentType },
					accessibility == PortAccessibility.ReadOnly ? CG.Value(true) : null), 
				variables, 
				accessibility, 
				parallel);
		}

		public static string GetBufferLookup(NodeObject nodeObject, Type ComponentType, PortAccessibility accessibility) {
			GetECSCommand(nodeObject, out var entities, out var commandName, out var commandType, autoRegisterVariableInJob: false, isValue: true);
			if(commandType == typeof(EntityManager)) {
				return CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.GetBufferLookup),
					new[] { ComponentType },
					accessibility == PortAccessibility.ReadOnly ? true.CGValue() : null);
			}
			else if(commandType == typeof(EntityCommandBuffer) || commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
				var variables = entities.JobVariables;
				if(variables != null) {
					return GetBufferLookup(commandType, variables, accessibility, commandType == typeof(EntityCommandBuffer.ParallelWriter));
				}
				else {
					throw null;
				}
			}
			else {
				throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
			}
		}

		public static string GetBufferLookup(Type bufferType, List<ECSJobVariable> variables, PortAccessibility accessibility, bool parallel = false) {
			var nm = bufferType.Name + "Lookup";
			var lookupType = typeof(BufferLookup<>).MakeGenericType(bufferType);
			return GetVariableCodeValue(
				nm,
				lookupType,
				CG.Invoke(
					typeof(SystemAPI),
					nameof(SystemAPI.GetBufferLookup),
					new[] { bufferType },
					accessibility == PortAccessibility.ReadOnly ? CG.Value(true) : null),
				variables,
				accessibility,
				parallel);
		}

		private static string GetVariableCodeValue(string variableName, Type variableType, string valueCode, List<ECSJobVariable> variables, PortAccessibility accessibility = PortAccessibility.ReadWrite, bool parallel = false) {
			var nm = variableName;
			var variable = variables.FirstOrDefault(v => v.name == nm && object.ReferenceEquals(v.owner, variableType));
			if(variable == null) {
				variable = new ECSJobVariable() {
					name = nm,
					type = variableType,
					owner = variableType,
				};
				variables.Add(variable);
			}
			bool isValid = variable.userData is PortAccessibility;
			if(isValid == false) {
				variable.userData = accessibility;
				variable.value = () => valueCode;
				switch(accessibility) {
					case PortAccessibility.ReadOnly:
						variable.attributes.Add(new AttributeData(typeof(ReadOnlyAttribute)));
						break;
					case PortAccessibility.WriteOnly:
						variable.attributes.Add(new AttributeData(typeof(WriteOnlyAttribute)));
						break;
				}
				if(parallel) {
					variable.attributes.Add(new AttributeData(typeof(NativeDisableParallelForRestrictionAttribute)));
				}
			}
			else {
				var oldAccessibility = (PortAccessibility)variable.userData;
				if(oldAccessibility != PortAccessibility.ReadWrite) {
					if(accessibility == PortAccessibility.ReadWrite ||
						accessibility == PortAccessibility.ReadOnly && oldAccessibility == PortAccessibility.WriteOnly ||
						accessibility == PortAccessibility.WriteOnly && oldAccessibility == PortAccessibility.ReadOnly) {
						variable.userData = PortAccessibility.ReadWrite;
						variable.value = () => valueCode;
						variable.attributes.Clear();
					}
				}
				if(parallel && variable.attributes.Any(a => a.attributeType == typeof(NativeDisableParallelForRestrictionAttribute)) == false) {
					variable.attributes.Add(new AttributeData(typeof(NativeDisableParallelForRestrictionAttribute)));
				}
			}
			return nm;
		}

		public static string GetECBSingleton(Type ecbType, NodeObject nodeObject) {
			if(CG.isGenerating) {
				var result = CG.GetUserObject<string>((nodeObject.graphContainer, "ECB-Singleton", ecbType));
				if(result == null) {
					result = CG.GenerateNewName("ecb");
					CG.RegisterUserObject(result, (nodeObject.graphContainer, "ECB-Singleton", ecbType));

					CG.RegisterPostClassManipulator(data => {
						var mdata = data.GetMethodData(nameof(ISystem.OnUpdate));
						if(mdata == null)
							throw new Exception($"There's no {nameof(ISystem.OnUpdate)} event/function in a graph");
						var graph = nodeObject.graphContainer as ECSGraph;
						var contents = CG.DeclareVariable(
							result,
							typeof(SystemAPI)
							.CGInvoke(nameof(SystemAPI.GetSingleton), new[] { ecbType }, null)
							.CGInvoke("CreateCommandBuffer", graph.CodegenStateName.CGAccess(nameof(SystemState.WorldUnmanaged)))
						);
						mdata.AddCode(contents, -1000);
					});
				}
				return result;
			}
			return null;
		}


		private static Dictionary<Type, bool> unmanagedTypeMap = new();
		public static bool IsFullyUnmanaged(Type type) {
			if(unmanagedTypeMap.TryGetValue(type, out var result)) {
				return result;
			}
			if(!type.IsValueType || type.IsPrimitive || type.IsPointer || type.IsEnum) {
				unmanagedTypeMap[type] = false;
				return false;
			}

			foreach(var field in type.GetFields(
				BindingFlags.Instance |
				BindingFlags.NonPublic |
				BindingFlags.Public)) {
				if(!IsFullyUnmanaged(field.FieldType)) {
					unmanagedTypeMap[type] = false;
					return false;
				}
			}

			unmanagedTypeMap[type] = true;
			return true;
		}
	}
}