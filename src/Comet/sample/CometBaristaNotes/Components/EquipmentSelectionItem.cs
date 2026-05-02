using System.ComponentModel;

namespace CometBaristaNotes.Components;

/// <summary>
/// Selection state for equipment lists rendered with Comet-native views.
/// </summary>
public class EquipmentSelectionItem : INotifyPropertyChanged
{
	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public EquipmentType EquipmentType { get; set; }
	public string TypeName => EquipmentType.ToString();

	bool _isSelected;
	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if (_isSelected != value)
			{
				_isSelected = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}
