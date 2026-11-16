using MagneticDesk.Models;
using MagneticDesk.Views;

namespace MagneticDesk.Tests;

public sealed class CompactPresentationRefreshTests
{
    [Fact]
    public void UnknownPropertyNotification_AlwaysRefreshes()
    {
        Assert.True(ContentWidgetWindow.IsCompactPresentationPropertyRelevant(
            WidgetKind.Todo,
            propertyName: null));
    }
}
