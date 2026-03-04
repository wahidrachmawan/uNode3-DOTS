using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Data", "Get Singleton", scope = NodeScope.ECSGraphAndJob)]
    public class GetSingleton : ValueNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false, DisplayReferenceType =false)]
		public SerializedType type = SerializedType.None;

		private const PortAccessibility accessibility = PortAccessibility.ReadOnly;

		protected override void OnRegister() {
			base.OnRegister();
		}

		protected override Type ReturnType() => type;

		public override string GetRichTitle() {
			return $"Get Singleton: {type.GetRichName()}";
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			{
				var code = ECSGraphUtility.GetValueCode(this, "singleton_" + type.type.Name, type, mode => CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.GetSingleton), new[] { type.type }), PortAccessibility.ReadOnly);
				CG.RegisterUserObject<Func<string>>(() => {
					return code;
				}, ("code", this));
			}
		}

		protected override string GenerateValueCode() {
			var func = CG.GetUserObject<Func<string>>(("code", this));
			return func?.Invoke();
		}
	}
}