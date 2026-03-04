using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Data", "Has Component", scope = NodeScope.ECSGraphAndJob)]
    public class HasComponent : ValueNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false)]
		public SerializedType type;
		public LookupExecutionKind executionKind = LookupExecutionKind.Auto;

		private const PortAccessibility accessibility = PortAccessibility.ReadOnly;

		[NonSerialized]
		public ValueInput entity;
		[NonSerialized]
		public ValueInput entityManager;
		[NonSerialized]
		public ValueInput lookup;

		protected override void OnRegister() {
			base.OnRegister();
			entity = ValueInput(nameof(entity), typeof(Entity));
			switch(executionKind) {
				case LookupExecutionKind.EntityManager: {
					entityManager = ValueInput(nameof(entityManager), typeof(EntityManager));
					break;
				}
				case LookupExecutionKind.Lookup: {
					lookup = ValueInput(nameof(lookup), () => type != null ? ReflectionUtils.MakeGenericType(typeof(ComponentLookup<>), type) : typeof(ComponentLookup<>));
					break;
				}
			}
		}

		protected override Type ReturnType() => typeof(bool);

		public override string GetRichTitle() {
			return $"Has Component: {type.GetRichName()}";
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			if(executionKind == LookupExecutionKind.Auto) {
				ECSGraphUtility.GetECSCommand(this, out var entities, out var commandName, out var commandType, autoRegisterVariableInJob: false, isValue: true);
				if(commandType == typeof(EntityManager)) {
					CG.RegisterUserObject<Func<string>>(() => {
						return CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.HasComponent), new[] { type.type }, entity.CGValue());
					}, ("ecb", this));
				}
				else if(commandType == typeof(EntityCommandBuffer) || commandType == typeof(EntityCommandBuffer.ParallelWriter)) {
					var variables = entities.JobVariables;
					var lookupType = typeof(ComponentLookup<>).MakeGenericType(type);
					if(variables != null) {
						var nm = ECSGraphUtility.GetComponentLookup(commandType, variables, accessibility, commandType == typeof(EntityCommandBuffer.ParallelWriter));
						CG.RegisterUserObject<Func<string>>(() => {
							return nm.CGInvoke(nameof(ComponentLookup<Asset>.HasComponent), entity.CGValue());
						}, ("ecb", this));
					}
				}
				else {
					throw new Exception("Invalid context of node with Auto execution mode. It should be used inside a system On Update event, IJobEntity or IJobChunk graph.");
				}
			}
		}

		protected override string GenerateValueCode() {
			if(executionKind == LookupExecutionKind.Auto) {
				var func = CG.GetUserObject<Func<string>>(("ecb", this));
				return func?.Invoke();
			}
			else if(executionKind == LookupExecutionKind.SystemAPI) {
				return typeof(SystemAPI).CGInvoke(nameof(SystemAPI.HasComponent), new[] { type.type }, entity.CGValue());
			}
			else if(executionKind == LookupExecutionKind.EntityManager) {
				return entityManager.CGValue().CGInvoke(nameof(EntityManager.HasComponent), new[] { type.type }, entity.CGValue());
			}
			else if(executionKind == LookupExecutionKind.Lookup) {
				return lookup.CGValue().CGInvoke(nameof(ComponentLookup<Asset>.HasComponent), entity.CGValue());
			}
			return null;
		}
	}
}