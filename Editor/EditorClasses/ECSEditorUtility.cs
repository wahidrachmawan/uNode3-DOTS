using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using MaxyGames.UNode.Nodes;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MaxyGames.UNode.Editors {
	public static class ECSEditorUtility {
		public static ClassScript CreateAspect(ScriptGraph scriptGraph, ClassScript component) {
			var authoringGraph = ScriptableObject.CreateInstance<ClassScript>();
			{
				authoringGraph.inheritType = typeof(MonoBehaviour);
				foreach(var variable in component.GetAllVariables()) {
					if(variable.type == typeof(Entity)) {
						authoringGraph.GraphData.variableContainer.AddVariable(variable.name, typeof(GameObject));
					}
					else if(variable.type.IsValueType) {
						authoringGraph.GraphData.variableContainer.AddVariable(variable.name, variable.type);
					}
				}
			}
			scriptGraph.TypeList.AddType(authoringGraph, scriptGraph);
			AssetDatabase.AddObjectToAsset(authoringGraph, scriptGraph);
			return authoringGraph;
		}

		public static ClassScript CreateBaker(ScriptGraph scriptGraph, Type componentType, Type authoringType) {
			var bakerGraph = ScriptableObject.CreateInstance<ClassScript>();
			{
				bakerGraph.name = componentType.Name + "Baker";
				bakerGraph.inheritType = ReflectionUtils.MakeGenericType(typeof(Baker<>), authoringType);
				var function = new Function(nameof(Baker.Bake), typeof(void), new[] { new ParameterData("authoring", authoringType) }) {
					modifier = new FunctionModifier() {
						Override = true,
					}
				};
				bakerGraph.GraphData.functionContainer.AddChild(function);
				{
					var entry = function.Entry;
					var authoringParameter = entry.nodeObject.ValueOutputs.First();

					NodeEditorUtility.AddNewNode(function, Vector2.zero, (CacheNode entity) => {
						entity.nodeObject.name = "entity";
						entry.exit.ConnectTo(entity.enter);

						NodeEditorUtility.AddNewNode(function, Vector2.zero, (NodeBaseCaller node) => {
							node.target = MemberData.CreateFromMember(typeof(IBaker).GetMethod(nameof(IBaker.GetEntity), new[] { typeof(TransformUsageFlags) }));
							node.parameters[0].input.AssignToDefault(TransformUsageFlags.Dynamic);
							entity.target.ConnectTo(node.output);
						});

						NodeEditorUtility.AddNewNode(function, Vector2.zero, (NodeBaseCaller addComponent) => {
							addComponent.target = MemberData.CreateFromMember(
								ReflectionUtils.MakeGenericMethod(
									typeof(IBaker).GetMethod(
										nameof(IBaker.AddComponent),
										1,
										new[]{
											typeof(Entity),
											Type.MakeGenericMethodParameter(0).MakeByRefType() }
										),
									componentType)
								);
							addComponent.parameters[0].input.ConnectToAsProxy(entity.output);

							entity.exit.ConnectTo(addComponent.enter);

							NodeEditorUtility.AddNewNode(function, Vector2.zero, (MultipurposeNode ctor) => {
								ctor.target = MemberData.CreateFromMember(ReflectionUtils.GetDefaultConstructor(componentType));
								var initializers = ctor.initializers;
								var fields = componentType.GetFields();
								foreach(var field in fields) {
									initializers.Add(new MultipurposeMember.InitializerData() {
										name = field.Name,
										type = field.FieldType,
									});
								}
								addComponent.parameters[1].input.ConnectTo(ctor.output);
								ctor.Register();
								foreach(var init in initializers) {
									var type = init.type.type;
									if(type == typeof(Entity)) {
										NodeEditorUtility.AddNewNode(function, Vector2.zero, (NodeBaseCaller baseCaller) => {
											baseCaller.target = MemberData.CreateFromMember(typeof(IBaker).GetMethod(
												nameof(IBaker.GetEntity),
												new[] { typeof(GameObject), typeof(TransformUsageFlags) }));

											NodeEditorUtility.AddNewNode(function, Vector2.zero, (MultipurposeNode node) => {
												node.target = MemberData.CreateFromMember(authoringType.GetField(init.name));
												node.instance.ConnectToAsProxy(authoringParameter);
												baseCaller.parameters[0].input.ConnectTo(node.output);
											});
											baseCaller.parameters[1].input.AssignToDefault(TransformUsageFlags.Dynamic);
											init.port.ConnectTo(baseCaller.output);
										});
									}
									else {
										var field = authoringType.GetField(init.name);
										if(field != null) {
											NodeEditorUtility.AddNewNode(function, Vector2.zero, (MultipurposeNode node) => {
												node.target = MemberData.CreateFromMember(field);
												node.instance.ConnectToAsProxy(authoringParameter);
												init.port.ConnectTo(node.output);
											});
										}
									}
								}
							});
						});
					});
				}
			}
			scriptGraph.TypeList.AddType(bakerGraph, scriptGraph);
			AssetDatabase.AddObjectToAsset(bakerGraph, scriptGraph);
			return bakerGraph;
		}

		//For reference only
		abstract class Baker : Baker<GraphComponent> { }
	}

	public static class BlittableTypeChecker {
		/// <summary>
		/// Cache for reflection-based blittable checks.
		/// </summary>
		private static readonly Dictionary<Type, bool> _blittableCache = new Dictionary<Type, bool>();

		/// <summary>
		/// Primitive blittable types.
		/// </summary>
		private static readonly HashSet<Type> PrimitiveBlittableTypes = new HashSet<Type> {
			typeof(bool),//is not blittable but unity support it
			typeof(byte),
			typeof(sbyte),
			typeof(short),
			typeof(ushort),
			typeof(int),
			typeof(uint),
			typeof(long),
			typeof(ulong),
			typeof(float),
			typeof(double),
			typeof(IntPtr),
			typeof(UIntPtr)
		};

		/// <summary>
		/// Compile-time generic blittable check.
		///
		/// Uses RuntimeHelpers.IsReferenceOrContainsReferences<T>()
		/// which is fast and works well for unmanaged validation.
		///
		/// Returns true if T contains no managed references.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsBlittable<T>() {
			try {
				return !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
			}
			catch(Exception ex) {
				UnityEngine.Debug.LogError(
					$"Blittable check failed for {typeof(T)}\n{ex}");

				return false;
			}
		}

		/// <summary>
		/// Reflection-based runtime blittable check.
		/// </summary>
		public static bool IsBlittable(Type type) {
			if(type == null)
				throw new ArgumentNullException(nameof(type));

			if(_blittableCache.TryGetValue(type, out bool cached))
				return cached;
			var visited = StaticHashPool<Type>.Allocate();
			bool result = InternalIsBlittable(type, visited);
			StaticHashPool.Free(visited);

			_blittableCache[type] = result;

			return result;
		}

		/// <summary>
		/// Internal recursive blittable validation.
		/// </summary>
		private static bool InternalIsBlittable(Type type, HashSet<Type> visited) {
			// Primitive blittable types.
			if(PrimitiveBlittableTypes.Contains(type))
				return true;

			// Enums are blittable if underlying type is blittable.
			if(type.IsEnum) {
				return IsBlittable(Enum.GetUnderlyingType(type));
			}

			// Pointer types are blittable.
			if(type.IsPointer)
				return true;

			// Reject managed/reference types.
			if(!type.IsValueType)
				return false;

			// Explicitly reject common problematic types.
			if(type == typeof(bool))
				return false;

			if(type == typeof(char))
				return false;

			if(type == typeof(decimal))
				return false;

			if(visited.Add(type)) {
				return false;
			}

			// Validate all instance fields recursively.
			FieldInfo[] fields = type.GetFields(
				BindingFlags.Instance |
				BindingFlags.Public |
				BindingFlags.NonPublic);

			foreach(FieldInfo field in fields) {
				Type fieldType = field.FieldType;

				if(!InternalIsBlittable(fieldType, visited))
					return false;
			}

			// Marshal.SizeOf throws if not blittable in many cases.
			try {
				Marshal.SizeOf(type);
			}
			catch {
				return false;
			}

			return true;
		}
	}
}