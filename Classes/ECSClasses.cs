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

	public enum DataAccessor {
		ReadOnly,
		ReadWrite,
		WriteOnly,
		None,
	}

	public enum DefaultCommandBufferType {
		/// <summary>
		/// Will relative to graph default command buffer, but if the graph also default it is same as BeginSimulation.
		/// </summary>
		Default = 0,
		/// <summary>
		/// Create a new command buffer and playback at the end of update
		/// </summary>
		ImmediatePlayback,
		/// <summary>
		/// Start of Simulation group
		/// </summary>
		BeginSimulation,
		/// <summary>
		/// End of Simulation group
		/// </summary>
		EndSimulation,
		/// <summary>
		/// Start of Initialization group
		/// </summary>
		BeginInitialization,
		/// <summary>
		/// End of Initialization group
		/// </summary>
		EndInitialization,
		/// <summary>
		/// Start of FixedStep group
		/// </summary>
		BeginFixedStepSimulation,
		/// <summary>
		/// End of FixedStep group
		/// </summary>
		EndFixedStepSimulation,
		/// <summary>
		/// Start of Presentation group
		/// </summary>
		BeginPresentation,
		///// <summary>
		///// End of Presentation group
		///// </summary>
		//EndPresentation,
		//TODO: add support for custom entity command buffer
		///// <summary>
		///// A custom entity command buffer
		///// </summary>
		//Custom,
	}

	public struct DefaultCommandBufferData {
		public DefaultCommandBufferType kind;
		//[Filter(typeof(EntityCommandBufferSystem))]
		//public SerializedType type;

		public Type GetEntityCommandBufferSingletonType(ECSGraph graph) {
			switch(kind) {
				case DefaultCommandBufferType.Default: {
					if(graph != null && graph.defaultCommandBuffer.kind != DefaultCommandBufferType.Default) {
						return graph.defaultCommandBuffer.GetEntityCommandBufferSingletonType(null);
					}
					goto case DefaultCommandBufferType.BeginSimulation;
				}
				case DefaultCommandBufferType.BeginSimulation:
					return typeof(BeginSimulationEntityCommandBufferSystem.Singleton);
				case DefaultCommandBufferType.EndSimulation:
					return typeof(EndSimulationEntityCommandBufferSystem.Singleton);
				case DefaultCommandBufferType.BeginInitialization:
					return typeof(BeginInitializationEntityCommandBufferSystem.Singleton);
				case DefaultCommandBufferType.EndInitialization:
					return typeof(EndInitializationEntityCommandBufferSystem.Singleton);
				case DefaultCommandBufferType.BeginFixedStepSimulation:
					return typeof(BeginFixedStepSimulationEntityCommandBufferSystem.Singleton);
				case DefaultCommandBufferType.EndFixedStepSimulation:
					return typeof(EndFixedStepSimulationEntityCommandBufferSystem.Singleton);
				case DefaultCommandBufferType.BeginPresentation:
					return typeof(BeginPresentationEntityCommandBufferSystem.Singleton);
					//case DefaultCommandBufferType.EndPresentation:
					//	return typeof(EndPresentationEntityCommandBufferSystem.Singleton);
			}
			return typeof(EntityCommandBuffer);
		}
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

	[Serializable]
	public class SystemOrderData {
		public SystemOrderKind kind;
		public ECSGraph graph;
		public SerializedType type = SerializedType.None;

		public string Name {
			get {
				if(graph != null) {
					return graph.name;
				}
				else if(type != null) {
					return type.prettyName;
				}
				return "(none)";
			}
		}
	}

	public enum SystemOrderKind {
		UpdateInGroup,
		UpdateAfter,
		UpdateBefore,
		CreateAfter,
		CreateBefore,
	}

	public enum ECSLogicExecutionMode {
		Auto = 0,
		Run = 1,
		Schedule = 2,
		ScheduleParallel = 3,
	}

	public enum LookupExecutionKind {
		Auto = 0,
		SystemAPI = 1 << 0,
		EntityManager = 1 << 1,
		Lookup = 1 << 2,
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
		/// <summary>
		/// Retrieves or creates a unique name for the EntityManager associated with the specified node object during code
		/// generation.
		/// </summary>
		/// <param name="nodeObject">The node object for which to retrieve or create the EntityManager name.</param>
		/// <returns>A unique EntityManager name if code generation is in progress; otherwise, null.</returns>
		/// <exception cref="Exception">Thrown if the ISystem.OnUpdate method is not found in the graph during code generation.</exception>
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

		public static string RegisterLocalVariable(object owner, string name, Type type, Func<string> value) {
			if(CG.isGenerating) {
				var result = CG.GetUserObject<string>((owner, "LocalVars", type));
				if(result == null) {
					result = CG.GenerateNewName(name);
					CG.RegisterUserObject(result, (owner, "LocalVars", type));

					CG.RegisterPostClassManipulator(data => {
						data.AddVariable(new CG.VData(result, type, autoCorrection: false));

						var mdata = data.GetMethodData(nameof(ISystem.OnUpdate));
						if(mdata == null)
							throw new Exception($"There's no {nameof(ISystem.OnUpdate)} event/function in a graph");

						var contents = CG.Set(result, value());
						mdata.AddCode(contents, -10);
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

		/// <summary>
		/// Retrieves the appropriate ECS command information for the specified node object, including the associated entities
		/// foreach context, command name, and command type.
		/// </summary>
		/// <param name="nodeObject">The node object for which to retrieve ECS command information.</param>
		/// <param name="entitiesForeach">When this method returns, contains the entities foreach context associated with the node object, if found.</param>
		/// <param name="commandName">When this method returns, contains the name of the ECS command variable.</param>
		/// <param name="commandType">When this method returns, contains the type of the ECS command.</param>
		/// <param name="autoRegisterVariableInJob">true to automatically register the command variable in the job; otherwise, false.</param>
		public static void GetECSCommand(NodeObject nodeObject, out INodeEntitiesForeach entitiesForeach, out string commandName, out Type commandType, bool autoRegisterVariableInJob = true, bool isValue = false, bool alwaysUseSchedule = false, Type ecbType = null) {
			if(ecbType == null) {
				if(nodeObject.graphContainer is ECSGraph graph) {
					ecbType = graph.defaultCommandBuffer.GetEntityCommandBufferSingletonType(null);
				}
				else {
					ecbType = typeof(BeginSimulationEntityCommandBufferSystem.Singleton);
				}
			}
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

					var ecbName = GetECBSingleton(ecbType, nodeObject);
					if(autoRegisterVariableInJob) {
						var variables = entities.JobVariables;
						if(variables != null) {
							entities.AddJobVariable(new ECSJobVariable() {
								name = ecbName,
								type = typeof(EntityCommandBuffer.ParallelWriter),
								value = () => ecbName.CGInvoke(nameof(EntityCommandBuffer.AsParallelWriter)),
								owner = ecbType,
							});
						}
					}
					commandName = ecbName;
					commandType = typeof(EntityCommandBuffer.ParallelWriter);
					return;
				}
				if(executionMode == ECSLogicExecutionMode.Auto || executionMode == ECSLogicExecutionMode.Schedule || alwaysUseSchedule) {
					var ecbName = GetECBSingleton(ecbType, nodeObject);
					if(autoRegisterVariableInJob) {
						var variables = entities.JobVariables;
						if(variables != null) {
							entities.AddJobVariable(new ECSJobVariable() {
								name = ecbName,
								type = typeof(EntityCommandBuffer),
								value = () => ecbName,
								owner = ecbType,
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
				if(alwaysUseSchedule) {
					commandName = GetECBSingleton(ecbType, nodeObject);
					commandType = typeof(EntityCommandBuffer);
				}
				else {
					commandName = GetEntityManager(evt);
					commandType = typeof(EntityManager);
				}
			}
			else {
				if(alwaysUseSchedule) {
					var ecbName = GetECBSingleton(ecbType, nodeObject);
					if(autoRegisterVariableInJob) {
						var variables = entities.JobVariables;
						if(variables != null) {
							entities.AddJobVariable(new ECSJobVariable() {
								name = ecbName,
								type = typeof(EntityCommandBuffer),
								value = () => ecbName,
								owner = ecbType,
							});
						}
					}
					commandName = ecbName;
					commandType = typeof(EntityCommandBuffer);
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
		public static string GetComponentTypeHandle(NodeObject nodeObject, Type ComponentType, PortAccessibility accessibility) {
			GetECSCommand(nodeObject, out var entities, out var commandName, out var commandType, autoRegisterVariableInJob: false, isValue: true);
			if(commandType == typeof(EntityManager)) {
				return CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.GetComponentTypeHandle),
					new[] { ComponentType },
					accessibility == PortAccessibility.ReadOnly ? true.CGValue() : null);
			}
			else if(commandType == typeof(EntityCommandBuffer) || commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
				var variables = entities.JobVariables;
				if(variables != null) {
					return GetComponentTypeHandle(ComponentType, variables, accessibility, commandType == typeof(EntityCommandBuffer.ParallelWriter));
				}
				else {
					throw null;
				}
			}
			else {
				throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
			}
		}

		public static string GetComponentTypeHandle(Type componentType, List<ECSJobVariable> variables, PortAccessibility accessibility, bool parallel = false) {
			var nm = componentType.Name + "TypeHandle";
			var lookupType = typeof(ComponentTypeHandle<>).MakeGenericType(componentType);
			return GetVariableCodeValue(
				nm,
				lookupType,
				CG.Invoke(
					typeof(SystemAPI),
					nameof(SystemAPI.GetComponentTypeHandle),
					new[] { componentType },
					accessibility == PortAccessibility.ReadOnly ? CG.Value(true) : null),
				variables,
				accessibility,
				parallel);
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
					return GetComponentLookup(ComponentType, variables, accessibility, commandType == typeof(EntityCommandBuffer.ParallelWriter));
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

		/// <summary>
		/// Retrieves the ECB singleton identifier for the specified type and node object.
		/// </summary>
		/// <typeparam name="T">The type implementing IECBSingleton.</typeparam>
		/// <param name="nodeObject">The node object associated with the ECB singleton.</param>
		/// <returns>The ECB singleton identifier as a string.</returns>
		public static string GetECBSingleton<T>(NodeObject nodeObject) where T : IECBSingleton {
			return GetECBSingleton(typeof(T), nodeObject);
		}

		/// <summary>
		/// Retrieves or creates a unique identifier for an ECB singleton associated with the specified type and node object
		/// during code generation.
		/// </summary>
		/// <param name="ecbType">The type of the ECB singleton.</param>
		/// <param name="nodeObject">The node object containing the graph context.</param>
		/// <returns>A unique identifier for the ECB singleton if code generation is active; otherwise, null.</returns>
		/// <exception cref="Exception">Thrown if the OnUpdate method is not found in the graph during post-class manipulation.</exception>
		public static string GetECBSingleton(Type ecbType, NodeObject nodeObject) {
			if(CG.isGenerating) {
				if(ecbType == null)
					throw new ArgumentNullException(nameof(ecbType));
				if(ecbType == typeof(EntityCommandBuffer)) {
					var result = CG.GetUserObject<string>((nodeObject.graphContainer, "ECB-Singleton", ecbType));
					if(result == null) {
						result = CG.GenerateNewName("ecb");
						CG.RegisterUserObject(result, (nodeObject.graphContainer, "ECB-Singleton", ecbType));

						//We get it from this to ensure we register the entity manager
						var em = GetEntityManager(nodeObject);

						CG.RegisterPostClassManipulator(data => {
							var mdata = data.GetMethodData(nameof(ISystem.OnUpdate));
							if(mdata == null)
								throw new Exception($"There's no {nameof(ISystem.OnUpdate)} event/function in a graph");
							var graph = nodeObject.graphContainer as ECSGraph;
							var contents = CG.DeclareVariable(
								result,
								CG.New(typeof(EntityCommandBuffer), CG.Value(Allocator.Temp))
							);
							mdata.AddCode(contents, -1000);

							mdata.AddCode(
								CG.Flow(
									result.CGFlowInvoke(nameof(EntityCommandBuffer.Playback), em),
									result.CGFlowInvoke(nameof(EntityCommandBuffer.Dispose))
								),
								int.MaxValue
							);
						});
					}
					return result;
				}
				else {
					if(ecbType.IsCastableTo(typeof(IECBSingleton)) == false) {
						throw new Exception("EntityCommandBuffer type should be implement: " + typeof(IECBSingleton).FullName);
					}
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
			}
			return null;
		}


		private static Dictionary<Type, bool> unmanagedTypeMap = new();
		/// <summary>
		/// Determines whether the specified type is a value type composed entirely of unmanaged types.
		/// </summary>
		/// <remarks>A type is considered fully unmanaged if it is a value type and all its fields are also fully
		/// unmanaged. Primitive types, pointers, and enums are not considered fully unmanaged by this method.</remarks>
		/// <param name="type">The type to evaluate for unmanaged composition.</param>
		/// <returns>true if the type is a value type and all its fields are unmanaged types; otherwise, false.</returns>
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