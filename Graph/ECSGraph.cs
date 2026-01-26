using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace MaxyGames.UNode {
	[GraphSystem(
		supportAttribute = true,
		supportModifier = true,
		supportGeneric = false,
		allowAutoCompile = false,
		isScriptGraph = true,
		generationKind = GenerationKind.Compatibility)]
	public class ECSGraph : GraphAsset, IClassGraph, IClassModifier, IGraphWithVariables, IGraphWithProperties, IGraphWithFunctions, IGraphWithAttributes, INamespaceSystem, ICustomMainGraph, IGraphWithEventGraph, IGeneratorPrePostInitializer {
		public string @namespace;
		public List<string> usingNamespaces = new List<string>() { "Unity.Burst", "Unity.Entities", "Unity.Transforms", "Unity.Mathematics" };
		public ClassModifier modifier = new ClassModifier() { Partial = true };
		public SerializedType inheritType = typeof(ValueType);

		//public bool burstCompile = true;
		public List<SerializedType> requiredForUpdates = new List<SerializedType>();

		[HideInInspector, SerializeField]
		private GeneratedScriptData scriptData = new GeneratedScriptData();

		#region Properties
		public string GraphName {
			get {
				var nm = GraphData.name;
				try {
					if(string.IsNullOrEmpty(nm)) {
						if(string.IsNullOrEmpty(scriptData.fileName) && uNodeUtility.IsInMainThread) {
							scriptData.fileName = this.name;
						}
						return scriptData.fileName;
					}
				}
				catch(Exception ex){
					Debug.LogException(ex, this);
				}
				return nm;
			}
		}

		public string Namespace {
			get {
				return @namespace;
			}
			set => @namespace = value;
		}

		public List<string> UsingNamespaces {
			get => usingNamespaces;
			set => usingNamespaces = value;
		}

		public string MainGraphScope => NodeScope.ECSGraph;

		public bool IsISystem => inheritType == typeof(ValueType);

		GeneratedScriptData ITypeWithScriptData.ScriptData => scriptData;
		Type IClassGraph.InheritType => inheritType;
		ClassModifier IClassModifier.GetModifier() {
			modifier.Partial = true;
			return modifier;
		}
		#endregion

		public string CodegenStateName {
			get {
				if(inheritType == typeof(ValueType)) {
					return "state";
				}
				else {
					return nameof(SystemBase.CheckedStateRef);
				}
			}
		}

		private static readonly HashSet<string> m_supportedEventGraph = new() {
			"ECS_System",
			"IJobEntity",
			"IJobChunk"
		};
		HashSet<string> IGraphWithEventGraph.SupportedEventGraphs => m_supportedEventGraph;

		void IGeneratorPrePostInitializer.OnPreInitializer() {
			//Ensure the type is partial
			modifier.Partial = true;

			var parameterName = "state";
			CG.MData onCreate;
			CG.MData onUpdate;
			CG.MData onDestroy;
			if(IsISystem) {
				onCreate = CG.generatorData.AddMethod(
					nameof(ISystem.OnCreate),
					typeof(void),
					new[] {
					new CG.MPData(parameterName, typeof(SystemState), RefKind.Ref)
					}
				).SetToPublic();
				onUpdate = CG.generatorData.AddMethod(
					nameof(ISystem.OnUpdate),
					typeof(void),
					new[] {
					new CG.MPData(parameterName, typeof(SystemState), RefKind.Ref)
					}
				).SetToPublic();
				onDestroy = CG.generatorData.AddMethod(
					nameof(ISystem.OnDestroy),
					typeof(void),
					new[] {
					new CG.MPData(parameterName, typeof(SystemState), RefKind.Ref)
					}
				).SetToPublic();
				if(GraphData.mainGraphContainer.GetObjectsInChildren().Any(element => element is NodeObject node && (node.node is Nodes.OnSystemStartRunning || node.node is Nodes.OnSystemStopRunning))) {
					CG.RegisterPostGeneration(classData => {
						classData.implementedInterfaces.Add(typeof(ISystemStartStop));
					});
					CG.generatorData.AddMethod(
						nameof(ISystemStartStop.OnStartRunning),
						typeof(void),
						new[] {
						new CG.MPData(parameterName, typeof(SystemState), RefKind.Ref)
						}
					).SetToPublic();
					CG.generatorData.AddMethod(
						nameof(ISystemStartStop.OnStopRunning),
						typeof(void),
						new[] {
						new CG.MPData(parameterName, typeof(SystemState), RefKind.Ref)
						}
					).SetToPublic();
				}
			}
			else {
				onCreate = CG.generatorData.AddMethod(
					nameof(ISystem.OnCreate),
					typeof(void)
				);
				onCreate.modifier = new FunctionModifier();
				onCreate.modifier.SetProtected();
				onCreate.modifier.SetOverride();
				onUpdate = CG.generatorData.AddMethod(
					nameof(ISystem.OnUpdate),
					typeof(void)
				);
				onUpdate.modifier = new FunctionModifier();
				onUpdate.modifier.SetProtected();
				onUpdate.modifier.SetOverride();
				onDestroy = CG.generatorData.AddMethod(
					nameof(ISystem.OnDestroy),
					typeof(void)
				);
				onDestroy.modifier = new FunctionModifier();
				onDestroy.modifier.SetProtected();
				onDestroy.modifier.SetOverride();
				if(GraphData.mainGraphContainer.GetObjectsInChildren().Any(element => element is NodeObject node && node.node is Nodes.OnSystemStartRunning)) {
					var mData = CG.generatorData.AddMethod(
						nameof(ISystemStartStop.OnStartRunning),
						typeof(void)
					);
					mData.modifier = new FunctionModifier();
					mData.modifier.SetProtected();
					mData.modifier.SetOverride();
				}
				if(GraphData.mainGraphContainer.GetObjectsInChildren().Any(element => element is NodeObject node && node.node is Nodes.OnSystemStopRunning)) {
					var mData = CG.generatorData.AddMethod(
						nameof(ISystemStartStop.OnStopRunning),
						typeof(void)
					);
					mData.modifier = new FunctionModifier();
					mData.modifier.SetProtected();
					mData.modifier.SetOverride();
				}
			}

			if(requiredForUpdates.Count > 0) {
				if(IsISystem) {
					onCreate.AddCodeForEvent(
						CG.Flow(requiredForUpdates.Where(t => t != null && t.type != null).Select(t => CG.FlowGenericInvoke(t.type, parameterName, nameof(SystemState.RequireForUpdate))))
					);
				}
				else {
					onCreate.AddCodeForEvent(
						CG.Flow(requiredForUpdates.Where(t => t != null && t.type != null).Select(t => CG.FlowGenericInvoke(t.type, string.Empty, nameof(SystemState.RequireForUpdate))))
					);
				}
			}

			//if(IsISystem && burstCompile && !CG.debugScript) {
			//	onCreate.RegisterAttribute(typeof(BurstCompileAttribute));
			//	onUpdate.RegisterAttribute(typeof(BurstCompileAttribute));
			//	onDestroy.RegisterAttribute(typeof(BurstCompileAttribute));
			//}

			//Post generations
			CG.RegisterPostGeneration((classData) => {
				if(IsISystem) {
					//if(burstCompile && !CG.debugScript && GraphData.attributes.Any(a => a.type == typeof(BurstCompileAttribute)) == false) {
					//	classData.RegisterAttribute(typeof(BurstCompileAttribute));
					//}
					classData.implementedInterfaces.Add(typeof(ISystem));
				}

				//if(requiredForUpdates.Count > 0) {
				//	//Add RequireMatchingQueriesForUpdate attribute if there's any required for updates
				//	classData.RegisterAttribute(typeof(RequireMatchingQueriesForUpdateAttribute));
				//}
			});
		}

		void IGeneratorPrePostInitializer.OnPostInitializer() { }

		private void OnValidate() {
			scriptData.fileName = this.name;
		}
	}
}