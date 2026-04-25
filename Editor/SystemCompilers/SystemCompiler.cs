#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MaxyGames.CompilerBuilder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace MaxyGames.UNode.Editors {
	public static class SystemCompiler {
		public static string OutputPath => "Library/uNode_ECS/" + OutputName;
		public static string OutputDirectory => "Library/uNode_ECS";
		public static string OutputName => "SystemAssembly";
		public static string OutputDllPath => OutputPath + ".dll";
		public static string OutputPdbPath => OutputPath + ".pdb";

		static string ScriptAssemlyPath => "Library/ScriptAssemblies";

		public static string OutputProjectDirectory => GenerationUtility.generatedPath + Path.DirectorySeparatorChar + "ECS_System";

		private static int m_fileIndex = 0;

		internal static bool useAssemblyBuilder = false;

		internal class BuildProcessor : UnityEditor.Build.IPreprocessBuildWithReport, UnityEditor.Build.IPostprocessBuildWithReport {
			public int callbackOrder => int.MinValue + 1000;

			public void OnPreprocessBuild(BuildReport report) {
				var graphs = GraphUtility.FindAllGraphAssets().Where(obj => obj.GetType().FullName == "MaxyGames.UNode.ECSGraph").ToArray();
				var scripts = GenerationUtility.GenerateScriptForGraphs(graphs, "Build_ECS_System");
				var dir = OutputProjectDirectory;
				foreach(var script in scripts) {
					string path;
					if(string.IsNullOrWhiteSpace(script.Namespace) == false) {
						Directory.CreateDirectory(dir + Path.DirectorySeparatorChar + script.Namespace);
						path = Path.GetFullPath(dir) + Path.DirectorySeparatorChar + script.Namespace + Path.DirectorySeparatorChar + script.fileName + ".cs";
					}
					else {
						path = Path.GetFullPath(dir) + Path.DirectorySeparatorChar + script.fileName + ".cs";
					}
					List<ScriptInformation> informations;
					var generatedScript = script.ToScript(out informations, true);
					using(StreamWriter sw = new StreamWriter(path)) {
						if(informations != null) {
							uNodeEditor.SavedData.RegisterGraphInfos(informations, script.graphOwner, path);
						}
						sw.Write(GenerationUtility.ConvertLineEnding(generatedScript, Application.platform != RuntimePlatform.WindowsEditor));
						sw.Close();
					}
				}
			}

			public void OnPostprocessBuild(BuildReport report) {
				// Clean up generated scripts after build
				var buildScripts = Directory.GetFiles(OutputProjectDirectory, "*.cs", SearchOption.AllDirectories);
				foreach(var script in buildScripts) {
					try {
						File.Delete(script);
					} catch (Exception ex) {
						Debug.LogError($"Failed to delete build script {script}: {ex.Message}");
					}
				}
			}
		}

		public static void GenerateAndCompileGraphs() {
			var graphs = GraphUtility.FindAllGraphAssets().Where(obj => obj.GetType().FullName == "MaxyGames.UNode.ECSGraph").ToArray();
			var scripts = GenerationUtility.GenerateScriptForGraphs(graphs, "ECS_System");
			CompileScripts(scripts.Select(s => GenerationUtility.GetGraphData(s.graphOwner).path).ToArray());
		}

		public static void CompileScripts(string[] scriptPaths, Action<bool> callback = null) {
			if(useAssemblyBuilder) {
				if(File.Exists(OutputPath + m_fileIndex + ".dll")) {
					try {
						File.Delete(OutputPath + m_fileIndex + ".dll");
						File.Delete(OutputPath + m_fileIndex + ".pdb");
					} catch { }
				}

				var path = OutputPath + ++m_fileIndex;
				Directory.CreateDirectory(OutputDirectory);
				// Use AssemblyBuilder
				var builder = new AssemblyBuilder(path, scriptPaths);

				builder.flags = AssemblyBuilderFlags.EditorAssembly;
				builder.referencesOptions = ReferencesOptions.UseEngineModules;
				
				if(RoslynUtility.AssemblyCSharp != null) {
					builder.additionalDefines = RoslynUtility.AssemblyCSharp.defines;
					builder.additionalReferences = RoslynUtility.AssemblyCSharp.allReferences.Append(RoslynUtility.AssemblyCSharp.outputPath).ToArray();
				}

				builder.buildFinished += (path, result) => {
					bool valid = true;
					foreach(var msg in result) {
						if(msg.type == CompilerMessageType.Error) {
							Debug.LogError($"{msg.type}: {msg.message}");
							valid = false;
						}
						else if(msg.type == CompilerMessageType.Warning) {
							Debug.LogWarning($"{msg.type}: {msg.message}");
						}
						else {
							Debug.Log($"{msg.type}: {msg.message}");
						}
					}
					if(valid == false) {
						Debug.LogError("Compile failed");
						return;
					}
					//var rawAssembly = File.ReadAllBytes(path + ".dll");
					//var rawPdb = File.ReadAllBytes(path + ".pdb");
					RunILPP(path, out var rawAssembly, out var rawPdb);

					File.WriteAllBytes(OutputDllPath, rawAssembly);
					File.WriteAllBytes(OutputPdbPath, rawPdb);

					Debug.Log("Compiled to: " + OutputPath);

					HotReloadSystemManager.LoadCompiledAssembly(OutputDllPath);
					
					callback?.Invoke(true);

					if(Application.isPlaying) {
						HotReloadSystemManager.UninjectSystems(false);
						HotReloadSystemManager.InjectSystems(false);
					}
				};

				builder.buildStarted += path => Debug.Log($"Starting compile {scriptPaths.Length} scripts.\n" + string.Join('\n', scriptPaths.Select(p => "Path => " + p)));

				if(!builder.Build()) {
					Debug.LogError("Compile failed to start");
					callback?.Invoke(false);
				}
			}
			else {
				var filename = "SystemTemp" + ++m_fileIndex;
				Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

				if(RoslynUtility.AssemblyCSharp != null) {
					var option = RoslynCodeCompiler.CreateCompilerOption(RoslynUtility.AssemblyCSharp, filename);
					option.References = option.References.Append(Path.GetFullPath(RoslynUtility.AssemblyCSharp.outputPath)).ToArray();
					option.SourceFiles = scriptPaths;

					int progressID = -1;
					float time = uNodeThreadUtility.time;
					float total = 3;
					bool progresFinish = false;
					const float progressTimeout = 10;

					uNodeThreadUtility.ExecuteWhile(() => {
						var current = uNodeThreadUtility.time - time;
						if(progresFinish || current >= progressTimeout) {
							// We've finished - remove progress bar.
							if(Progress.Exists(progressID)) {
								Progress.Remove(progressID);
								progressID = -1;
							}
							return false;
						}
						float curr = current;
						if(curr >= total) {
							curr = total;
						}
						// Do we need to create the progress bar?
						if(!Progress.Exists(progressID)) {
							progressID = Progress.Start(
								"uNode - Compiling ECS Graphs",
								"Compiling ECS System...",
								Progress.Options.Managed);
						}

						Progress.Report(
							progressID,
							curr / total + 0.1f,
							$"Compiling ECS System....");
						return true;
					}, static () => { });

					Debug.Log("Starting compiling ECS Graphs...");
					RoslynCodeCompiler.Run(option, result => {
						try {
							progresFinish = true;
							if(result.Success) {
								var dllPath = result.OutputPath;
								var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
								if(File.Exists(dllPath) && File.Exists(pdbPath)) {
									File.Copy(dllPath, OutputDllPath, true);
									File.Copy(pdbPath, OutputPdbPath, true);

									Debug.Log("Compiled to: " + OutputPath);

									uNodeThreadUtility.Queue(() => {
										HotReloadSystemManager.LoadCompiledAssembly(OutputDllPath);

										callback?.Invoke(true);

										if(Application.isPlaying) {
											HotReloadSystemManager.UninjectSystems(false);
											HotReloadSystemManager.InjectSystems(false);
										}
									});
								}
								else {
									throw null;
								}
							}
							else {
								Debug.LogError("Compile failed");
								callback?.Invoke(false);
							}
						}
						catch(Exception ex) {
							Debug.LogException(ex);
						}
					});
				}
			}
		}

		static Type loaderType => "Unity.Burst.Editor.BurstLoader".ToType(false);
		static Type burstCompilerType => "Unity.Burst.BurstCompiler".ToType(false);

		static DateTime m_LastCompiledTime;
		static int m_BurstCompileIndex;
		static List<string> m_AllSystemAssemblies = new();

		internal static void NotifyBurst(string dllPath) {
			if(loaderType == null || burstCompilerType == null) return;
			var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
			if(RoslynUtility.AssemblyCSharp != null && File.Exists(dllPath) && File.Exists(pdbPath)) {
				var destDllPath = Path.Combine(ScriptAssemlyPath, OutputName + (++m_BurstCompileIndex) + ".dll");
				var destPdbPath = Path.ChangeExtension(destDllPath, ".pdb");

				var lastWriteTime = File.GetLastWriteTime(dllPath);
				//Notify only when the system compiler is changed.
				if(lastWriteTime != m_LastCompiledTime || File.Exists(destDllPath) == false) {
					m_LastCompiledTime = lastWriteTime;

#if UNODE_DEV
					Debug.Log("Notified Burst Compiler");
#endif

					File.Copy(dllPath, destDllPath, true);
					File.Copy(pdbPath, destPdbPath, true);

					NotifyCompilationStarted();

					foreach(var p in m_AllSystemAssemblies) {
						var path = Path.GetFullPath(p);
						//Debug.Log("Skipping system assembly for burst: " + path);
						NotifyAssemblyCompilationNotRequired(path);
					}

					var finishedPath = Path.GetFullPath(destDllPath);
					foreach(var path in Directory.GetFiles(ScriptAssemlyPath, "*.dll")) {
						var p = Path.GetFullPath(path);
						if(finishedPath == p) {
							continue;
						}
						NotifyAssemblyCompilationNotRequired(p);
					}
					NotifyAssemblyCompilationFinished(finishedPath, RoslynUtility.AssemblyCSharp.defines);
					NotifyCompilationFinished();

					m_AllSystemAssemblies.Add(destDllPath);
				}
			}

			static void NotifyCompilationStarted() {
				var notifyCompilationStarted = burstCompilerType.GetMemberCached("NotifyCompilationStarted") as MethodInfo;
				var getAssemblyFolders = loaderType.GetMemberCached("GetAssemblyFolders") as MethodInfo;
				notifyCompilationStarted.InvokeOptimized(null, getAssemblyFolders.InvokeOptimized(null), new string[0]);
			}

			static void NotifyAssemblyCompilationFinished(string path, string[] defines) {
				burstCompilerType.GetMemberCached("NotifyAssemblyCompilationFinished").ConvertTo<MethodInfo>().InvokeOptimized(null, Path.GetFileNameWithoutExtension(path), defines);
			}

			static void NotifyAssemblyCompilationNotRequired(string path) {
				burstCompilerType.GetMemberCached("NotifyAssemblyCompilationNotRequired").ConvertTo<MethodInfo>().InvokeOptimized(null, Path.GetFileNameWithoutExtension(path));
			}

			static void NotifyCompilationFinished() {
				burstCompilerType.GetMemberCached("NotifyCompilationFinished").ConvertTo<MethodInfo>().InvokeOptimized(null);
			}
		}

		//public static void CompileAllCSX() {
		//	string fullPath = Path.Combine(Directory.GetCurrentDirectory(), CSXPath);
		//	string[] csxFiles = Directory.GetFiles(fullPath, "*.csx", SearchOption.AllDirectories);

		//	if(csxFiles.Length == 0) {
		//		Debug.LogWarning("No .csx files found.");
		//		return;
		//	}
		//	CompileScripts(csxFiles, success => {
		//		if(success) {
		//			Debug.Log("All .csx files compiled successfully.");
		//		}
		//		else {
		//			Debug.LogError("Failed to compile .csx files.");
		//		}
		//	});
		//}

		static void RunILPP(string path, out byte[] rawAssembly, out byte[] rawPdb) {
			rawAssembly = null;
			rawPdb = null;
			var ilpp = new List<ILPostProcessor>();
			var references = new List<string>();
			foreach(var assembly in EditorReflectionUtility.GetAssemblies()) {
				var name = assembly.GetName().Name;
				if(name.StartsWith("Unity.") && name.EndsWith("CodeGen")) {
					//Debug.Log("Gen: " + name);
					foreach(var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && (typeof(ILPostProcessor).IsAssignableFrom(t)))) {
						ilpp.Add((ILPostProcessor)Activator.CreateInstance(type));
					}
				}
				//Skip AssetStoreTools assembly
				if(assembly.GetName().Name.StartsWith("AssetStoreTools", StringComparison.Ordinal))
					continue;
				var loc = assembly.Location;
				if(!string.IsNullOrEmpty(loc)) {
					references.Add(assembly.Location);
				}
			}
			var rawAssembly1 = File.ReadAllBytes(path + ".dll");
			var rawPdb1 = File.ReadAllBytes(path + ".pdb");
			var compiledAssembly = new uNodeECSCompiledAssembly(rawAssembly1, rawPdb1, path, references.ToArray(), new string[0]);
			foreach(var v in ilpp) {
				if(v.GetType().Name == "BurstILPostProcessor") continue;
				if(v.WillProcess(compiledAssembly)) {
					var iLPostProcessResult = v.Process(compiledAssembly);
					//Debug.Log(v.GetType() + ": " + iLPostProcessResult);
					if(iLPostProcessResult == null) {
						continue;
					}
					//if(iLPostProcessResult.Diagnostics != null) {
					//	Debug.Log(string.Join('\n', iLPostProcessResult.Diagnostics.Select(v => v.MessageData)));
					//}
					if(iLPostProcessResult.InMemoryAssembly != null) {
						if(iLPostProcessResult.InMemoryAssembly.PeData == null) {
							Debug.Log("ILPostProcessor produced an assembly without PE data");
						}
						compiledAssembly.InMemoryAssembly = iLPostProcessResult.InMemoryAssembly;
					}
				}
			}
			rawAssembly = compiledAssembly.InMemoryAssembly.PeData;
			rawPdb = compiledAssembly.InMemoryAssembly.PdbData;
		}
	}
}