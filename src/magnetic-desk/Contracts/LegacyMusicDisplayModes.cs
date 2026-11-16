namespace MagneticDesk.Contracts;

/// <summary>
/// One owner for the feature-section settings-page writes: the music and
/// weather presentation options, the weather location policy, the feature-card
/// enable states and the section's misc presentation picks (attachment
/// storage, managed-drop action, folder-open behavior). Every write follows
/// the original section semantics — normalize through the existing shared
/// normalizers (including <c>WeatherSettingsPolicy</c>), skip unchanged
/// writes, store, and schedule one debounced save. Widget creation, hiding
/// and runtime start/stop stay on the existing WidgetManager /
/// SettingsChanged chains owned by the host; the enable port only writes the
/// persisted flag, exactly like the shell did before the migration.
/// </summary>
/// <summary>
/// Canonical music display-mode values, owned here so the feature editor can
/// build its option list without referencing the settings adapter.
/// <see cref="Services.SettingsService"/> keeps its historical constants as
/// aliases of these.
/// </summary>
public static class MusicDisplayModes
{
    public const string Auto = "Auto";
    public const string Cover = "Cover";
    public const string Controls = "Controls";
    public const string RecordVertical = "RecordVertical";
    public const string RecordHorizontal = "RecordHorizontal";
}
