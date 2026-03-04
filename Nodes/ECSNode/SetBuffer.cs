using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using System.Linq;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Flow", "Set Buffer", scope = NodeScope.ECSGraphAndJob)]
	public class SetBuffer : ECSNode {
		[Filter(typeof(IBufferElementData), DisplayAbstractType = false)]
		public SerializedType type = SerializedType.None;

		[Serializable]
		public class PortData {
			[HideInInspector]
			public string id = uNodeUtility.GenerateUID();
			[NonSerialized]
			public ValueInput port;
		}
		public List<PortData> buffers = new List<PortData>() { new() };

		protected override void RegisterECSPort() {
			entity.SetTooltip("The entity to set buffers to.");
			for(int i = 0; i < buffers.Count; i++) {
				buffers[i].port = ValueInput(buffers[i].id, () => type.type).SetName("element " + (i + 1));
			}
		}

		protected override string GenerateFlowCode() {
			var contents = GenerateFlowInvokeCode(
				mode => {
					if(mode == ECSLogicExecutionMode.Run) {
						return (nameof(EntityManager.GetBuffer),
							new[] { type.type },
							new[] { entity.CGValue() });
					}
					return (nameof(EntityCommandBuffer.SetBuffer),
						new[] { type.type },
						new[] { entity.CGValue() });
				});
			var nm = CG.GenerateNewName("buffer");
			contents = CG.Flow(
				CG.DeclareVariable(nm, contents), 
				nm.CGFlowInvoke(nameof(IList.Clear)), 
				CG.Flow(buffers.Where(x => x.port.isAssigned).Select(x => nm.CGInvoke(nameof(IList.Add), x.port.CGValue())))
			);
			return CG.Flow(contents, CG.FlowFinish(enter, exit));
		}

		public override string GetTitle() {
			if(type?.isAssigned == true) {
				return $"Set Buffer: {type.prettyName}";
			}
			return base.GetTitle();
		}

		public override string GetRichTitle() {
			if(type?.isAssigned == true) {
				return $"Set Buffer: {type.GetRichName()}";
			}
			return base.GetRichTitle();
		}
	}
}