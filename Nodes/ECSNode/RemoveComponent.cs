using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Flow", "RemoveComponent", scope = NodeScope.ECSGraphAndJob, hasFlowInput = true, hasFlowOutput = true, inputs = new[] { typeof(Entity), typeof(NativeArray<Entity>), typeof(EntityQuery), typeof(SystemHandle) })]
	public class RemoveComponent : ECSNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false)]
		public SerializedType type = SerializedType.None;

		protected override void OnRegister() {
			base.OnRegister();
			entity.SetTooltip("The entity to remove the component.");
			entity.filter = new FilterAttribute(typeof(Entity), typeof(NativeArray<Entity>), typeof(EntityQuery), typeof(SystemHandle));
		}

		protected override string GenerateFlowCode() {
			return CG.Flow(
				GenerateFlowInvokeCode(nameof(EntityManager.RemoveComponent), new[] { type.type }, entity.CGValue()),
				CG.FlowFinish(enter, exit)
			);
		}

		public override string GetRichTitle() {
			return $"Remove Component: {type.GetRichName()}";
		}

		public override void CheckError(ErrorAnalyzer analyzer) {
			base.CheckError(analyzer);
			analyzer.CheckValue(type, nameof(type), this);
		}
	}
}