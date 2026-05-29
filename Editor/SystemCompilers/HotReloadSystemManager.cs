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
					//World.DisposeAllWorlds();
				}
			};
		}

		struct SystemData {
			public SystemHandle systemHandle;
			public ComponentSystemBase managedSystem;
			public Type systemType;

			public bool IsManagedSystem => managedSystem != null;
			public SystemTypeIndex GetSystemTypeIndex => TypeManager.GetSystemTypeIndex(systemType);
			public Unity.Collections.NativeList<TypeManager.SystemAttribute> UpdateInGroupAttributes => TypeManager.GetSystemAttributes(GetSystemTypeIndex, TypeManager.SystemAttributeKind.UpdateInGroup);
		}

		private static Assembly loadedAssembly {
			get => ECSRuntimeUtility.loadedAssembly;
			set => ECSRuntimeUtility.loadedAssembly = value;
		}
		private static List<SystemData> allActiveSystems = new();
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
			};

			SystemCompiler.NotifyBurst(path);

			loadedAssembly = Assembly.Load(File.ReadAllBytes(path), File.ReadAllBytes(Path.ChangeExtension(path, ".pdb")));
			foreach(var t in loadedAssembly.GetTypes()) {
				var type = t;
				if(type.Name.StartsWith("__")) {
					var methods = type.GetMethods();
					foreach(var m in methods) {
						if(m.IsDefined(typeof(RuntimeInitializeOnLoadMethodAttribute), false) || m.IsDefined(typeof(InitializeOnLoadMethodAttribute), false)) {
							//Debug.Log(m);
							var method = m;
							postAction += () => EarlyInitHelpers.AddEarlyInitFunction(() => method.InvokeOptimized(null));
						}
					}
				}
				//if(type.IsCastableTo(typeof(ISystem))) {
				//	//postAction += () => TypeManager.GetSystemTypeIndex(type);
				//	//var method = typeof(TypeManager).GetMemberCached("AddSystemTypeToTablesAfterInit") as MethodInfo;
				//	//method.InvokeOptimized(null, type);
				//}
				//else if(type.IsCastableTo(typeof(ComponentSystemBase))) {
				//	//postAction += () => TypeManager.GetSystemTypeIndex(type);
				//	//var method = typeof(TypeManager).GetMemberCached("AddSystemTypeToTablesAfterInit") as MethodInfo;
				//	//method.InvokeOptimized(null, type);
				//}
				//else if(type.IsCastableTo(typeof(IComponentData))) {
				//	//postAction += () => TypeManager.GetTypeIndex(type);
				//	//Debug.Log("ComponentType () => " + type.FullName);
				//	//var method = typeof(TypeManager).GetMemberCached("GetOrCreateTypeIndex") as MethodInfo;
				//	//method.InvokeOptimized(null, type);
				//}
			}

			TypeManager.Shutdown();
			TypeManager.Initialize();

