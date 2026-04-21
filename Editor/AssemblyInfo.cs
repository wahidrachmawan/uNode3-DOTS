using MaxyGames.UNode;
using Unity.Entities;
using Unity.Jobs;
using System;
using System.Reflection;

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
	}
}