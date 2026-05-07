using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS/Data", "InstantiateEntity", scope = NodeScope.ECSGraphAndJob, inputs = new[] { typeof(Entity) }, outputs = new[] { typeof(Entity) })]
	public class InstantiateEntity : ECSCommandValueNode {
		[NonSerialized]
		public ValueInput sourceEntity;

		protected override void RegisterECSPort() {
			sourceEntity = ValueInput(nameof(sourceEntity), typeof(Entity));
			sourceEntity.SetTooltip("The entity to clone. Must entity with prefab tag.");
		}

		protected override Type ReturnType() => typeof(Entity);

		protected override string GenerateValueCode() {
			return GenerateInvokeCode(data => {
				return data.commandName.CGInvoke(nameof(EntityManager.Instantiate), data.entityIndex, sourceEntity.CGValue());
			});
		}
	}
}