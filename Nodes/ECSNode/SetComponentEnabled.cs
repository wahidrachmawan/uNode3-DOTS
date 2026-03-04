using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Flow", "Set Component Enabled", scope = NodeScope.ECSGraphAndJob)]
    public class SetComponentEnabled : ECSNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false)]
		public SerializedType type;

		[NonSerialized]
		public ValueInput value;

		protected override void RegisterECSPort() {
			base.RegisterECSPort();
			entity.SetTooltip("The entity to set component to.");
			value = ValueInput(nameof(value), typeof(bool));
			value.SetTooltip("The enable value.");
		}

		protected override string GenerateFlowCode() {
			return CG.Flow(
				GenerateFlowInvokeCode(nameof(EntityManager.SetComponentEnabled), new[] { type.type }, entity.CGValue(), value.CGValue()),
				CG.FlowFinish(enter, exit)
			);
		}
	}
}