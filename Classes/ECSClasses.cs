using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace MaxyGames.UNode {
	public interface INodeEntitiesForeach {
		List<ECSJobVariable> JobVariables { get; }

		public void AddJobVariable(ECSJobVariable variable) {
			var data = JobVariables;
			if(data.Any(item => item.owner == variable.owner || item.name == variable.name) == false) {
				data.Add(variable);
			}
		}
	}

	public enum QueryFilter {
		WithAll = 0,
		WithAny = 1,
		WithNone = 2,
		WithChangeFilter = 3,
		WithAbsent = 4,
		WithDisabled = 5,
		WithPresent = 6,
	}

	public class ECSQueryFilter {
		public QueryFilter filter;
		public SerializedType type = typeof(IComponentData);
	}

	public interface IECSNode {

	}

	public class ECSJobVariable {
		public string name;
		public Type type;
		public Func<string> value;

		public object owner;
	}

	public static class ECSGraphUtility {
		public static string GetEntityManager(NodeObject nodeObject) {
			if(CG.isGenerating) {
				var result = CG.GetUserObject<string>((nodeObject.graphContainer, "EntityManager", typeof(EntityManager)));
				if(result == null) {
					result = CG.GenerateNewName("entityManager");
					CG.RegisterUserObject(result, (nodeObject.graphContainer, "EntityManager", typeof(EntityManager)));

					CG.RegisterPostClassManipulator(data => {
						var mdata = data.GetMethodData(nameof(ISystem.OnUpdate));
						if(mdata == null)
							throw new Exception($"There's no {nameof(ISystem.OnUpdate)} event/function in a graph");

						var graph = nodeObject.graphContainer as ECSGraph;
						var contents = CG.DeclareVariable(result, CG.Access(graph.CodegenStateName).CGAccess("EntityManager"));
						mdata.AddCode(contents, -1000);
					});
				}
				return result;
			}
			return null;
		}

		public static string GetECBSingleton<T>(NodeObject nodeObject) where T : IECBSingleton {
			if(CG.isGenerating) {
				var result = CG.GetUserObject<string>((nodeObject.graphContainer, "ECB-Singleton", typeof(T)));
				if(result == null) {
					result = CG.GenerateNewName("ecb");
					CG.RegisterUserObject(result, (nodeObject.graphContainer, "ECB-Singleton", typeof(T)));

					CG.RegisterPostClassManipulator(data => {
						var mdata = data.GetMethodData(nameof(ISystem.OnUpdate));
						if(mdata == null)
							throw new Exception($"There's no {nameof(ISystem.OnUpdate)} event/function in a graph");

						var graph = nodeObject.graphContainer as ECSGraph;
						var contents = CG.DeclareVariable(
							result,
							typeof(SystemAPI)
							.CGInvoke(nameof(SystemAPI.GetSingleton), new[] { typeof(T) }, null)
							.CGInvoke("CreateCommandBuffer", graph.CodegenStateName.CGAccess(nameof(SystemState.WorldUnmanaged)))
						);
						mdata.AddCode(contents, -1000);
					});
				}
				return result;
			}
			return null;
		}
	}
}