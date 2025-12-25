#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaxyGames.UNode;
using MaxyGames.UNode.Editors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace MaxyGames.UNode.Editors {
	public static class SystemCompiler {
		public static string OutputPath => "Library/uNode_ECS/" + OutputName;
		public static string OutputDirectory => "Library/uNode_ECS";
		public static string OutputName => "SystemAssembly";
		public static string CSXPath => ""; // Relative to project root
		private static int m_fileIndex = 0;

		internal static bool useAssemblyBuilder = true;

		public static void CompileAllCSX() {
			string fullPath = Path.Combine(Directory.GetCurrentDirectory(), CSXPath);
			string[] csxFiles = Directory.GetFiles(fullPath, "*.csx", SearchOption.AllDirectories);

			if(csxFiles.Length == 0) {
				Debug.LogWarning("No .csx files found.");
				return;
			}
			CompileScripts(csxFiles, success => {
				if(success) {
					Debug.Log("All .csx files compiled successfully.");
				}
				else {
					Debug.LogError("Failed to compile .csx files.");
				}
			});
		}

		public static void CompileScripts(string[] scriptPaths, Action<bool> callback = null) {
			if(useAssemblyBuilder) {
				var path = OutputPath + ++m_fileIndex;
				Directory.CreateDirectory(OutputDirectory);
				// Use AssemblyBuilder
				var builder = new AssemblyBuilder(path, scriptPaths);

				builder.additionalReferences = RoslynUtility.AssemblyCSharp.allReferences.Append(RoslynUtility.AssemblyCSharp.outputPath).ToArray();

				builder.buildFinished += (path, result) => {
					Debug.Log("Compileds: " + path);
					foreach(var msg in result) {
						Debug.Log($"{msg.type}: {msg.message}");
					}
					RunILPP(path, out var rawAssembly, out var rawPdb);

					File.WriteAllBytes(OutputPath + ".dll", rawAssembly);
					File.WriteAllBytes(OutputPath + ".pdb", rawPdb);

					HotReloadSystemManager.LoadCompiledAssembly(OutputPath + ".dll");
					callback?.Invoke(true);
				};

				builder.buildStarted += path => Debug.Log("Starting build: " + path);

				if(!builder.Build()) {
					Debug.LogError("Build failed to start");
					callback?.Invoke(false);
				}
			}
			else {
				//var syntaxTrees = csxFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file))).ToList();

				//var references = AppDomain.CurrentDomain.GetAssemblies()
				//	.Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
				//	.Select(a => MetadataReference.CreateFromFile(a.Location))
				//	.Cast<MetadataReference>();

				//var compilation = CSharpCompilation.Create("HotReloadSystemTemp")
				//	.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
				//	.AddReferences(references)
				//	.AddSyntaxTrees(syntaxTrees);
				//var result = compilation.Emit(OutputPath);

				var filename = "SystemTemp" + ++m_fileIndex;
				Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
				var result = RoslynUtility.CompileFilesAndSave(filename, scriptPaths, OutputPath + ".dll", false);

				if(!result.isSuccess) {
					result.LogErrors();
					callback?.Invoke(false);
				}
				else {

					Debug.Log("Compiled systems successfully.");
					HotReloadSystemManager.LoadCompiledAssembly(OutputPath + ".dll");
					callback?.Invoke(true);
				}
			}
		}

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