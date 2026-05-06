using System;
using UnityEngine;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode {
	public abstract class BaseECSEvent : BaseGraphEvent {
		public ValueOutput state { get; set; }

		protected override void OnRegister() {
			state = ValueOutput(nameof(state), typeof(SystemState));
			base.OnRegister();
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			var graph = nodeObject.graphContainer as ECSGraph;
			CG.RegisterPort(state, () => graph.CodegenStateName);
		}
	}
}