using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Data", "GetComponentLookup", scope = NodeScope.ECSGraphAndJob, outputs = new[] { typeof(ComponentLookup<>) })]
    public class GetComponentLookup : ValueNode {
		[Filter(typeof(IComponentData), DisplayAbstractType = false)]
		public SerializedType type = SerializedType.None;
		public PortAccessibility accessibility = PortAccessibility.ReadWrite;

		protected override Type ReturnType() {
			if(type.isAssigned) {
				try {
					return ReflectionUtils.MakeGenericType(typeof(ComponentLookup<>), type);
				}
				catch { }
			}
			return typeof(object);
		}

		public override string GetRichTitle() {
			return $"Get Component Lookup: {type.GetRichName()}";
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			var nm = ECSGraphUtility.GetComponentLookup(this, type, accessibility);
			CG.RegisterUserObject<Func<string>>(() => {
				return nm;
			}, ("value", this));
		}

		protected override string GenerateValueCode() {
			var func = CG.GetUserObject<Func<string>>(("value", this));
			return func?.Invoke();
		}
	}
}