using MaxyGames.UNode;
using Unity.Entities;
using Unity.Jobs;
using System;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;

[assembly: TypeIcons.RegisterIconGuid(typeof(LocalToWorld), "26a9c54ad253e6e48bd566e538386094")]
[assembly: TypeIcons.RegisterIconGuid(typeof(LocalTransform), "0d9e7052d10f7ec449636f01da8a6f96")]
[assembly: TypeIcons.RegisterIconGuid(typeof(Parent), "d35a09adc97b7a14a92953fbaeebe8d7")]

[assembly: TypeIcons.RegisterIconGuid(typeof(Entity), "1649a5c109aae6642a01b83de611d8a4")]
[assembly: TypeIcons.RegisterIconGuid(typeof(EntityQuery), "ba164113c38602b4da035d272adcd15d")]
[assembly: TypeIcons.RegisterIconGuid(typeof(SystemState), "ea8e3d30657bf844e9cbea6a2ea03c8d")]
[assembly: TypeIcons.RegisterIconGuid(typeof(EntityManager), "288170a47a07b7d419f38ede984483d9")]
[assembly: TypeIcons.RegisterIconGuid(typeof(JobHandle), "99e6b2b1a7b4e6947bbae8c61e1948ee")]
[assembly: TypeIcons.RegisterIconGuid(typeof(IBufferElementData), "cec858d3726e3434baa09488467e52e6", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(ISharedComponentData), "ccfa41989efd67d4c80096414c6b1ea7", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(IComponentData), "f33df7761a0710444857e481741f8399", true, typeFilter = typeof(MaxyGames.UNode.Editors.TypeIconFilter.ComponentTagFilter))]
[assembly: TypeIcons.RegisterIconGuid(typeof(IComponentData), "9fa9fe76f5bf71c46b34ce6940f2a344", true, typeFilter = typeof(MaxyGames.UNode.Editors.TypeIconFilter.ManagedComponentFilter))]
[assembly: TypeIcons.RegisterIconGuid(typeof(IComponentData), "8962928cffe5d134b8c64abfe1ef709f", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(ISystem), "9b88b1c74c6a1244b97f851a405560c9", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(IJobEntity), "bc396893cb8045f4da8ffa1d71e478da", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(IJobChunk), "06485293297100e45b9d127ec43e7ae3", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(ComponentSystemBase), "9b88b1c74c6a1244b97f851a405560c9", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(World), "ecd7ff483d6525148a2edf22cfa6034e", true)]
[assembly: TypeIcons.RegisterIconGuid(typeof(EntityArchetype), "84ea9d5414135e7428961d0e3609c7e7")]
[assembly: TypeIcons.RegisterIconGuid(null, null, typeIconSelector = typeof(MaxyGames.UNode.Editors.TypeIconFilter.TypeIconSelector))]

[assembly: TypeIcons.RegisterIconGuid(typeof(bool2), "39e23c2649b464f45b26f3f696dafe58")]
[assembly: TypeIcons.RegisterIconGuid(typeof(bool2x2), "93d9e29a9fd5be34888fa6916d9016c2")]
[assembly: TypeIcons.RegisterIconGuid(typeof(bool2x3), "b3079250e3e6787438e7345ce801ccc2")]
[assembly: TypeIcons.RegisterIconGuid(typeof(bool2x4), "57e7b0bf8993c1846b4705be899c35ad")]
[assembly: TypeIcons.RegisterIconGuid(typeof(bool3), "dd5b8996171242645ace8ed4c7dc277d")]
[assembly: TypeIcons.RegisterIconGuid(typeof(bool4), "e7e055c3f77e8c64f83093e81e050bfb")]
[assembly: TypeIcons.RegisterIconGuid(typeof(float2), "8e435ee5b0797824c9774f4072f94170")]
[assembly: TypeIcons.RegisterIconGuid(typeof(float3), "54a371dc96e86724fad7b24ac83383ba")]
[assembly: TypeIcons.RegisterIconGuid(typeof(float4), "5ff09c764c2256e49b49a0b4a34b7e44")]
[assembly: TypeIcons.RegisterIconGuid(typeof(int2), "5138491f87f9574488e5396c90ce7cea")]
[assembly: TypeIcons.RegisterIconGuid(typeof(int3), "3bdb7826b4cb4a0489f0a00586f0f35a")]
[assembly: TypeIcons.RegisterIconGuid(typeof(int4), "254dc6d6291ccb2458309f8e393a5852")]

namespace MaxyGames.UNode.Editors {
	static class TypeIconFilter {
		public sealed class ComponentTagFilter : TypeIcons.ITypeIconFilter {
			public bool IsValid(Type type) {
				if(type.IsValueType == false) return false;
				return EditorReflectionUtility.GetFields(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.NonPublic).Length == 0;
				//return type.IsValueType && TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(type)).TypeSize == 1;
			}
		}
		public sealed class ManagedComponentFilter : TypeIcons.ITypeIconFilter {
			public bool IsValid(Type type) {
				return type.IsClass;
			}
		}
		public sealed class TypeIconSelector : TypeIcons.ITypeIconSelector {
			public Texture nativeArrayIcon;
			public Texture nativeListIcon;

			public TypeIconSelector() {
				nativeArrayIcon = uNodeEditorUtility.Icons.GetIconByGuid("4275ba9e04141814d864f80ad1142814");
				nativeListIcon = uNodeEditorUtility.Icons.GetIconByGuid("10348a0ba96fb9041a4ddc5e6a2b3f82");
			}

			public Texture GetIcon(Type type) {
				if(type.IsValueType && type.IsGenericType) {
					if(type.IsConstructedGenericType) {
						type = type.GetGenericTypeDefinition();
						if(type == typeof(NativeArray<>)) {
							return nativeArrayIcon;
						}
						else if(type == typeof(NativeList<>)) {
							return nativeListIcon;
						}
					}
					else if(type == typeof(NativeArray<>)) {
						return nativeArrayIcon;
					}
					else if(type == typeof(NativeList<>)) {
						return nativeListIcon;
					}
				}
				return null;
			}
		}
	}
}