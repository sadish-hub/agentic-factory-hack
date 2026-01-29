namespace RepairPlanner.Services;

/// <summary>
/// Interface for mapping fault types to required skills and parts.
/// </summary>
public interface IFaultMappingService
{
    /// <summary>
    /// Gets the list of skills required to repair a given fault type.
    /// </summary>
    IReadOnlyList<string> GetRequiredSkills(string faultType);

    /// <summary>
    /// Gets the list of part numbers required to repair a given fault type.
    /// </summary>
    IReadOnlyList<string> GetRequiredParts(string faultType);
}

/// <summary>
/// Hardcoded mappings from fault types to required skills and parts.
/// In a real-world scenario, this would be fetched from a dedicated system.
/// </summary>
public sealed class FaultMappingService : IFaultMappingService
{
    // Fault → Required Skills mapping
    // StringComparer.OrdinalIgnoreCase makes lookups case-insensitive (like Python's .lower())
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FaultToSkills =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["curing_temperature_excessive"] = new[]
            {
                "tire_curing_press", "temperature_control", "instrumentation",
                "electrical_systems", "plc_troubleshooting", "mold_maintenance"
            },
            ["curing_cycle_time_deviation"] = new[]
            {
                "tire_curing_press", "plc_troubleshooting", "mold_maintenance",
                "bladder_replacement", "hydraulic_systems", "instrumentation"
            },
            ["building_drum_vibration"] = new[]
            {
                "tire_building_machine", "vibration_analysis", "bearing_replacement",
                "alignment", "precision_alignment", "drum_balancing", "mechanical_systems"
            },
            ["ply_tension_excessive"] = new[]
            {
                "tire_building_machine", "tension_control", "servo_systems",
                "precision_alignment", "sensor_alignment", "plc_programming"
            },
            ["extruder_barrel_overheating"] = new[]
            {
                "tire_extruder", "temperature_control", "rubber_processing",
                "screw_maintenance", "instrumentation", "electrical_systems", "motor_drives"
            },
            ["low_material_throughput"] = new[]
            {
                "tire_extruder", "rubber_processing", "screw_maintenance",
                "motor_drives", "temperature_control"
            },
            ["high_radial_force_variation"] = new[]
            {
                "tire_uniformity_machine", "data_analysis", "measurement_systems",
                "tire_building_machine", "tire_curing_press"
            },
            ["load_cell_drift"] = new[]
            {
                "tire_uniformity_machine", "load_cell_calibration", "measurement_systems",
                "sensor_alignment", "instrumentation"
            },
            ["mixing_temperature_excessive"] = new[]
            {
                "banbury_mixer", "temperature_control", "rubber_processing",
                "instrumentation", "electrical_systems", "mechanical_systems"
            },
            ["excessive_mixer_vibration"] = new[]
            {
                "banbury_mixer", "vibration_analysis", "bearing_replacement",
                "alignment", "mechanical_systems", "preventive_maintenance"
            }
        };

    // Fault → Required Parts mapping
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FaultToParts =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["curing_temperature_excessive"] = new[] { "TCP-HTR-4KW", "GEN-TS-K400" },
            ["curing_cycle_time_deviation"] = new[] { "TCP-BLD-800", "TCP-SEAL-200" },
            ["building_drum_vibration"] = new[] { "TBM-BRG-6220" },
            ["ply_tension_excessive"] = new[] { "TBM-LS-500N", "TBM-SRV-5KW" },
            ["extruder_barrel_overheating"] = new[] { "EXT-HTR-BAND", "GEN-TS-K400" },
            ["low_material_throughput"] = new[] { "EXT-SCR-250", "EXT-DIE-TR" },
            ["high_radial_force_variation"] = Array.Empty<string>(),
            ["load_cell_drift"] = new[] { "TUM-LC-2KN", "TUM-ENC-5000" },
            ["mixing_temperature_excessive"] = new[] { "BMX-TIP-500", "GEN-TS-K400" },
            ["excessive_mixer_vibration"] = new[] { "BMX-BRG-22320", "BMX-SEAL-DP" }
        };

    // Default values for unknown fault types
    private static readonly IReadOnlyList<string> DefaultSkills = new[] { "general_maintenance" };
    private static readonly IReadOnlyList<string> DefaultParts = Array.Empty<string>();

    public IReadOnlyList<string> GetRequiredSkills(string faultType)
    {
        // TryGetValue returns true if found, out parameter gets the value
        // ?? is "if null use this" (like Python's "or")
        return FaultToSkills.TryGetValue(faultType, out var skills)
            ? skills
            : DefaultSkills;
    }

    public IReadOnlyList<string> GetRequiredParts(string faultType)
    {
        return FaultToParts.TryGetValue(faultType, out var parts)
            ? parts
            : DefaultParts;
    }
}
