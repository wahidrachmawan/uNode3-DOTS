using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;
using Unity.Entities;
using Unity.Collections;

namespace MaxyGames.UNode.Editors.Analyzer {
    class EntitiesAnalyzer : GraphAnalyzer {

		public override bool IsValidAnalyzerForNode(Type nodeType) {
			return nodeType == typeof(MultipurposeNode);
		}

		public override void CheckNodeErrors(ErrorAnalyzer analyzer, Node node) {
			//Skip when graph is macro graph
			if(node.nodeObject.graphContainer is IMacroGraph) return;

			MultipurposeNode mNode = node as MultipurposeNode;
			if(mNode.target.isAssigned == false) return;
			var members = mNode.target.GetMembers(false);
			if(members == null || members.Length == 0) return;
			if(mNode.target.startType == typeof(SystemAPI)) {
				if(IsSystemGraph(node.nodeObject.graphContainer) == false) {
					analyzer.RegisterError(node, "SystemAPI member use is not permitted outside of a system type (SystemBase or ISystem)");
				}
				else {
					var container = node.nodeObject.GetNodeInParent<NodeContainer>();
					if(container is Function function) {
						if(function.modifier.Static) {
							analyzer.RegisterError(node, "SystemAPI member use is not permitted in a static function");
						}
					}
				}
			}
		}

		private bool IsSystemGraph(IGraph graph) {
			if(graph is ECSGraph) return true;
			var inheritType = graph.GetGraphInheritType();
			if(inheritType != null) {
				if(inheritType == typeof(ValueType)) {
					if(graph is IInterfaceSystem interfaceSystem) {
						return interfaceSystem.Interfaces.Any(t => t == typeof(ISystem));
					}
				}
				else {
					return inheritType.IsCastableTo(typeof(SystemBase));
				}
			}
			return false;
		}
	}
}
