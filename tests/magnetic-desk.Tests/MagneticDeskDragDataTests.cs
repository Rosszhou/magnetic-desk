using MagneticDesk.Services;
using Windows.ApplicationModel.DataTransfer;

namespace MagneticDesk.Tests;

public sealed class MagneticDeskDragDataTests
{
    [Fact]
    public async Task InternalMixedFileAndFolderDrop_PreservesEveryPath()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "magnetic-desk.Tests",
            Guid.NewGuid().ToString("N"));
        string folderPath = Directory.CreateDirectory(
            Path.Combine(tempDirectory, "folder")).FullName;
        string filePath = Path.Combine(tempDirectory, "file.txt");
        File.WriteAllText(filePath, "content");

        try
        {
            var dataPackage = new DataPackage();
            dataPackage.Properties[MagneticDeskDragData.SourcePathsProperty] =
                new[] { filePath, folderPath };

            IReadOnlyList<DroppedFilePath> internalFiles =
                MagneticDeskDragData.GetInternalDroppedFiles(dataPackage.GetView());
            using DroppedFileBatch batch =
                await MagneticDeskDragData.TryGetDroppedFilesAsync(dataPackage.GetView());

            Assert.Equal([filePath, folderPath], internalFiles.Select(file => file.Path));
            Assert.Equal([filePath, folderPath], batch.Files.Select(file => file.Path));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
