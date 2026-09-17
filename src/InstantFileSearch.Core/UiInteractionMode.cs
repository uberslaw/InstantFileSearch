namespace InstantFileSearch;

/// <summary>
/// View is the default every launch. Edit is not persisted so a later session
/// cannot stay destructive by accident.
/// </summary>
public static class UiInteractionMode
{
    public const string ViewLabel = "View";
    public const string EditLabel = "Edit";
    public const string DeleteMenuHeader = "Delete…";
    public const string EditBanner =
        "Edit mode — right-click a file in the results list to delete it. Folders are not deleted here. This is not Exclude or Remove from list.";

    public static bool CanDeleteFile(bool isEditMode, bool isScanning, bool isFile) =>
        isEditMode && !isScanning && isFile;
}
