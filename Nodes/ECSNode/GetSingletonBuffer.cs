using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
    [NodeMenu("ECS/Data", "GetSingletonBuffer", scope = NodeScope.ECSGraphAndJob, outputs = new[] { typeof(DynamicBuffer<>) })]
    public class GetSingletonBuffer : ValueNode {
		[Filter(typeof(IBufferElementData), DisplayAbstractType = false, DisplayReferenceType =false)]
		public SerializedType type = SerializedType.None;

		private const PortAccessibility accessibility = PortAccessibility.ReadOnly;

		protected override void OnRegister() {
			base.OnRegister();
		}

		protected override Type ReturnType() {
			if(type.isAssigned) {
				try {
					return ReflectionUtils.MakeGenericType(typeof(DynamicBuffer<>), type);
				}
				catch { }
			}
			return typeof(object);
		}

		public override string GetRichTitle() {
			return $"Get Singleton Buffer: {type.GetRichName()}";
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			{
				var code = ECSGraphUtility.GetValueCode(this, "singletonBuffer_" + type.type.Name, ReturnType(), mode => CG.Invoke(typeof(SystemAPI), nameof(SystemAPI.GetSingletonBuffer), new[] { type.type }, CG.Value(true)), PortAccessibility.ReadOnly);
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