using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	public abstract class BaseJobContainer : NodeContainerWithEntry, IEventGraphCanvas, INodeEntitiesForeach {
		public class VData {
			public string id = uNodeUtility.GenerateUID();

			public string name = "variable";
			public SerializedType type = typeof(float);

			[NonSerialized]
			public ValueOutput port;
		}
		public List<VData> variableDatas = new List<VData>();

		public IEnumerable<NodeObject> NestedFlowNodes {
			get {
				yield return Entry;
			}
		}

		public List<ECSJobVariable> JobVariables {
			get {
				var result = CG.GetUserObject<List<ECSJobVariable>>(this, "var");
				if(result == null) {
					result = new List<ECSJobVariable>();
					CG.RegisterUserObject(result, this, "var");
				}
				return result;
			}
		}

		public virtual string Title => name;

		public const string Scope = "ECS_Job";

		public override void RegisterEntry(BaseEntryNode node) {
			for(int i = 0; i < variableDatas.Count; i++) {
				var data = variableDatas[i];
				data.port = Node.Utilities.ValueOutput(node, data.id, () => data.type).SetName(data.name);
			}
		}
	}
}