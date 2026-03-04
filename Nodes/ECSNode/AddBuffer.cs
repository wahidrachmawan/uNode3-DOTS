using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Flow", "Add Buffer", scope = NodeScope.ECSGraphAndJob)]
	public class AddBuffer : ECSNode {
		[Filter(typeof(IBufferElementData), DisplayAbstractType =false)]
		public SerializedType type;

		protected override void RegisterECSPort() {
			entity.SetTooltip("The entity to add component to.");
		}

		protected override string GenerateFlowCode() {
			return CG.Flow(
				GenerateFlowInvokeCode(nameof(EntityManager.AddBuffer), new[] { type.type }, entity.CGValue()),
				CG.FlowFinish(enter, exit)
			);
		}

		public override string GetTitle() {
			if(type?.isAssigned == true) {
				return $"Add Buffer: {type.prettyName}";
			}
			return base.GetTitle();
		}

		public override string GetRichTitle() {
			if(type?.isAssigned == true) {
				return $"Add Buffer: {type.GetRichName()}";
			}
			return base.GetRichTitle();
		}
	}
}