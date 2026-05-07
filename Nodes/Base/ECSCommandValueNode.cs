using UnityEngine;
using System;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	public abstract class ECSCommandValueNode : ValueNode {
		[Tooltip("The execution mode of the node. Auto will automatically determine the execution mode based on the context of the node. Run will execute the logic immediately, Schedule will schedule the logic to be executed later, ScheduleParallel will schedule the logic to be executed in parallel.")]
		public ECSLogicExecutionMode executionMode = ECSLogicExecutionMode.Auto;
		[Tooltip("If true, the execution will always use EntityCommandBuffer to schedule the execution later to safety execute from structural changes.")]
		[Hide(nameof(executionMode), ECSLogicExecutionMode.Auto, false)]
		public bool alwaysUseSchedule = true;
		[Hide(nameof(executionMode), ECSLogicExecutionMode.Auto, false)]
		public DefaultCommandBufferData defaultCommandBuffer;

		[NonSerialized]
		public ValueInput entityManager;
		[NonSerialized]
		public ValueInput entityCommandBuffer;
		[NonSerialized]
		public ValueInput parallelWriter;
		[NonSerialized]
		public ValueInput sortKey;

		protected virtual bool AlwaysUseSchedule => alwaysUseSchedule;
		protected virtual bool AutoRegisterVariableInJob => true;

		protected (INodeEntitiesForeach, string, Type) GetECSCommandData() => CG.GetUserObject<(INodeEntitiesForeach, string, Type)>(("ecs_command", this));

		protected override void OnRegister() {
			base.OnRegister();

			RegisterECSPort();

			switch(executionMode) {
				case ECSLogicExecutionMode.Run: {
					entityManager = ValueInput(nameof(entityManager), typeof(EntityManager));
					break;
				}
				case ECSLogicExecutionMode.Schedule: {
					entityCommandBuffer = ValueInput(nameof(entityCommandBuffer), typeof(EntityCommandBuffer));
					break;
				}
				case ECSLogicExecutionMode.ScheduleParallel: {
					parallelWriter = ValueInput(nameof(parallelWriter), typeof(EntityCommandBuffer.ParallelWriter));
					sortKey = ValueInput(nameof(sortKey), typeof(int));
					break;
				}
			}
		}

		/// <summary>
		/// Register additional ports
		/// </summary>
		protected virtual void RegisterECSPort() { }

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			if(executionMode == ECSLogicExecutionMode.Auto) {
				ECSGraphUtility.GetECSCommand(this, out var entities, out var commandName, out var commandType,
					isValue: true,
					alwaysUseSchedule: AlwaysUseSchedule,
					autoRegisterVariableInJob: AutoRegisterVariableInJob,
					ecbType: defaultCommandBuffer.GetEntityCommandBufferSingletonType(nodeObject.graphContainer as ECSGraph));
				CG.RegisterUserObject<(INodeEntitiesForeach, string, Type)>((entities, commandName, commandType), ("ecs_command", this));
			}
		}

		protected virtual string GenerateInvokeCode(Func<(ECSLogicExecutionMode mode, string commandName, string entityIndex), string> function) {
			if(executionMode == ECSLogicExecutionMode.Auto) {
				var (entities, commandName, commandType) = GetECSCommandData();
				if(commandType == typeof(EntityManager)) {
					return function((ECSLogicExecutionMode.Run, commandName, null));
				}
				else if(commandType == typeof(EntityCommandBuffer)) {
					return function((ECSLogicExecutionMode.Schedule, commandName, null));
				}
				else if(commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
					return function((ECSLogicExecutionMode.ScheduleParallel, commandName, entities.GenerateParallelIndex()));
				}
				else {
					throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
				}
			}
			else {
				switch(executionMode) {
					case ECSLogicExecutionMode.Run: {
						var commandName = CG.GeneratePort(entityManager);
						return function((ECSLogicExecutionMode.Run, commandName, null));
					}
					case ECSLogicExecutionMode.Schedule: {
						var commandName = CG.GeneratePort(entityCommandBuffer);
						return function((ECSLogicExecutionMode.Schedule, commandName, null));
					}
					case ECSLogicExecutionMode.ScheduleParallel: {
						var commandName = CG.GeneratePort(parallelWriter);
						return function((ECSLogicExecutionMode.ScheduleParallel, commandName, sortKey.CGValue()));
					}
				}
			}
			throw new NotImplementedException();
		}
	}

}