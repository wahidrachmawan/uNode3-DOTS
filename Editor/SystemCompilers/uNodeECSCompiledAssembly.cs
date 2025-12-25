using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace MaxyGames.UNode.Editors {
	public class uNodeECSCompiledAssembly : ICompiledAssembly {
		public InMemoryAssembly InMemoryAssembly { get; set; }
		public string Name { get; }
		public string[] References { get; }
		public string[] Defines { get; }

		public uNodeECSCompiledAssembly(byte[] pe, byte[] pdb, string name, string[] refs, string[] defs) {
			InMemoryAssembly = new InMemoryAssembly(pe, pdb);
			Name = name;
			References = refs;
			Defines = defs;
		}
	}
}