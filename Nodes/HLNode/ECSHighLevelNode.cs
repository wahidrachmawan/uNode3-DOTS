using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace MaxyGames.UNode {
	/// <summary>
	/// An interface for implementing ECS Node.
	/// The Execution method must be static.
	/// </summary>
	public interface IECSNode : IHighLevelNode<ECSHighLevelNode> {

		public MethodInfo GetExecutionMethod(ECSHighLevelNode node) {
			return this.GetType().GetMemberCached("Execute") as MethodInfo;
		}
	}

	public class ECSHighLevelNode : Node, IHighLevelNodeDefinition {
		[HideInInspector]
		public SerializedType type = typeof(object);

		[SerializeField]
		private IECSNode m_instance;
		[NonSerialized]
		private MethodInfo m_executionInfo;

		public class PortData {
			public string name;
			public object info;

			public ValueInput valueInput;
			public ValueOutput valueOutput;
			public ValueOutput[] valueOutputs;
			public FlowInput flowInput;
			public FlowOutput flowOutput;
		}
		[NonSerialized]
		public List<PortData> ports = new();

		[NonSerialized]
		public FlowInput enter;
		[NonSerialized]
		public FlowOutput exit;

		Type IHighLevelNodeDefinition.NodeType => type;

		[System.Runtime.Serialization.OnDeserialized]
		private void OnDeserialized() {
			ports = new List<PortData>();
		}

		protected override void OnRegister() {
			var instanceType = this.type.type;
			if(m_instance == null || m_instance.GetType() != instanceType) {
				if(instanceType != null && ReflectionUtils.CanCreateInstance(instanceType)) {
					m_instance = ReflectionUtils.CreateInstance(instanceType) as IECSNode;
				}
				else {
					m_instance = null;
					nodeObject.RestorePreviousPort();
					return;
				}
			}
			ports.Clear();
			enter = PrimaryFlowInput(nameof(enter), null);
			exit = PrimaryFlowOutput(nameof(exit));
			if(m_instance != null) {
				m_executionInfo = m_instance.GetExecutionMethod(this);
				var method = m_executionInfo;
				if(method.IsGenericMethod) {
					throw new Exception("Generic is not supported yet.");
				}
				var parameters = method.GetParameters();
				foreach(var param in parameters) {
					PortData data = null;
					if(param.IsOut) {
						data = new PortData() {
							name = param.Name,
							valueOutput = ValueOutput(param.Name, param.ParameterType.ElementType(), PortAccessibility.ReadWrite),
						};
						data.valueOutput.isVariable = true;
					}
					else {
						data = new PortData() {
							name = param.Name,
							valueInput = ValueInput(param.Name, param.ParameterType)
						};
						if(param.IsOptional) {
							data.valueInput.MarkAsOptional();
						}
					}
					if(data != null) {
						data.info = param;
						ports.Add(data);
					}
				}
				var rType = method.ReturnType;
				if(rType != typeof(void)) {
					PortData data = new PortData() {
						name = "Out",
						info = method,
					};
					if(rType.IsCastableTo(typeof(ITuple))) {
						var tupleAtts = method.ReturnTypeCustomAttributes.GetCustomAttributes(typeof(TupleElementNamesAttribute), true);
						if(tupleAtts.Length == 1) {
							var tupleNames = tupleAtts[0] as TupleElementNamesAttribute;
							ValueOutput[] outputs = new ValueOutput[tupleNames.TransformNames.Count];
							var tupleTypes = rType.GetGenericArguments();
							if(tupleTypes.Length != outputs.Length)
								throw new Exception("The lenght of tuple name is not match with the tuple parameter type");
							for(int i = 0; i < outputs.Length; i++) {
								outputs[i] = ValueOutput($"ret:{i}", tupleTypes[i]).SetName(tupleNames.TransformNames[i]);
							}
							data.valueOutputs = outputs;
						}
						else {
							throw new Exception("Please give a name to the tuple type");
						}
					}
					else {
						data.valueOutput = ValueOutput("Out", rType);
					}
					ports.Add(data);
				}
			}
		}

		#region Code Generation
		public override void OnGeneratorInitialize() {
			if(m_instance == null) return;
			if(CG.IsFlowRegistered(enter)) {
				for(int x = 0; x < ports.Count; x++) {
					var data = ports[x];
					if(data.info is ParameterInfo parameter) {
						if(data.valueOutput?.hasValidConnections == true) {
							var nm = CG.RegisterVariable(data.valueOutput, parameter.Name, parameter.ParameterType);
							CG.RegisterPort(data.valueOutput, () => {
								return nm;
							});
						}
					}
					else if(data.info is MethodInfo method) {
						if(data.valueOutput != null) {
							if(data.valueOutput.hasValidConnections) {
								var nm = CG.RegisterVariable(data.valueOutput, "m_value", method.ReturnType);
								CG.RegisterPort(data.valueOutput, () => {
									return nm;
								});
							}
						}
						else if(data.valueOutputs != null) {
							for(int i = 0; i < data.valueOutputs.Length; i++) {
								var port = data.valueOutputs[i];
								if(port.hasValidConnections) {
									var nm = CG.RegisterVariable(port, port.name, method.ReturnType);
									CG.RegisterPort(port, () => {
										return nm;
									});
								}
							}
						}
					}
				}
			}
			if(enter != null) {
				CG.RegisterPort(enter, () => {
					List<string> param = new();
					string body = null;

					foreach(var p in ports) {
						if(p.info is ParameterInfo info) {
							if(p.valueInput != null) {
								if(info.ParameterType.IsByRef) {
									param.Add("ref " + CG.GeneratePort(p.valueInput));
								}
								else {
									param.Add(CG.GeneratePort(p.valueInput));
								}
							}
							else if(p.valueOutput != null) {
								param.Add("out var " + CG.GetVariableName(p.valueOutput));
							}
						}
						else if(p.info is MethodInfo method) {
							if(p.valueOutput != null) {
								if(CG.HasRegisteredVariable(p.valueOutput)) {
									var nm = CG.GetVariableName(p.valueOutput);
									body = CG.DeclareVariable(nm, CG.Invoke(m_instance.GetType(), m_executionInfo.Name, param.ToArray()));
								}
							}
							else if(p.valueOutputs != null) {
								List<string> names = new();
								for(int i = 0; i < p.valueOutputs.Length; i++) {
									var port = p.valueOutputs[i];
									if(port.hasValidConnections) {
										names.Add(CG.GetVariableName(port));
									}
									else {
										names.Add(null);
									}
								}
								if(names.Any(n => n != null)) {
									body = CG.DeclareVariableTuple(names, CG.Invoke(m_instance.GetType(), m_executionInfo.Name, param.ToArray()));
								}
							}
						}
					}

					return CG.Flow(
						body ?? CG.FlowInvoke(m_instance.GetType(), m_executionInfo.Name, param.ToArray()),
						CG.FlowFinish(enter, exit)
					);
				});
			}
		}
		#endregion

		void IHighLevelNodeDefinition.OnCreate(IHighLevelNode instance) {
			type = instance.GetType();
			m_instance = instance as IECSNode;
		}

		public override string GetTitle() {
			Type instancecType = type.type;
			if(instancecType != null) {
				if(instancecType.IsDefined(typeof(NodeMenu), true)) {
					return (instancecType.GetCustomAttributes(typeof(NodeMenu), true)[0] as NodeMenu).name;
				}
			}
			else {
				return "Missing Type";
			}
			return type.prettyName;
		}

		public override void CheckError(ErrorAnalyzer analizer) {
			base.CheckError(analizer);
			if(type.type == null) {
				analizer.RegisterError(this, "Missing node type: " + type.typeName);
			}
			else if(!ReflectionUtils.CanCreateInstance(type.type)) {
				analizer.RegisterError(this, "Cannot create instance of type: " + type.type);
			}
		}

		public override Type GetNodeIcon() {
			if(m_instance != null) {
				if(m_instance is IIcon) {
					return (m_instance as IIcon).GetIcon();
				}
				if(m_instance.GetType().IsDefined(typeof(NodeMenu), true)) {
					var icon = m_instance.GetType().GetCustomAttribute<NodeMenu>().GetIcon();
					if(icon != null)
						return icon;
				}
			}
			return base.GetNodeIcon();
		}
	}
}

#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;

	class ECSHighLevelNodeDrawer : NodeDrawer<ECSHighLevelNode> {
		public override void DrawLayouted(ref DrawerOption option) {
			var value = GetNode(ref option);
			if(value != null) {
				UInspector.DrawChilds(option.property["m_instance"]);
			}
			base.DrawLayouted(ref option);
		}
	}
}
#endif