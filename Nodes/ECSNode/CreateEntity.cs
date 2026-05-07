using UnityEngine;
using System;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Data", "CreateEntity", scope = NodeScope.ECSGraphAndJob, outputs = new[] { typeof(Entity) })]
	public class CreateEntity : ECSCommandValueNode {
		protected override Type ReturnType() => typeof(Entity);

		protected override string GenerateValueCode() {
			return GenerateInvokeCode(data => {
				return data.commandName.CGInvoke(nameof(EntityManager.CreateEntity), data.entityIndex);
			});
		}
	}
}