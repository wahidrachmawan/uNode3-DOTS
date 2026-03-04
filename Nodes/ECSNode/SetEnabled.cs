using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Flow", "Set Enabled", scope = NodeScope.ECSGraphAndJob)]
    public class SetEnabled : ECSNode {
		[NonSerialized]
		public ValueInput value;

		protected override void RegisterECSPort() {
			base.RegisterECSPort();
			entity.SetTooltip("The entity to Adds or removes the Disabled component");
			value = ValueInput(nameof(value), typeof(bool));
			value.SetTooltip("The enable value.");
		}

		protected override string GenerateFlowCode() {
			return CG.Flow(
				GenerateFlowInvokeCode(nameof(EntityManager.SetEnabled), Type.EmptyTypes, entity.CGValue(), value.CGValue()),
				CG.FlowFinish(enter, exit)
			);
		}
	}
}