using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.IO;
using System;

namespace MaxyGames.UNode.Editors {
	public class SystemHotReloadWindow : EditorWindow {
		[MenuItem("Tools/uNode ECS/System Hot Reload")]
		public static void Open() => GetWindow<SystemHotReloadWindow>("System Hot Reload");

		public static bool AutoInject {
			get => EditorPrefs.GetBool("UNODE_DOTS_AUTO_INJECT", true);
			set {
				EditorPrefs.SetBool("UNODE_DOTS_AUTO_INJECT", value);
			}
		}

		private ListView listView;

		void OnEnable() {
			var root = rootVisualElement;
			var compileButton = new Button(() => {
				SystemCompiler.GenerateAndCompileGraphs();
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
				HotReloadSystemManager.EnsureLoadCompiledAssembly();
				HotReloadSystemManager.InjectSystems();
				listView.RefreshItems();
			}) { text = "Inject Systems (Manual)" });
			root.Add(new Button(() => {
				HotReloadSystemManager.UninjectSystems();
				listView.RefreshItems();
			}) { text = "UnInject Systems (Manual)" });

			var toggle = new Toggle("Enable Auto Inject");
			toggle.SetValueWithoutNotify(AutoInject);
			toggle.RegisterValueChangedCallback(evt => {
				AutoInject = evt.newValue;
			});
			root.Add(toggle);

			root.Add(new Label("Active Systems:"));
			root.Add(listView);
			listView.RefreshItems();

			var spacer = new VisualElement();
			spacer.style.flexGrow = 1;

			root.Add(spacer);

			root.Add(new Button(() => {
				Refresh();
			}) { text = "Refresh" });

			EditorApplication.playModeStateChanged += OnPlayModeChanged;
		}

		private void OnDisable() {
			EditorApplication.playModeStateChanged -= OnPlayModeChanged;
		}

		private void OnPlayModeChanged(PlayModeStateChange change) {
			if (change == PlayModeStateChange.EnteredPlayMode) {
				Refresh();
			}
			else if (change == PlayModeStateChange.ExitingPlayMode) {
				Refresh();
			}
		}

		void Refresh() {
			listView.RefreshItems();
		}
	}
}