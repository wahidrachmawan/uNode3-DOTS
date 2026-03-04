using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	public abstract class ECSNode : FlowNode {
		public ECSLogicExecutionMode executionMode = ECSLogicExecutionMode.Auto;

		[NonSerialized]
		public ValueInput entity;
		[NonSerialized]
		public ValueInput entityManager;
		[NonSerialized]
		public ValueInput entityCommandBuffer;
		[NonSerialized]
		public ValueInput parallelWriter;
		[NonSerialized]
		public ValueInput sortKey;

		protected override void OnRegister() {
			base.OnRegister();
			entity = ValueInput(nameof(entity), typeof(Entity));

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

		protected virtual bool IsValueECSNode => false;

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			if(executionMode == ECSLogicExecutionMode.Auto) {
				ECSGraphUtility.GetECSCommand(this, out var entities, out var commandName, out var commandType, isValue: IsValueECSNode);
				CG.RegisterUserObject<(INodeEntitiesForeach, string, Type)>((entities, commandName, commandType), ("ecs_command", this));
			}
		}

		protected string GenerateFlowInvokeCode(string functionName, Type[] genericParameterTypes, params string[] parameters) {
			return GenerateInvokeCode(mode => (functionName, genericParameterTypes, parameters)).AddSemicolon();
		}

		protected string GenerateFlowInvokeCode(Func<ECSLogicExecutionMode, (string name, Type[] genericParameterTypes, string[] parameters)> function) {
			return GenerateInvokeCode(function).AddSemicolon();
		}

		protected string GenerateInvokeCode(string functionName, Type[] genericParameterTypes, params string[] parameters) {
			return GenerateInvokeCode(mode => (functionName, genericParameterTypes, parameters));
		}

		protected virtual string GenerateInvokeCode(Func<ECSLogicExecutionMode, (string name, Type[] genericParameterTypes, string[] parameters)> function) {
			if(executionMode == ECSLogicExecutionMode.Auto) {
				var (entities, commandName, commandType) = CG.GetUserObject<(INodeEntitiesForeach, string, Type)>(("ecs_command", this));
				//ECSGraphUtility.GetECSCommand(this, out var entities, out var commandName, out var commandType);
				if(commandType == typeof(EntityManager)) {
					var (name, gtype, parameters) = function(ECSLogicExecutionMode.Run);
					return commandName.CGInvoke(name, gtype, parameters);
				}
				else if(commandType == typeof(EntityCommandBuffer)) {
					var (name, gtype, parameters) = function(ECSLogicExecutionMode.Schedule);
					return commandName.CGInvoke(name, gtype, parameters);
				}
				else if(commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
					var (name, gtype, parameters) = function(ECSLogicExecutionMode.ScheduleParallel);
					return commandName.CGInvoke(name, gtype, new[] { entities.GenerateParallelIndex() }.Concat(parameters).ToArray());
				}
				else {
					throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
				}
			} else {
				switch(executionMode) {
					case ECSLogicExecutionMode.Run: {
						var commandName = CG.GeneratePort(entityManager);
						var (name, gtype, parameters) = function(ECSLogicExecutionMode.Run);
						return commandName.CGInvoke(name, gtype, parameters);
					}
					case ECSLogicExecutionMode.Schedule: {
						var commandName = CG.GeneratePort(entityCommandBuffer);
						var (name, gtype, parameters) = function(ECSLogicExecutionMode.Schedule);
						return commandName.CGInvoke(name, gtype, parameters);
					}
					case ECSLogicExecutionMode.ScheduleParallel: {
						var commandName = CG.GeneratePort(parallelWriter);
						var (name, gtype, parameters) = function(ECSLogicExecutionMode.ScheduleParallel);
						return commandName.CGInvoke(name, gtype, new[] { sortKey.CGValue() }.Concat(parameters).ToArray());
					}
				}
			}
			throw new NotImplementedException();
		}
	}
}