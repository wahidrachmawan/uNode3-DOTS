using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.IO;

namespace MaxyGames.UNode.Editors {
	public class SystemHotReloadWindow : EditorWindow {
		[MenuItem("Tools/uNode ECS/System Hot Reload")]
		public static void Open() => GetWindow<SystemHotReloadWindow>("DOTS System Hot Reload");

		private ListView listView;

		void OnEnable() {
			var root = rootVisualElement;
			var compileButton = new Button(() => {
				SystemCompiler.CompileAllCSX();
			}) { text = "Compile + Reload" };

			listView = new ListView {
				makeItem = () => new Label(),
				bindItem = (el, i) => ((Label)el).text = HotReloadSystemManager.ActiveSystemNames[i],
				fixedItemHeight = 18,
				selectionType = SelectionType.None
			};

			listView.itemsSource = HotReloadSystemManager.ActiveSystemNames;

			root.Add(compileButton);
			root.Add(new Button(() => {
				HotReloadSystemManager.InjectSystems();
			}) { text = "Inject Systems (Manual)" });
			root.Add(new Button(() => {
				HotReloadSystemManager.UninjectSystems();
			}) { text = "UnInject Systems (Manual)" });
			root.Add(new Label("Active Systems:"));
			root.Add(listView);
		}

		void Update() {
			listView.RefreshItems();
		}
	}
}