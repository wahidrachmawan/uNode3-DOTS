using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;
using Unity.Entities;

namespace MaxyGames.UNode.Editors {
    class ECSGraphCreator : GraphCreator {
		public override string menuName => "DOTS/ECS Graph";
		
		public ECSGraphCreator() {
			graphUsingNamespaces = new List<string>() {
				"Unity.Burst", 
				"Unity.Entities", 
				"Unity.Transforms",
				"Unity.Mathematics",
				"Unity.Collections",
			};
		}

		protected virtual ECSGraph CreateGraph() {
			return ScriptableObject.CreateInstance<ECSGraph>();
		}

		public override Object CreateAsset() {
			var graph = CreateGraph();
			graph.@namespace = graphNamespaces;
			graph.UsingNamespaces.Clear();
			graph.UsingNamespaces.AddRange(graphUsingNamespaces);
			graph.GraphData.graphLayout = graphLayout;
			{
				graph.GraphData.mainGraphContainer.AddChild(
					new NodeObject(new Nodes.OnSystemCreate()) {
						position = new Rect(0, 0, 0, 0)
					}
				);
			}
			{
				graph.GraphData.mainGraphContainer.AddChild(
					new NodeObject(new Nodes.OnSystemUpdate()) {
						position = new Rect(200, 0, 0, 0)
					}
				);
			}
			{
				graph.GraphData.mainGraphContainer.AddChild(
					new NodeObject(new Nodes.OnSystemDestroy()) {
						position = new Rect(400, 0, 0, 0)
					}
				);
			}
			return graph;
		}

		public override void OnGUI() {
			DrawNamespaces();
			DrawUsingNamespaces();
			DrawGraphLayout();
		}
	}

	class ComponentScriptGraphCreator : ClassGraphCreator {
		enum ComponentKind {
			Component,
			EnableableComponent,
		}
		private ComponentKind componentKind = ComponentKind.Component;

		public override string menuName => "DOTS/Component";

		public ComponentScriptGraphCreator() {
			graphUsingNamespaces = new List<string>() {
				"Unity.Burst",
				"Unity.Entities",
				"Unity.Transforms",
				"Unity.Mathematics",
				"Unity.Collections",
			};
		}

		public override void OnGUI() {
			base.OnGUI();
			uNodeGUIUtility.ShowField(nameof(componentKind), this);
			DrawGraphLayout();
		}

		protected override IScriptGraphType CreateScriptGraphType() {
			var graph = ScriptableObject.CreateInstance<ClassScript>();
			graph.inheritType = typeof(ValueType);
			switch(componentKind) {
				case ComponentKind.Component:
					graph.interfaces = new List<SerializedType>() {
						typeof(IComponentData)
					};
					break;
				case ComponentKind.EnableableComponent:
					graph.interfaces = new List<SerializedType>() {
						typeof(IEnableableComponent)
					};
					break;
			}
			graph.GraphData.graphLayout = graphLayout;
			return graph;
		}
	}
}