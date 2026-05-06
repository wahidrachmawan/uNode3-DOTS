using UnityEngine;
using UnityEngine.UIElements;
using Unity.Mathematics;

namespace MaxyGames.UNode.Editors {
	[ControlField(typeof(double2))]
	public class Double2Control : ValueControl {
		public Double2Control(ControlConfig config, bool autoLayout = false) : base(config, autoLayout) {
			Init();
		}

		void Init() {
			Vector2Field field = new Vector2Field() {
				value = config.value is double2 val ? new Vector2((float)val.x, (float)val.y) : new Vector2(),
			};
			field.EnableInClassList("compositeField", false);

			field.RegisterValueChangedCallback((e) => {
				config.OnValueChanged(new double2(e.newValue.x, e.newValue.y));
				MarkDirtyRepaint();
			});
			Add(field);
		}
	}
}