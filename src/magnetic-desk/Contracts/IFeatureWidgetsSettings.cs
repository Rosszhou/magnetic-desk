using MagneticDesk.Models;

namespace MagneticDesk.Contracts;

public interface IFeatureWidgetsSettings
{

    /// <summary>
    /// Writes one feature card's persisted enable state (Music / Weather /
    /// Glance / later feature kinds). Does not save: the caller's existing
    /// WidgetManager sync chain owns persistence for this path, unchanged.
    /// </summary>
    void SetFeatureWidgetEnabled(WidgetKind kind, bool enabled);

    bool SetAttachmentStorageMode(string? mode);

    bool SetManagedDropAction(string? action);

    bool SetFileWidgetFolderOpenBehavior(string? behavior);

    /// <summary>
    /// Reads the persisted global folder-open behavior, normalized through
    /// <see cref="FileWidgetFolderOpenBehaviors"/> (batch 45 read port for
    /// the file-widget overview combo).
    /// </summary>
    string ReadFileWidgetFolderOpenBehavior();

    /// <summary>
    /// Reads the persisted attachment storage mode, normalized through
    /// <see cref="AttachmentStorageModes"/> (batch 50 read port for the
    /// General section's attachment-storage combo, whose binding surface
    /// lives on the feature-widgets editor).
    /// </summary>
    string ReadAttachmentStorageMode();
}
