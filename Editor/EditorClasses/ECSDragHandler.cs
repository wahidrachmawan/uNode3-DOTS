using UnityEngine;
using System.Collections.Generic;
using MaxyGames.UNode.Nodes;
using UnityEngine.UIElements;
using UnityEditor;
using System.Linq;
using System.Reflection;
using UnityEngine.Events;

namespace MaxyGames.UNode.Editors {
    class DragHandlerForBaseJobContainer : DragHandlerMenu {
		public override int order => int.MinValue;

		public override IEnumerable<DropdownMenuItem> GetMenuItems(DragHandlerData data) {
			if(data is DragHandlerDataForGraphElement d) {
				var draggedObj = d.draggedValue as BaseJobContainer;

				IEnumerable<DropdownMenuItem> DoAction(UGraphElement obj, string path = "") {
					yield return new DropdownMenuAction($"{path}Create Run", evt => {
						NodeEditorUtility.AddNewNode<JobEntityExecutor>(d.graphData, obj.name, null, d.mousePositionOnCanvas, n => {
							n.runWith = JobEntityExecutor.RunWith.Run;
							n.ReferenceNode = draggedObj;
							//For refreshing the graph editor
							uNodeGUIUtility.GUIChanged(draggedObj.GetUnityObject(), UIChangeType.Important);
						});
						d.graphEditor.Refresh();
					}, DropdownMenuAction.AlwaysEnabled);
					yield return new DropdownMenuAction($"{path}Create Schedule", evt => {
						NodeEditorUtility.AddNewNode<JobEntityExecutor>(d.graphData, obj.name, null, d.mousePositionOnCanvas, n => {
							n.runWith = JobEntityExecutor.RunWith.Schedule;
							n.ReferenceNode = draggedObj;
							//For refreshing the graph editor
							uNodeGUIUtility.GUIChanged(draggedObj.GetUnityObject(), UIChangeType.Important);
						});
						d.graphEditor.Refresh();
						d.graphEditor.Refresh();
					}, DropdownMenuAction.AlwaysEnabled);
					yield return new DropdownMenuAction($"{path}Create ScheduleParallel", evt => {
						NodeEditorUtility.AddNewNode<JobEntityExecutor>(d.graphData, obj.name, null, d.mousePositionOnCanvas, n => {
							n.runWith = JobEntityExecutor.RunWith.ScheduleParallel;
							n.ReferenceNode = draggedObj;
							//For refreshing the graph editor
							uNodeGUIUtility.GUIChanged(draggedObj.GetUnityObject(), UIChangeType.Important);
						});
						d.graphEditor.Refresh();
					}, DropdownMenuAction.AlwaysEnabled);
				}
				foreach(var v in DoAction(draggedObj)) {
					yield return v;
				}
			}
			yield break;
		}

		public override bool IsValid(DragHandlerData data) {
			if(data is DragHandlerDataForGraphElement d) {
				return d.draggedValue is BaseJobContainer && d.graphData.IsValidScope(NodeScope.ECSGraph);
			}
			return false;
		}
	}
}