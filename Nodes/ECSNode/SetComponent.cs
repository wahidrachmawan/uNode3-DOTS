using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Flow", "Set Component", scope = NodeScope.ECSGraphAndJob)]
    public class SetComponent : ECSNode {
		[NonSerialized]
		public ValueInput component;

		protected override void RegisterECSPort() {
			base.RegisterECSPort();
			entity.SetTooltip("The entity to set component to.");
			component = ValueInput(nameof(component), typeof(IComponentData));
			component.SetTooltip("The component value to sets, will use default value when empty.");
		}

		protected override string GenerateFlowCode() {
			var contents = GenerateFlowInvokeCode(
				mode => {
					switch(mode) {
						case ECSLogicExecutionMode.Run: {
							return (nameof(EntityManager.SetComponentData),
								new[] { component.ValueType },
								new[] { entity.CGValue(), component.CGValue() });
						}
						case ECSLogicExecutionMode.Schedule: {
							return (nameof(EntityCommandBuffer.SetComponent),
								new[] { component.ValueType },
								new[] { entity.CGValue(), component.CGValue() });
						}
						case ECSLogicExecutionMode.ScheduleParallel: {
							return (nameof(EntityCommandBuffer.ParallelWriter.SetComponent),
								new[] { component.ValueType },
								new[] { entity.CGValue(), component.CGValue() });
						}
					}
					throw null;
				});
			return CG.Flow(contents, CG.FlowFinish(enter, exit));
		}

		public override void CheckError(ErrorAnalyzer analyzer) {
			base.CheckError(analyzer);
			if(component.isAssigned) {
				if(component.ValueType.IsValueType == false) {
					analyzer.RegisterError(this, "Component must be assigned to Value Type ( struct )");
				}
			}
		}
	}
}