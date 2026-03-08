using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Data", "InstantiateEntity", scope = NodeScope.ECSGraphAndJob, inputs = new[] { typeof(Entity) }, outputs = new[] { typeof(Entity) })]
	public class InstantiateEntity : ValueNode {
		[NonSerialized]
		public ValueInput sourceEntity;
		[NonSerialized]
		public ValueInput entityManager;
		[NonSerialized]
		public ValueInput entityCommandBuffer;
		[NonSerialized]
		public ValueInput parallelWriter;
		[NonSerialized]
		public ValueInput sortKey;

		public ECSLogicExecutionMode executionMode = ECSLogicExecutionMode.Auto;

		protected override void OnRegister() {
			base.OnRegister();

			sourceEntity = ValueInput(nameof(sourceEntity), typeof(Entity));
			sourceEntity.SetTooltip("The entity to clone. Must entity with prefab tag.");
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

		protected override Type ReturnType() => typeof(Entity);

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			if(executionMode == ECSLogicExecutionMode.Auto) {
				ECSGraphUtility.GetECSCommand(this, out var entities, out var commandName, out var commandType, isValue: true);
				if(commandType == typeof(EntityManager)) {
					CG.RegisterUserObject<Func<string>>(() => {
						return commandName.CGInvoke(nameof(EntityManager.Instantiate), sourceEntity.CGValue());
					}, ("ecb", this));
				}
				else if(commandType == typeof(EntityCommandBuffer)) {
					CG.RegisterUserObject<Func<string>>(() => {
						return commandName.CGInvoke(nameof(EntityCommandBuffer.Instantiate), sourceEntity.CGValue());
					}, ("ecb", this));
				}
				else if(commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
					CG.RegisterUserObject<Func<string>>(() => {
						return commandName.CGInvoke(nameof(EntityCommandBuffer.ParallelWriter.Instantiate), entities.GenerateParallelIndex(), sourceEntity.CGValue());
					}, ("ecb", this));
				}
				else {
					throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
				}
			}
		}

		protected override string GenerateValueCode() {
			switch(executionMode) {
				case ECSLogicExecutionMode.Auto: {
					var func = CG.GetUserObject<Func<string>>(("ecb", this));
					return func?.Invoke();
				}
				case ECSLogicExecutionMode.Run: {
					var commandName = CG.GeneratePort(entityManager);
					return commandName.CGInvoke(nameof(EntityManager.Instantiate), sourceEntity.CGValue());
				}
				case ECSLogicExecutionMode.Schedule: {
					var commandName = CG.GeneratePort(entityCommandBuffer);
					return commandName.CGInvoke(nameof(EntityCommandBuffer.Instantiate), sourceEntity.CGValue());
				}
				case ECSLogicExecutionMode.ScheduleParallel: {
					var commandName = CG.GeneratePort(parallelWriter);
					return commandName.CGInvoke(nameof(EntityCommandBuffer.ParallelWriter.Instantiate), sortKey.CGValue(), sourceEntity.CGValue());
				}
			}
			throw null;
		}
	}
}