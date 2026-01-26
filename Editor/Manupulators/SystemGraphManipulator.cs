using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;
using System.Collections;
using Unity.Entities;
using Unity.Collections;
using UnityEngine.UIElements;
using MaxyGames.UNode.Nodes;

namespace MaxyGames.UNode.Editors {
	class SystemGraphManipulator : GraphManipulator {
		public override bool IsValid(string action) {
			return graph is ECSGraph;
		}

		public override bool HandleCommand(string command) {
			if(command == Command.CompileCurrentGraph) {
				Compile();
				return true;
			}
			else if(command == Command.CompileGraph) {
				Compile();
				return true;
			}
			return base.HandleCommand(command);
		}

		private void Compile() {
			SystemCompiler.GenerateAndCompileGraphs();
		}
	}
}