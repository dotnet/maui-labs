namespace CometBaristaNotes.Models;

public class ShotEquipment
{
    public int ShotRecordId { get; set; }
    public int EquipmentId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ShotRecord? ShotRecord { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Equipment? Equipment { get; set; }
}
