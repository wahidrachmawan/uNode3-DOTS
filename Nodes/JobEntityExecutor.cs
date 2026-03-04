using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;

namespace MaxyGames.UNode.Nodes {
	public class JobEntityExecutor : FlowNode {
		public enum RunWith {
			Run,
			Schedule,
			ScheduleParallel,
		}

		public RunWith runWith;

		[SerializeField]
		private UGraphElementRef reference;
		public BaseJobContainer ReferenceNode {
			get {
				if(reference != null && reference.reference is BaseJobContainer node) {
					return node;
				}
				return null;
			}
			set {
				reference = value;
			}
		}

		public ValueInput query { get; set; }
		public ValueInput dependsOn { get; set; }
		public ValueInput chunkBaseEntityIndices { get; set; }
		public ValueOutput jobHandle { get; set; }
		public ValueInput[] variablePorts { get; set; }

		protected override void OnRegister() {
			base.OnRegister();
			if(ReferenceNode != null) {
				var datas = ReferenceNode.variableDatas;
				variablePorts = new ValueInput[datas.Count];
				for(int i = 0; i < variablePorts.Length; i++) {
					var data = datas[i];
					variablePorts[i] = ValueInput(data.id, () => data.type).SetName(data.name);
				}
			}
			switch(runWith) {
				case RunWith.Run:
					query = ValueInput(nameof(query), typeof(EntityQuery));
					break;
				case RunWith.Schedule:
					query = ValueInput(nameof(query), typeof(EntityQuery));
					dependsOn = ValueInput(nameof(dependsOn), typeof(Unity.Jobs.JobHandle));
					break;
				case RunWith.ScheduleParallel:
					query = ValueInput(nameof(query), typeof(EntityQuery));
					dependsOn = ValueInput(nameof(dependsOn), typeof(Unity.Jobs.JobHandle));
					if(dependsOn.isAssigned) {
						chunkBaseEntityIndices = ValueInput(nameof(chunkBaseEntityIndices), typeof(Unity.Collections.NativeArray<int>));
					}
					break;
			}
			query.SetTooltip("The query selecting chunks with the necessary components (optional)");
			query.MarkAsOptional();
			if(dependsOn != null) {
				dependsOn.SetTooltip("The handle identifying already scheduled jobs that could constrain this job.\r\nA job that writes to a component cannot run in parallel with other jobs that read or write that component.\r\nJobs that only read the same components can run in parallel.");
				dependsOn.MarkAsOptional();
				if(dependsOn.isAssigned) {
					jobHandle = ValueOutput(nameof(jobHandle), typeof(Unity.Jobs.JobHandle));
				}
			}
		}

		public override string GetTitle() {
			switch(runWith) {
				case RunWith.Run:
					return $"Job.Run";
				case RunWith.Schedule:
					return $"Job.Schedule";
				case RunWith.ScheduleParallel:
					return $"Job.ScheduleParallel";
			}
			return base.GetTitle();
		}

		public override void OnGeneratorInitialize() {
			base.OnGeneratorInitialize();
			if(jobHandle != null && jobHandle.hasValidConnections) {
				string varName = CG.RegisterVariable(jobHandle);
				CG.RegisterPort(jobHandle, () => varName);
			}
		}

		protected override string GenerateFlowCode() {
			List<string> parameters = new List<string>(3);
			if(query.isAssigned) {
				parameters.Add(CG.GeneratePort(query));
			}
			if(dependsOn != null && dependsOn.isAssigned) {
				parameters.Add(CG.GeneratePort(dependsOn));
			}
			if(chunkBaseEntityIndices != null && chunkBaseEntityIndices.isAssigned) {
				parameters.Add(CG.GeneratePort(chunkBaseEntityIndices));
			}
			List<string> initializers = null;
			{
				var jobVariables = ReferenceNode.JobVariables;
				initializers = new List<string>(jobVariables.Count);
				foreach(var variable in jobVariables) {
					if(variable.value != null) {
						initializers.Add(CG.SetValue(variable.name, variable.value()));
					}
					else if(variable.value == null && variable.owner is BaseJobContainer.VData vdata) {
						var port = variablePorts.FirstOrDefault(p => p.name == vdata.name);
						if(port != null) {
							initializers.Add(CG.SetValue(variable.name, CG.GeneratePort(port)));
						}
					}
				}
			}
			string job = CG.GenerateName("job", this);
			string result = CG.DeclareVariable(job, CG.New(CG.GetUserObject<string>(ReferenceNode), null, initializers));

			if(jobHandle != null && jobHandle.hasValidConnections) {
				result = CG.Flow(
					result,
					CG.DeclareVariable(
						CG.GetVariableName(jobHandle), 
						job.CGInvoke(runWith.ToString(), parameters.ToArray()))
				);
			}
			else {
				result = CG.Flow(
					result, 
					job.CGFlowInvoke(runWith.ToString(), parameters.ToArray())
				);
			}
			return CG.Flow(result, CG.FlowFinish(enter, exit));
		}

		public override void CheckError(ErrorAnalyzer analyzer) {
			if(ReferenceNode == null) {
				analyzer.RegisterError(this, "Reference is missing.");
			}
		}
	}
}


#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;
	using UnityEngine.UIElements;

	[NodeCustomEditor(typeof(Nodes.JobEntityExecutor))]
	class JobEntityExecutorView : BaseNodeView {
		protected override void OnSetup() {
			base.OnSetup();
			var node = targetNode as Nodes.JobEntityExecutor;
			{
				var element = new Button();
				if(node.ReferenceNode != null) {
					element.text = node.ReferenceNode.name;
				}
				else {
					element.text = "None";
				}
				element.clickable.clickedWithEventInfo += (evt) => {
					if(node.ReferenceNode != null) {
						uNodeEditor.Highlight(node.ReferenceNode);
					}
					else {
						if(nodeObject.graph.mainGraphContainer != null) {
							GenericMenu menu = new GenericMenu();
							foreach(var n in nodeObject.graph.mainGraphContainer.GetObjectsInChildren<Nodes.BaseJobContainer>()) {
								var jobNode = n;
								menu.AddItem(new GUIContent(n.name), false, () => {
									node.ReferenceNode = jobNode;
								});
							}
							menu.ShowAsContext();
						}
					}
				};

				element.AddManipulator(new ContextualMenuManipulator(evt => {
					if(nodeObject.graph.mainGraphContainer != null) {
						foreach(var n in nodeObject.graph.mainGraphContainer.GetObjectsInChildren<Nodes.BaseJobContainer>()) {
							var jobNode = n;
							evt.menu.AppendAction(n.name, act => {
								node.ReferenceNode = jobNode;
							});
						}
					}
				}));

				titleContainer.Add(element);
			}
		}
	}
}
#endif