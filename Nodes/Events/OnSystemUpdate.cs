using System;
using UnityEngine;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
    [EventMenu("", "On Update", scope = NodeScope.ECSGraph)]
    public class OnSystemUpdate : BaseECSEvent {
		public ValueOutput deltaTime;
		public ValueOutput elapsedTime;
		public ValueOutput entityManager;

		protected override void OnRegister() {
			base.OnRegister();
			deltaTime = ValueOutput<float>(nameof(deltaTime));
			elapsedTime = ValueOutput<double>(nameof(elapsedTime));
			entityManager = ValueOutput<EntityManager>(nameof(entityManager));
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			if(deltaTime.hasValidConnections) {
				var value = CG.RegisterLocalVariable(nameof(deltaTime), typeof(float));
				CG.RegisterUserObject(value, deltaTime);
				CG.RegisterPort(deltaTime, () => value);
			}
			if(elapsedTime.hasValidConnections) {
				var value = CG.RegisterLocalVariable(nameof(elapsedTime), typeof(double));
				CG.RegisterUserObject(value, elapsedTime);
				CG.RegisterPort(elapsedTime, () => value);
			}
			if(entityManager.hasValidConnections) {
				CG.RegisterPort(entityManager, () => ECSGraphUtility.GetEntityManager(this));
			}
		}

		public override void GenerateEventCode() {
			DoGenerateCode(nameof(ISystem.OnUpdate));
		}

		public override string GenerateFlows() {
			return CG.Flow(
				CG.DeclareVariable(CG.GetUserObject<string>(deltaTime), CG.Access(typeof(SystemAPI)).CGAccess("Time").CGAccess("DeltaTime")),
				CG.DeclareVariable(CG.GetUserObject<string>(elapsedTime), CG.Access(typeof(SystemAPI)).CGAccess("Time").CGAccess("ElapsedTime")),
				base.GenerateFlows()
			);
		}
	}
}