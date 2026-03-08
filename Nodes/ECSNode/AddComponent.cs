using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Flow", "AddComponent", scope = NodeScope.ECSGraphAndJob, hasFlowInput = true, hasFlowOutput = true, inputs = new[] { typeof(Entity), typeof(IComponentData) })]
	public class AddComponent : ECSNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false)]
		public SerializedType type;

		[NonSerialized]
		public ValueInput component;

		protected override void RegisterECSPort() {
			entity.SetTooltip("The entity to add component to.");
			component = ValueInput(nameof(component), () => type ?? typeof(IComponentData));
			component.MarkAsOptional();
			component.SetTooltip("The component value to sets, will use default value when empty.");
		}

		protected override string GenerateFlowCode() {
			var contents = GenerateFlowInvokeCode(
				mode => {
					if(mode == ECSLogicExecutionMode.Run) {
						if(component.isAssigned) {
							if(ECSGraphUtility.IsFullyUnmanaged(component.ValueType)) {
								return (nameof(EntityManager.AddComponentData),
									new[] { component.ValueType },
									new[] { entity.CGValue(), component.CGValue() });
							}
							else {
								return (nameof(EntityManager.AddComponentObject),
									new[] { component.ValueType },
									new[] { entity.CGValue(), component.CGValue() });
							}
						}
					}
					return (nameof(EntityManager.AddComponent),
						new[] { component?.isAssigned == true ? component.ValueType : type.type },
						new[] { entity.CGValue(), component?.isAssigned == true ? component.CGValue() : null });
				});
			return CG.Flow(contents, CG.FlowFinish(enter, exit));
		}

		public override string GetTitle() {
			if(component?.isAssigned == true) {
				return $"Add Component: {component.ValueType.PrettyName()}";
			}
			if(type?.isAssigned == true) {
				return $"Add Component: {type.prettyName}";
			}
			return base.GetTitle();
		}

		public override string GetRichTitle() {
			if(component?.isAssigned == true) {
				return $"Add Component: {uNodeUtility.GetRichTypeName(component.ValueType.PrettyName(), false)}";
			}
			if(type?.isAssigned == true) {
				return $"Add Component: {type.GetRichName()}";
			}
			return base.GetRichTitle();
		}
	}
}