//#if !DISABLE_TYPEMANAGER_ILPP
//			//postAction += static () => {
//			//};
//			try {
//				var method = typeof(TypeManager).GetMemberCached("RegisterStaticAssemblyTypes") as MethodInfo;
//				var asm = new Assembly[] { loadedAssembly };
//				method.InvokeOptimized(null, asm, new HashSet<Type>(), new List<Type>());
//				method = typeof(TypeManager).GetMemberCached("InitializeSystemSharedStatics") as MethodInfo;
//				method.InvokeOptimized(null);
//			}
//			catch(Exception ex) {
//				Debug.LogException(ex);
//			}
//#endif
			postAction();
			postAction = null;

			EarlyInitHelpers.FlushEarlyInits();
			SystemBaseRegistry.InitializePendingTypes();

			Debug.Log("Loaded system assembly: " + loadedAssembly.FullName);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void InitializeOnPlay() {
			if(loadedAssembly == null) {
				var path = SystemCompiler.OutputPath + ".dll";
				if(File.Exists(path)) {
					LoadCompiledAssembly(path);
				}
				else {
					//Skip if no compiled assembly found, which can happen when entering play mode before any graph is compiled. Systems will be injected when the some graph is compiled and loaded.
					return;
				}
			}

			allActiveSystems.Clear();
			ActiveSystemNames.Clear();

			//TypeManager.Shutdown();
			//TypeManager.Initialize();
			postAction?.Invoke();
			postAction = null;

			if(SystemHotReloadWindow.AutoInject) {
				InjectSystems();
			}

			if(loadedAssembly != null) {
				ReflectionUtils.RegisterRuntimeAssembly(loadedAssembly);
			}
		}

		public static void InjectSystems(bool log = true) {
			if(loadedAssembly == null) {
				if(log)
					Debug.LogWarning("No compiled systems loaded.");
				return;
			}
			if(Application.isPlaying == false) {
				if(log)
					Debug.Log("Prevent Inject System because not in play mode");
				return;
			}
			if(ActiveSystemNames.Count > 0) {
				if(log)
					Debug.Log("Prevent Inject System because already injected");
				return;
			}

			var world = World.DefaultGameObjectInjectionWorld;
			if(world == null) {
				Debug.LogError("No active Default World.");
				return;
			}
			//var simulationGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
			ActiveSystemNames.Clear();

			var list = StaticListPool<Type>.Allocate();

			try {
				foreach(var type in loadedAssembly.GetTypes()) {
					if(!type.IsClass && !type.IsValueType)
						continue;
					if(typeof(ISystem).IsAssignableFrom(type) && type.IsValueType) {
						// Inject ISystem (struct-based)
						var handle = world.GetOrCreateSystem(type);
						//simulationGroup.AddSystemToUpdateList(handle);

						allActiveSystems.Add(new SystemData() {
							systemHandle = handle,
							systemType = type,
						});

						ActiveSystemNames.Add(type.FullName + " (ISystem)");
						list.Add(type);
						//Debug.Log($"Injected ISystem: {type.FullName}");
					}
					else if(typeof(SystemBase).IsAssignableFrom(type)) {
						// Inject SystemBase (class-based)
						var managedSystem = world.GetOrCreateSystemManaged(type);
						//simulationGroup.AddSystemToUpdateList(managedSystem);

						allActiveSystems.Add(new SystemData() {
							systemHandle = managedSystem.SystemHandle,
							managedSystem = managedSystem,
							systemType = type,
						});

						ActiveSystemNames.Add(type.FullName + " (SystemBase)");
						list.Add(type);
						//Debug.Log($"Injected SystemBase: {type.FullName}");
					}
				}

				if(list.Count > 0) {
					DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, list);

					Debug.Log($"Injected {list.Count} systems.\n" + string.Join('\n', list.Select(item => item.IsValueType ?
						$"ISystem => {item.PrettyName(true)}" :
						$"SystemBase => {item.PrettyName(true)}")));
				}
			}
			catch (Exception ex) {
				Debug.LogException(new Exception("Error on injecting system, possible because of unknow error try Reload Domain or it is because of TypeManager. Try add `DISABLE_TYPEMANAGER_ILPP` define symbol.", ex));
				throw;
			}
			finally {
				StaticListPool<Type>.Free(list);
			}
			//else {
			//	Debug.LogWarning("No systems found to inject in the loaded assembly.");
			//}
		}

		public static void UninjectSystems(bool log = true) {
			if(loadedAssembly == null) return;

			var world = World.DefaultGameObjectInjectionWorld;
			if(world == null) return;

			var initializationGroup = world.GetExistingSystemManaged<InitializationSystemGroup>();
			var simulationGroup = world.GetExistingSystemManaged<SimulationSystemGroup>();
			var presentationGroup = world.GetExistingSystemManaged<PresentationSystemGroup>();
			foreach(var sys in allActiveSystems) {
				if(sys.IsManagedSystem) {
					initializationGroup?.RemoveSystemFromUpdateList(sys.managedSystem);
					simulationGroup?.RemoveSystemFromUpdateList(sys.managedSystem);
					presentationGroup?.RemoveSystemFromUpdateList(sys.managedSystem);
					var attrs = sys.UpdateInGroupAttributes;
					if(attrs.Length > 0) {
						foreach(var attr in attrs) {
							var groupTypeIndex = attr.TargetSystemTypeIndex;
							var componentSystem = world.GetExistingSystemManaged(groupTypeIndex);
							if(componentSystem is ComponentSystemGroup group) {
								group.RemoveSystemFromUpdateList(sys.managedSystem);
							}
						}
					}
				}
				else {
					if(world.Unmanaged.IsSystemValid(sys.systemHandle)) {
						initializationGroup?.RemoveSystemFromUpdateList(sys.systemHandle);
						simulationGroup?.RemoveSystemFromUpdateList(sys.systemHandle);
						presentationGroup?.RemoveSystemFromUpdateList(sys.systemHandle);
						var attrs = sys.UpdateInGroupAttributes;
						if(attrs.Length > 0) {
							foreach(var attr in attrs) {
								var groupTypeIndex = attr.TargetSystemTypeIndex;
								var componentSystem = world.GetExistingSystemManaged(groupTypeIndex);
								if(componentSystem is ComponentSystemGroup group) {
									group.RemoveSystemFromUpdateList(sys.managedSystem);
								}
							}
						}
					}
				}
			}
			// Clear the list of active systems after uninjecting them.
			allActiveSystems.Clear();
			ActiveSystemNames.Clear();

			//Sort the groups after removing systems to make sure the update order is correct.
			initializationGroup?.SortSystems();
			simulationGroup?.SortSystems();
			presentationGroup?.SortSystems();

			if(log)
				Debug.Log("Cleared injected systems.");
		}
	}
}