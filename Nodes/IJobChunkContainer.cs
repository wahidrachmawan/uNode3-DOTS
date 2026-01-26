using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Burst.Intrinsics;

[assembly: MakeSerializable(typeof(MaxyGames.UNode.Nodes.IJobChunkContainer))]
namespace MaxyGames.UNode.Nodes {
	[EventGraph("IJobChunk", createName = "newJobChunk")]
	public class IJobChunkContainer : BaseJobContainer, ISuperNodeWithEntry, IGeneratorPrePostInitializer {
		public ValueOutput chunk { get; private set; }
		public ValueOutput unfilteredChunkIndex { get; private set; }
		public ValueOutput useEnabledMask { get; private set; }
		public ValueOutput chunkEnabledMask { get; private set; }

		public override void RegisterEntry(BaseEntryNode node) {
			chunk = Node.Utilities.ValueOutput<ArchetypeChunk>(node, nameof(chunk));
			unfilteredChunkIndex = Node.Utilities.ValueOutput<int>(node, nameof(unfilteredChunkIndex));
			useEnabledMask = Node.Utilities.ValueOutput<bool>(node, nameof(useEnabledMask));
			chunkEnabledMask = Node.Utilities.ValueOutput<v128>(node, nameof(chunkEnabledMask));

			base.RegisterEntry(node);
		}

		public void OnPreInitializer() {
			//Ensure this node is registered
			Entry.EnsureRegistered();
			//Manual register the entry node.
			CG.RegisterDependency(entryObject);
			//Initialize the class name
			CG.RegisterUserObject(CG.GenerateNewName(name), this);
		}

		public void OnPostInitializer() {
			string className = CG.GetUserObject<string>(this);
			for(int i = 0; i < variableDatas.Count; i++) {
				var data = variableDatas[i];
				var vName = CG.RegisterVariable(data.port);
				CG.RegisterPort(data.port, () => vName);
			}
			var chunkCode = CG.GenerateNewName(nameof(chunk));
			var unfilteredChunkIndexCode = CG.GenerateNewName(nameof(unfilteredChunkIndex));
			var useEnabledMaskCode = CG.GenerateNewName(nameof(useEnabledMask));
			var chunkEnabledMaskCode = CG.GenerateNewName(nameof(chunkEnabledMask));
			CG.RegisterPort(chunk, () => chunkCode);
			CG.RegisterPort(unfilteredChunkIndex, () => unfilteredChunkIndexCode);
			CG.RegisterPort(useEnabledMask, () => useEnabledMaskCode);
			CG.RegisterPort(chunkEnabledMask, () => chunkEnabledMaskCode);

			List<ECSJobVariable> localVariables = JobVariables;
			if(variableDatas.Count > 0) {
				for(int i = 0; i < variableDatas.Count; i++) {
					var data = variableDatas[i];
					localVariables.Add(new ECSJobVariable() {
						name = CG.GetVariableName(data.port),
						type = data.type,
						owner = data,
					});
				}
			}

			CG.RegisterPostGeneration((classData) => {
				//Create class
				var classBuilder = new CG.ClassData(className);
				classBuilder.implementedInterfaces.Add(typeof(IJobChunk));
				classBuilder.SetToPartial();
				classBuilder.SetTypeToStruct();
				if(localVariables.Count > 0) {
					for(int i = 0; i < localVariables.Count; i++) {
						var data = localVariables[i];
						classBuilder.RegisterVariable(CG.DeclareVariable(data.type, data.name, modifier: FieldModifier.PublicModifier));
					}
				}

				//Create execute method
				var method = new CG.MData(nameof(IJobChunk.Execute), typeof(void)) {
					modifier = new FunctionModifier(),
				};
				List<CG.MPData> parameters = new List<CG.MPData> {
					new CG.MPData(chunkCode, typeof(ArchetypeChunk), RefKind.In),
					new CG.MPData(unfilteredChunkIndexCode, typeof(int)),
					new CG.MPData(useEnabledMaskCode, typeof(bool)),
					new CG.MPData(chunkEnabledMaskCode, typeof(v128), RefKind.In)
				};
				method.parameters = parameters;
				//Generate code for execute logic
				method.code = CG.GeneratePort(Entry.nodeObject.primaryFlowOutput);

				//Register the generated function code
				classBuilder.RegisterFunction(method.GenerateCode());
				//Register the generated type code
				classData.RegisterNestedType(CG.WrapWithInformation(classBuilder.GenerateCode(), this));
			});
		}
	}
}

#if UNITY_EDITOR
namespace MaxyGames.UNode.Editors {
	using UnityEditor;
	using UnityEngine.UIElements;

	class IJobChunkContainerDrawer : UGraphElementDrawer<Nodes.IJobChunkContainer> {
		static readonly FilterAttribute componentFilter;

		static IJobChunkContainerDrawer() {
			componentFilter = new FilterAttribute(typeof(IComponentData), typeof(IQueryTypeParameter)) {
				DisplayInterfaceType = false,
				DisplayReferenceType = true,
				DisplayValueType = true,
			};
		}

		public override void DrawLayouted(DrawerOption option) {
			var container = GetValue(option);

			uNodeGUI.DrawCustomList(container.variableDatas, "Variables",
				drawElement: (position, index, value) => {
					position.height = EditorGUIUtility.singleLineHeight;
					var portName = EditorGUI.DelayedTextField(position, new GUIContent("Name "), value.name);
					if(portName != value.name) {
						value.name = portName;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}
					position.y += EditorGUIUtility.singleLineHeight + 1;
					uNodeGUIUtility.DrawTypeDrawer(position, value.type, new GUIContent("Type"), type => {
						value.type = type;
						container.Entry.Register();
						uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
						uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
					}, FilterAttribute.DefaultTypeFilter, option.unityObject);
				},
				add: position => {
					option.RegisterUndo();
					container.variableDatas.Add(new Nodes.BaseJobContainer.VData());
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				remove: index => {
					option.RegisterUndo();
					container.variableDatas.RemoveAt(index);
					container.Entry.Register();
					uNodeGUIUtility.GUIChanged(container, UIChangeType.Important);
					uNodeGUIUtility.GUIChanged(container.Entry, UIChangeType.Average);
				},
				elementHeight: index => {
					return (EditorGUIUtility.singleLineHeight * 2) + 2;
				});

			DrawErrors(option);
		}
	}
}
#endif