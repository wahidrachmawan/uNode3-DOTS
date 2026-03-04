using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Data", "Get Buffer Lookup", scope = NodeScope.ECSGraphAndJob)]
    public class GetBufferLookup : ValueNode {
		[Filter(typeof(IBufferElementData), DisplayAbstractType = false)]
		public SerializedType type = SerializedType.None;
		public PortAccessibility accessibility = PortAccessibility.ReadWrite;

		protected override Type ReturnType() {
			if(type.isAssigned) {
				try {
					return ReflectionUtils.MakeGenericType(typeof(BufferLookup<>), type);
				}
				catch { }
			}
			return typeof(object);
		}

		public override string GetRichTitle() {
			return $"Get Buffer Lookup: {type.GetRichName()}";
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			var nm = ECSGraphUtility.GetBufferLookup(this, type, accessibility);
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