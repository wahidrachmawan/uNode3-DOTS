using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Data", "HasSingleton", scope = NodeScope.ECSGraphAndJob, outputs = new[] { typeof(bool) })]
    public class HasSingleton : ValueNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false)]
		public SerializedType type;

		private const PortAccessibility accessibility = PortAccessibility.ReadOnly;

		protected override void OnRegister() {
			base.OnRegister();
		}

		protected override Type ReturnType() => typeof(bool);

		public override string GetRichTitle() {
			return $"Has Singleton: {type.GetRichName()}";
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			{
				var code = ECSGraphUtility.GetValueCode(this, "has_singleton_" + type.type.Name, typeof(bool), mode => CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.HasSingleton), new[] { type.type }), PortAccessibility.ReadOnly);
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