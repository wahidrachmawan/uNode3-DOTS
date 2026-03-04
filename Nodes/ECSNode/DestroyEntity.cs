using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Flow", "Destroy Entity", scope = NodeScope.ECSGraphAndJob)]
	public class DestroyEntity : ECSNode {

		protected override string GenerateFlowCode() {
			return CG.Flow(
				GenerateFlowInvokeCode(nameof(EntityCommandBuffer.DestroyEntity), null, entity.CGValue()),
				CG.FlowFinish(enter, exit)
			);
		}
	}
}