using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("ECS", "Entity Query Builder", scope = NodeScope.ECSGraph, outputs = new[] {typeof(EntityQuery)})]
	public class EntityQueryBuilder : ValueNode, IGeneratorPrePostInitializer {
		public List<ECSQueryFilter> queryFilters = new List<ECSQueryFilter>();
		public List<SerializedType> withSharedComponentFilter = new List<SerializedType>();
		public EntityQueryOptions options = EntityQueryOptions.Default;

		protected override Type ReturnType() {
			return typeof(EntityQuery);
		}

		public override string GetRichTitle() {
			return "Entity Query Builder";
		}

		protected override string GenerateValueCode() {
			string iterator =  typeof(SystemAPI).CGInvoke(nameof(SystemAPI.QueryBuilder));

			var queries = queryFilters.OrderBy(data => (int)data.filter).GroupBy(data => data.filter);
			foreach(var group in queries) {
				iterator = iterator.CGInvoke(group.Key.ToString(), group.Select(item => item.type.type).ToArray());
			}

			if(withSharedComponentFilter.Count > 0) {
				iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithSharedComponentFilter), withSharedComponentFilter.Select(item => item.type).ToArray());
			}
			if(options != EntityQueryOptions.Default) {
				iterator = iterator.CGInvoke(nameof(QueryEnumerable<int>.WithOptions), options.CGValue());
			}
			return iterator.CGInvoke(nameof(SystemAPIQueryBuilder.Build));
		}
	}
}

#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;
	using UnityEngine.UIElements;
	using UnityEditor.UIElements;

	class EntityQueryBuilderDrawer : NodeDrawer<Nodes.EntityQueryBuilder> {
		static readonly FilterAttribute componentFilter;
		static readonly FilterAttribute sharedComponentFilter;

		static EntityQueryBuilderDrawer() {
			componentFilter = new FilterAttribute(typeof(IComponentData), typeof(IQueryTypeParameter)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = true,
				DisplayValueType = true,
			};
			sharedComponentFilter = new FilterAttribute(typeof(ISharedComponentData)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = false,
				DisplayValueType = true,
			};
		}

		public override void DrawLayouted(DrawerOption option) {
			var node = GetNode(option);

			UInspector.Draw(option.property[nameof(node.options)]);

			uNodeGUI.DrawCustomList(node.queryFilters, "Query Filters",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var filter = (QueryFilter)EditorGUI.EnumPopup(position, new GUIContent("Filter " + index), value.filter);
					if(filter != value.filter) {
						value.filter = filter;
						node.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Component Type"), type => {
						value.type = type;
						node.Register();
						uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
					}, componentFilter, option.unityObject);
				},
				add: position => {
					option.RegisterUndo();
					node.queryFilters.Add(new());
					node.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					node.queryFilters.RemoveAt(index);
					node.Register();
					uNodeGUIUtility.GUIChanged(node, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 3;
				});

			uNodeGUI.DrawTypeList("With Shared Component Filter", node.withSharedComponentFilter, sharedComponentFilter, node.GetUnityObject());

			DrawInputs(option);
			DrawOutputs(option);
			DrawErrors(option);
		}
	}
}
#endif