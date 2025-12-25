using MaxyGames.UNode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace MaxyGames.UNode.Editors {
	[InitializeOnLoad]
	public static class HotReloadSystemManager {
		public static List<string> ActiveSystemNames = new();

		static HotReloadSystemManager() {
			EditorApplication.playModeStateChanged += state => {
				if(state == PlayModeStateChange.EnteredPlayMode) {
					//InjectSystems();
				}
				else if(state == PlayModeStateChange.ExitingPlayMode) {
					UninjectSystems();
				}
			};
		}

		private static Assembly loadedAssembly;
		private static List<SystemHandle> liveSystems = new();
		private static List<ComponentSystemBase> liveManagedSystems = new();
		private static object m_oldRegistration, m_oldStructTypes;
		private static Action postAction;

		public static void LoadCompiledAssembly(string path) {
			if(!File.Exists(path)) {
				Debug.LogWarning("DLL not found: " + path);
				return;
			}
			//For make sure only process post action for last compiled assembly
			postAction = null;
			//if(m_oldRegistration == null) {
			//	var type = typeof(SystemBaseRegistry).GetNestedType("Managed", MemberData.flags);
			//	m_oldRegistration = SerializerUtility.Duplicate(type.GetFieldCached("s_PendingRegistrations").GetValue(null));
			//	m_oldStructTypes = SerializerUtility.Duplicate(type.GetFieldCached("s_StructTypes").GetValue(null));
			//}
			postAction += () => {
				var type = typeof(SystemBaseRegistry).GetNestedType("Managed", MemberData.flags);
				var field = type.GetFieldCached("s_PendingRegistrations");
				var value = type.GetFieldCached("s_PendingRegistrations").GetValue(null);
				if(value == null) {
					type.GetFieldCached("s_PendingRegistrations").SetValue(null, ReflectionUtils.CreateInstance(field.FieldType));
				}
				//type.GetFieldCached("s_StructTypes").SetValue(null, SerializerUtility.Duplicate(m_oldStructTypes));
			};
			loadedAssembly = Assembly.Load(File.ReadAllBytes(path), File.ReadAllBytes(Path.ChangeExtension(path, ".pdb")));
			foreach(var type in loadedAssembly.GetTypes()) {
				if(type.Name.StartsWith("__")) {
					var methods = type.GetMethods();
					foreach(var m in methods) {
						if(m.IsDefined(typeof(RuntimeInitializeOnLoadMethodAttribute), false)) {
							//Debug.Log(m);
							var method = m;
							postAction += () => EarlyInitHelpers.AddEarlyInitFunction(() => m.InvokeOptimized(null));
						}
					}
				}
				if(type.IsCastableTo(typeof(ISystem))) {
					postAction += () => TypeManager.GetSystemTypeIndex(type);
					//Debug.Log(type);
				}
				else if(type.IsCastableTo(typeof(ComponentSystemBase))) {
					postAction += () => TypeManager.GetSystemTypeIndex(type);
					//var method = typeof(TypeManager).GetMemberCached("AddSystemTypeToTablesAfterInit") as MethodInfo;
					//method.InvokeOptimized(null, type);
				}
				postAction();
			}

			//var method = typeof(TypeManager).GetMemberCached("RegisterStaticAssemblyTypes") as MethodInfo;
			//HashSet<Type> hash = new();
			//method.InvokeOptimized(null, new Assembly[] { loadedAssembly }, hash, null);
			//method = typeof(TypeManager).GetMemberCached("InitializeSystemSharedStatics") as MethodInfo;
			//method.InvokeOptimized(null);
			Debug.Log("Loaded system assembly: " + loadedAssembly.FullName);
		}
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		public static void InitializeOnPlay() {
			if(loadedAssembly == null) {
				return;
			}

			liveSystems.Clear();
			liveManagedSystems.Clear();
			ActiveSystemNames.Clear();

			//TypeManager.Shutdown();
			//TypeManager.Initialize();
			postAction?.Invoke();
			postAction = null;

			InjectSystems();
		}

		public static void InjectSystems() {
			if(loadedAssembly == null) {
				Debug.LogWarning("No compiled systems loaded.");
				return;
			}
			if(ActiveSystemNames.Count > 0) {
				Debug.Log("Prevent Inject System because already injected");
				return;
			}

			var world = World.DefaultGameObjectInjectionWorld;
			if(world == null) {
				Debug.LogError("No active Default World.");
				return;
			}
			var simulationGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();

			ActiveSystemNames.Clear();

			foreach(var type in loadedAssembly.GetTypes()) {
				if(!type.IsClass && !type.IsValueType)
					continue;

				if(typeof(ISystem).IsAssignableFrom(type) && type.IsValueType) {
					// Inject ISystem (struct-based)
					var handle = world.GetOrCreateSystem(type);
					simulationGroup.AddSystemToUpdateList(handle);
					liveSystems.Add(handle);
					ActiveSystemNames.Add(type.FullName + " (ISystem)");
					Debug.Log($"Injected ISystem: {type.FullName}");
				}
				else if(typeof(SystemBase).IsAssignableFrom(type)) {
					// Inject SystemBase (class-based)
					var managedSystem = world.GetOrCreateSystemManaged(type);
					simulationGroup.AddSystemToUpdateList(managedSystem);
					liveManagedSystems.Add(managedSystem);
					ActiveSystemNames.Add(type.FullName + " (SystemBase)");
					Debug.Log($"Injected SystemBase: {type.FullName}");
				}
			}
		}

		public static void UninjectSystems() {
			if(loadedAssembly == null) return;

			var world = World.DefaultGameObjectInjectionWorld;
			if(world == null) return;

			if(Application.isPlaying) {
				var simulationGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
				foreach(var sys in liveSystems) {
					if(world.Unmanaged.IsSystemValid(sys)) {
						simulationGroup.RemoveSystemFromUpdateList(sys);
					}
				}
				foreach(var sys in liveManagedSystems) {
					simulationGroup.RemoveSystemFromUpdateList(sys);
				}
			}

			liveSystems.Clear();
			liveManagedSystems.Clear();
			ActiveSystemNames.Clear();
			Debug.Log("Cleared injected systems.");
		}
	}
}